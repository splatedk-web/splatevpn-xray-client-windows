using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class AiDnsChecks
{
    public static async Task ProbeXboxAsync(string root, bool modern = false)
    {
        var uplink = await TunDeviceManager.GetUplinkAsync();
        var httpPort = FreePort(); var socksPort = FreePort();
        var config = new XrayConfigGenerator().GenerateConfig(new ServerNode { Address = "127.0.0.1", Port = 9, Security = "none", Uuid = "00000000-0000-0000-0000-000000000001" }, enableAiXboxDns: !modern, enableAiDns:modern, uplinkInterface: uplink.Name, httpPort: httpPort, socksPort: socksPort);
        var debugConfig = JsonNode.Parse(config)!; debugConfig["log"]!["loglevel"] = "debug"; config = debugConfig.ToJsonString();
        var core = new XrayProcessManager(root);
        core.OnLogMessage += line => { if (line.Contains("dns-xbox") || line.Contains("ai-smartdns") || line.Contains("chatgpt.com") || line.Contains("gemini.google.com") || line.Contains("DNS: got answer")) Console.WriteLine(line); };
        try
        {
            if (!await core.StartAsync(config, httpPort, socksPort)) throw new Exception("Probe core failed");
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = true, Proxy = new WebProxy($"http://127.0.0.1:{httpPort}"), AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36");
            foreach (var domain in new[] { "chatgpt.com", "gemini.google.com" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://" + domain + "/");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                Console.WriteLine($"DIRECT {(modern ? "DNS-AI" : "XBOX DNS")} {domain}: HTTP {(int)response.StatusCode}");
            }
        }
        finally { core.Stop(); }
    }

    public static async Task RunAsync(string root, bool modern = false)
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var aiDns = new TcpListener(IPAddress.Loopback, 0); aiDns.Start();
        var defaultDns = new TcpListener(IPAddress.Loopback, 0); defaultDns.Start();
        var vpn = new TcpListener(IPAddress.Loopback, 0); vpn.Start();
        var target = new TcpListener(IPAddress.Loopback, 0); target.Start();
        var aiQueries = new List<string>(); var defaultQueries = new List<string>();
        var aiWorker = ServeDns(aiDns, aiQueries, new byte[] { 127, 0, 0, 1 }, cancel.Token);
        var defaultWorker = ServeDns(defaultDns, defaultQueries, new byte[] { 127, 0, 0, 2 }, cancel.Token);
        var vpnConnection = vpn.AcceptTcpClientAsync(cancel.Token).AsTask();
        var targetWorker = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                using var connection = await target.AcceptTcpClientAsync(cancel.Token);
                var stream = connection.GetStream();
                var request = new byte[8192]; await stream.ReadAsync(request, cancel.Token);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"), cancel.Token);
            }
        });
        var httpPort = FreePort(); var socksPort = FreePort(); var metricsPort = FreePort();
        var node = new ServerNode { Address = "127.0.0.1", Port = ((IPEndPoint)vpn.LocalEndpoint).Port, Security = "none", Uuid = "00000000-0000-0000-0000-000000000001" };
        var config = JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(node, enableAiBypass: true, enableSniffing: false, enableAiXboxDns: !modern, enableAiDns:modern, httpPort: httpPort, socksPort: socksPort, metricsPort: metricsPort))!;
        var dnsServers = config["dns"]!["servers"]!.AsArray();
        dnsServers.OfType<JsonObject>().Single(d => d["domains"]?.ToJsonString().Contains("chatgpt.com") == true)["address"] = $"tcp://127.0.0.1:{((IPEndPoint)aiDns.LocalEndpoint).Port}";
        dnsServers.OfType<JsonObject>().Last()["address"] = $"tcp://127.0.0.1:{((IPEndPoint)defaultDns.LocalEndpoint).Port}";
        var core = new XrayProcessManager(root);
        try
        {
            if (!await core.StartAsync(config.ToJsonString(), httpPort, socksPort, cancel.Token)) throw new Exception("AI DNS fixture core failed");
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = true, Proxy = new WebProxy($"http://127.0.0.1:{httpPort}") }) { Timeout = TimeSpan.FromSeconds(8) };
            var targetPort = ((IPEndPoint)target.LocalEndpoint).Port;
            if (await client.GetStringAsync($"http://chatgpt.com:{targetPort}/ai-dns-check", cancel.Token) != "OK") throw new Exception("OpenAI DNS payload failed");
            using var monitor = new TrafficMonitorService();
            var baseline = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var counted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            monitor.SessionBytesUpdated += (down, up) => { baseline.TrySetResult(true); if (down > 0 && up > 0) counted.TrySetResult(true); };
            monitor.Start(metricsPort);
            await baseline.Task.WaitAsync(TimeSpan.FromSeconds(4));
            if (await client.GetStringAsync($"http://gemini.google.com:{targetPort}/ai-dns-check", cancel.Token) != "OK") throw new Exception("Gemini DNS payload failed");
            await counted.Task.WaitAsync(TimeSpan.FromSeconds(4)); monitor.Stop();
            await targetWorker;
            if (!aiQueries.Contains("chatgpt.com") || !aiQueries.Contains("gemini.google.com") || defaultQueries.Count != 0) throw new Exception("Selective AI DNS or fallback isolation failed");
            if (vpnConnection.IsCompletedSuccessfully) throw new Exception("Smart DNS traffic unexpectedly reached the VPN server");
            Console.WriteLine("PASS real Xray Smart DNS: OpenAI/Gemini resolver and data bypass VPN; no fallback; AI traffic counted");
        }
        finally
        {
            core.Stop(); cancel.Cancel(); aiDns.Stop(); defaultDns.Stop(); vpn.Stop(); target.Stop();
            await Task.WhenAll(aiWorker, defaultWorker);
            try { using var unexpected = await vpnConnection; } catch (OperationCanceledException) { }
        }
    }

    static async Task ServeDns(TcpListener listener, List<string> queries, byte[] ip, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var connection = await listener.AcceptTcpClientAsync(ct);
                var stream = connection.GetStream();
                var size = new byte[2]; await stream.ReadExactlyAsync(size, ct);
                var query = new byte[(size[0] << 8) | size[1]]; await stream.ReadExactlyAsync(query, ct);
                var cursor = 12; var labels = new List<string>();
                while (query[cursor] != 0) { var length = query[cursor++]; labels.Add(Encoding.ASCII.GetString(query, cursor, length)); cursor += length; }
                cursor += 5; queries.Add(string.Join(".", labels));
                var response = new byte[cursor + 16]; Array.Copy(query, response, cursor);
                response[2] = 0x81; response[3] = 0x80; response[6] = 0; response[7] = 1; response[8] = response[9] = response[10] = response[11] = 0;
                new byte[] { 0xc0, 0x0c, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4 }.CopyTo(response, cursor); ip.CopyTo(response, cursor + 12);
                await stream.WriteAsync(new byte[] { (byte)(response.Length >> 8), (byte)response.Length }, ct); await stream.WriteAsync(response, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (SocketException) when (ct.IsCancellationRequested) { }
    }
    static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
}
