using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public static class PingService
{
    private static readonly string[] TestEndpoints =
    [
        "https://cp.cloudflare.com/generate_204",
        "https://www.gstatic.com/generate_204"
    ];

    /// <summary>
    /// Measures real end-to-end HTTP latency for a single server.
    /// If the node is currently connected, measures via the active local HTTP proxy.
    /// Otherwise, runs an isolated Xray probe. A TCP handshake never qualifies as a working VPN.
    /// </summary>
    public static async Task<long> MeasureRealPingSingleAsync(
        ServerNode node,
        string corePath,
        int? connectedHttpPort = null,
        int timeoutMs = 3500,
        CancellationToken ct = default)
    {
        if (node == null) return 0;

        if (connectedHttpPort.HasValue && connectedHttpPort.Value > 0)
        {
            var latency = await MeasureHttpProxyLatencyAsync(connectedHttpPort.Value, timeoutMs, ct).ConfigureAwait(false);
            if (latency > 0) return latency;
        }

        long result = 0;
        await MeasureRealPingBatchAsync(
            new[] { node },
            corePath,
            connectedHttpPort: null,
            currentlyActiveNode: null,
            timeoutMs: timeoutMs,
            onSingleNodePinged: (n, lat) => result = lat,
            ct: ct
        ).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Measures real HTTP end-to-end latency for multiple servers using an ephemeral Xray instance.
    /// Executes in batches of up to 10 nodes to preserve system resources and prevent socket exhaustion.
    /// </summary>
    public static async Task MeasureRealPingBatchAsync(
        IReadOnlyList<ServerNode> nodes,
        string corePath,
        int? connectedHttpPort = null,
        ServerNode? currentlyActiveNode = null,
        int timeoutMs = 4000,
        Action<ServerNode, long>? onSingleNodePinged = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (nodes == null || nodes.Count == 0) return;

        bool hasXrayCore = !string.IsNullOrWhiteSpace(corePath) && File.Exists(corePath);

        // TCP reachability is not a verified working tunnel.
        if (!hasXrayCore)
        {
            foreach (var node in nodes) onSingleNodePinged?.Invoke(node, 0);
            return;
        }

        var route = await ProbeRoute.CaptureAsync(ct).ConfigureAwait(false);
        // Process in batches
        const int batchSize = 10;
        for (int i = 0; i < nodes.Count; i += batchSize)
        {
            if (ct.IsCancellationRequested) break;
            var batch = nodes.Skip(i).Take(batchSize).ToList();
            await MeasureSingleBatchInternalAsync(batch, corePath, connectedHttpPort, currentlyActiveNode, timeoutMs, onSingleNodePinged, route, ct).ConfigureAwait(false);
        }
    }

    private static async Task MeasureSingleBatchInternalAsync(
        List<ServerNode> batch,
        string corePath,
        int? connectedHttpPort,
        ServerNode? currentlyActiveNode,
        int timeoutMs,
        Action<ServerNode, long>? onSingleNodePinged,
        ProbeRoute route,
        CancellationToken ct)
    {
        route.Validate();
        var probeNodes = new List<(ServerNode Node, int Port)>();
        var allocatedPorts = GetAvailablePorts(batch.Count);

        for (int i = 0; i < batch.Count; i++)
        {
            var node = batch[i];
            if (currentlyActiveNode != null && node.Id == currentlyActiveNode.Id && connectedHttpPort.HasValue && connectedHttpPort.Value > 0)
            {
                // Measure connected server directly
                var activeLat = await MeasureHttpProxyLatencyAsync(connectedHttpPort.Value, timeoutMs, ct).ConfigureAwait(false);
                onSingleNodePinged?.Invoke(node, activeLat > 0 ? activeLat : 0);
            }
            else
            {
                probeNodes.Add((node, allocatedPorts[i]));
            }
        }

        if (probeNodes.Count == 0) return;

        var configRoot = BuildProbeConfig(probeNodes, route.Uplink?.Name);

        var tempConfigPath = Path.Combine(Path.GetTempPath(), $"splate_probe_{Guid.NewGuid():N}.json");
        Process? probeProc = null;
        using var probeJob = new ChildProcessJob();

        try
        {
            await File.WriteAllTextAsync(tempConfigPath, configRoot.ToJsonString(), ct).ConfigureAwait(false);

            var psi = new ProcessStartInfo
            {
                FileName = corePath,
                Arguments = $"run -c \"{tempConfigPath}\"",
                WorkingDirectory = Path.GetDirectoryName(corePath) ?? "",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            probeProc = Process.Start(psi);
            if (probeProc == null || probeProc.HasExited)
            {
                // Core did not start: never substitute an open TCP port for a working VPN.
                await ReportUnverifiedBatchAsync(probeNodes.Select(p => p.Node), timeoutMs, onSingleNodePinged, ct).ConfigureAwait(false);
                return;
            }

            probeJob.Attach(probeProc);
            probeProc.OutputDataReceived += (_, _) => { };
            probeProc.ErrorDataReceived += (_, _) => { };
            probeProc.BeginOutputReadLine();
            probeProc.BeginErrorReadLine();

            // Allow slow machines to initialize the isolated core before HTTP checks.
            var firstPort = probeNodes[0].Port;
            bool ready = false;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                if (probeProc.HasExited) break;
                if (await IsPortListeningAsync(firstPort, 50).ConfigureAwait(false))
                {
                    ready = true;
                    break;
                }
                await Task.Delay(40, ct).ConfigureAwait(false);
            }

            if (!ready)
            {
                await ReportUnverifiedBatchAsync(probeNodes.Select(p => p.Node), timeoutMs, onSingleNodePinged, ct).ConfigureAwait(false);
                return;
            }

            // Measure each server through its designated local HTTP proxy inbound
            var tasks = probeNodes.Select(async item =>
            {
                long latency = await MeasureHttpProxyLatencyAsync(item.Port, timeoutMs, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested(); route.Validate();
                onSingleNodePinged?.Invoke(item.Node, latency > 0 ? latency : 0);
            }).ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested(); route.Validate();
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            await ReportUnverifiedBatchAsync(probeNodes.Select(p => p.Node), timeoutMs, onSingleNodePinged, ct).ConfigureAwait(false);
        }
        finally
        {
            if (probeProc != null)
            {
                try
                {
                    if (!probeProc.HasExited)
                    {
                        probeProc.Kill(true);
                        probeProc.WaitForExit(500);
                    }
                }
                catch { }
                finally
                {
                    probeProc.Dispose();
                }
            }

            try
            {
                if (File.Exists(tempConfigPath)) File.Delete(tempConfigPath);
            }
            catch { }
        }
    }

    internal static JsonObject BuildProbeConfig(IReadOnlyList<(ServerNode Node, int Port)> probeNodes, string? uplinkInterface)
    {
        var configRoot = new JsonObject
        {
            ["log"] = new JsonObject { ["loglevel"] = "warning" }
        };

        var inbounds = new JsonArray();
        var outbounds = new JsonArray();
        var rules = new JsonArray();
        var hosts = new JsonObject();
        if (!string.IsNullOrWhiteSpace(uplinkInterface)) { hosts["cloudflare-dns.com"] = new JsonArray { "1.1.1.1", "1.0.0.1" }; hosts["dns.google"] = new JsonArray { "8.8.8.8", "8.8.4.4" }; }

        for (int i = 0; i < probeNodes.Count; i++)
        {
            var item = probeNodes[i];
            var inTag = $"probe-in-{i}";
            var outTag = $"probe-out-{i}";

            inbounds.Add(new JsonObject
            {
                ["tag"] = inTag,
                ["listen"] = "127.0.0.1",
                ["port"] = item.Port,
                ["protocol"] = "http"
            });

            var outboundObj = XrayConfigGenerator.BuildProxyOutbound(item.Node, tag: outTag);
            var addressAlias = XrayConfigGenerator.ServerAddressAlias(item.Node);
            if (addressAlias != null) hosts[addressAlias] = item.Node.Address;
            if (!string.IsNullOrWhiteSpace(uplinkInterface)) BindOutbound(outboundObj, uplinkInterface);
            outbounds.Add(outboundObj);

            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray { inTag },
                ["outboundTag"] = outTag
            });
        }

        var direct = new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom" };
        if (!string.IsNullOrWhiteSpace(uplinkInterface)) BindOutbound(direct, uplinkInterface);
        outbounds.Add(direct);
        rules.Insert(0, new JsonObject { ["type"] = "field", ["inboundTag"] = new JsonArray { "probe-dns" }, ["outboundTag"] = "direct" });

        configRoot["inbounds"] = inbounds;
        configRoot["dns"] = new JsonObject { ["hosts"] = hosts, ["servers"] = string.IsNullOrWhiteSpace(uplinkInterface) ? new JsonArray { "localhost" } : new JsonArray { "https://cloudflare-dns.com/dns-query", "https://dns.google/dns-query" }, ["enableParallelQuery"] = true, ["queryStrategy"] = string.IsNullOrWhiteSpace(uplinkInterface) ? "UseIP" : "UseIPv4", ["tag"] = "probe-dns" };
        configRoot["outbounds"] = outbounds;
        configRoot["routing"] = new JsonObject
        {
            ["domainStrategy"] = "AsIs",
            ["rules"] = rules
        };

        return configRoot;
    }
    private static void BindOutbound(JsonObject outbound, string name)
    {
        var stream = outbound["streamSettings"] as JsonObject;
        if (stream == null) outbound["streamSettings"] = stream = new JsonObject();
        var options = stream["sockopt"] as JsonObject;
        if (options == null) stream["sockopt"] = options = new JsonObject();
        // Force forbids a fallback to the OS resolver, which is captured by TUN.
        options["interface"] = name; options["domainStrategy"] = "ForceIPv4";
    }

    private static async Task ReportUnverifiedBatchAsync(
        IEnumerable<ServerNode> nodes,
        int timeoutMs,
        Action<ServerNode, long>? onSingleNodePinged,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var node in nodes) onSingleNodePinged?.Invoke(node, 0);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Measures TCP handshake latency to the server host and port.
    /// Uses low-level Socket with Linger=0 to prevent SocketException 995 / UnobservedTaskException crashes.
    /// </summary>
    public static Task<long> MeasureTcpLatencyAsync(string host, int port, int timeoutMs = 2500) => MeasureTcpOnRouteAsync(host, port, timeoutMs, null);
    internal static async Task<long> MeasureTcpOnRouteAsync(string host, int port, int timeoutMs, ProbeRoute? probeRoute, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535) return -1;

        var sw = Stopwatch.StartNew();
        Socket? socket = null;
        try
        {
            var route = probeRoute ?? await ProbeRoute.CaptureAsync(token).ConfigureAwait(false);
            route.Validate();
            using var dnsCts = CancellationTokenSource.CreateLinkedTokenSource(token); dnsCts.CancelAfter(timeoutMs);
            var addresses = await route.ResolveAsync(host.Trim(), dnsCts.Token).ConfigureAwait(false);
            var targetIp = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();

            if (targetIp == null) return -1;

            socket = new Socket(targetIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                Blocking = false,
                NoDelay = true,
                LingerState = new LingerOption(true, 0)
            };

            if (!IPAddress.IsLoopback(targetIp)) route.Bind(socket);
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token); connectCts.CancelAfter(timeoutMs);
            sw.Restart();
            try
            {
                await socket.ConnectAsync(new IPEndPoint(targetIp, port), connectCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return -1;
            }
            catch (SocketException)
            {
                return -1;
            }

            sw.Stop(); route.Validate();
            return Math.Max(1, sw.ElapsedMilliseconds);
        }
        catch
        {
            return -1;
        }
        finally
        {
            if (socket != null)
            {
                try
                {
                    if (socket.Connected)
                    {
                        socket.Shutdown(SocketShutdown.Both);
                    }
                }
                catch { }

                try
                {
                    socket.Close(0);
                    socket.Dispose();
                }
                catch { }
            }
        }
    }

    /// <summary>
    /// Measures real end-to-end HTTP latency through the specified local HTTP proxy port.
    /// </summary>
    public static Task<long> MeasureHttpProxyLatencyAsync(int proxyPort, int timeoutMs = 3500, CancellationToken token = default) =>
        MeasureProxyLatencyToAsync(proxyPort, TestEndpoints, timeoutMs, token);

    internal static async Task<long> MeasureProxyLatencyToAsync(int proxyPort, IReadOnlyList<string> endpoints, int timeoutMs, CancellationToken token)
    {
        if (proxyPort <= 0 || proxyPort > 65535) return -1;
        token.ThrowIfCancellationRequested();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(timeoutMs);
        var pending = endpoints.Select(CheckEndpointAsync).ToList();
        try
        {
            while (pending.Count > 0)
            {
                var done = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(done);
                var latency = await done.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (latency > 0) return latency;
            }
            return -1;
        }
        finally
        {
            lifetime.Cancel();
            await Task.WhenAll(pending).ConfigureAwait(false);
        }

        async Task<long> CheckEndpointAsync(string endpoint)
        {
            try
            {
                using var handler = new SocketsHttpHandler
                {
                    Proxy = new WebProxy($"http://127.0.0.1:{proxyPort}"),
                    UseProxy = true,
                    ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMs),
                    PooledConnectionLifetime = TimeSpan.FromSeconds(5)
                    ,
                    AllowAutoRedirect = false
                };

                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromMilliseconds(timeoutMs)
                };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                var sw = Stopwatch.StartNew();
                using var response = await client.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, lifetime.Token).ConfigureAwait(false);
                sw.Stop();

                if ((int)response.StatusCode == 204)
                {
                    return Math.Max(1, sw.ElapsedMilliseconds);
                }
            }
            catch
            {
                // A failed endpoint never overrides another successful endpoint.
            }
            return -1;
        }
    }

    private static List<int> GetAvailablePorts(int count)
    {
        var ports = new List<int>();
        var listeners = new List<TcpListener>();
        try
        {
            for (int i = 0; i < count; i++)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                ports.Add(port);
                listeners.Add(listener);
            }
        }
        finally
        {
            foreach (var l in listeners)
            {
                try { l.Stop(); } catch { }
            }
        }
        return ports;
    }

    private static async Task<bool> IsPortListeningAsync(int port, int timeoutMs = 150)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
                LingerState = new LingerOption(true, 0)
            };
            using var cts = new CancellationTokenSource(timeoutMs);
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), cts.Token).ConfigureAwait(false);
            return socket.Connected;
        }
        catch
        {
            return false;
        }
    }
}
