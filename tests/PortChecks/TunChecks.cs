using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class TunChecks
{
    public static async Task RunAsync(string root)
    {
        if (!TunDeviceManager.IsAdministrator()) throw new Exception("TUN checks require an administrator shell.");
        var adapter = "SplateProbe" + Guid.NewGuid().ToString("N")[..8];
        var httpPort = FreePort(); var socksPort = FreePort(); var coreHttpPort = FreePort();
        var payload = "isolated-tun-payload";
        using var http = new HttpListener();
        http.Prefixes.Add($"http://127.0.0.1:{httpPort}/"); http.Start();
        using var fixtureCancel = new CancellationTokenSource();
        var responder = Task.Run(async () =>
        {
            while (!fixtureCancel.IsCancellationRequested)
            {
                try
                {
                    var context = await http.GetContextAsync().WaitAsync(fixtureCancel.Token);
                    var bytes = Encoding.UTF8.GetBytes(payload);
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
                catch (OperationCanceledException) { break; }
                catch (HttpListenerException) when (fixtureCancel.IsCancellationRequested) { break; }
            }
        });
        var config = JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(new ServerNode { Address = "127.0.0.1", Security = "none", Uuid = "00000000-0000-0000-0000-000000000001" }, mode: "global", enableSniffing: false, blockIpv6: false, socksPort: socksPort, httpPort: coreHttpPort))!;
        config["outbounds"]![0] = new JsonObject { ["tag"] = "proxy", ["protocol"] = "freedom", ["settings"] = new JsonObject { ["redirect"] = $"127.0.0.1:{httpPort}" } };
        config["routing"]!["rules"] = new JsonArray
        {
            new JsonObject { ["type"] = "field", ["ip"] = new JsonArray { "192.0.2.2/32", "2001:db8:5350::2/128" }, ["outboundTag"] = "proxy" },
            new JsonObject { ["type"] = "field", ["network"] = "tcp,udp", ["outboundTag"] = "block" }
        };
        var core = new XrayProcessManager(root);
        Process? tun = null;
        using var job = new ChildProcessJob();
        var ownedRoutes = new List<(string Family, string Prefix)>();
        Task<string>? output = null, errors = null;
        try
        {
            if (!await core.StartAsync(config.ToJsonString(), coreHttpPort, socksPort)) throw new Exception("Fixture core failed");
            var psi = TunDeviceManager.BuildStartInfo(Path.Combine(root, "core"), adapter, socksPort);
            if (psi.ArgumentList[1] != "tun://" + adapter) throw new Exception("Invalid TUN driver syntax");
            tun = Process.Start(psi)!; job.Attach(tun);
            output = tun.StandardOutput.ReadToEndAsync(); errors = tun.StandardError.ReadToEndAsync();
            for (var i = 0; i < 100; i++)
            {
                if (tun.HasExited) throw new Exception("Adapter creation failed: " + await errors);
                if (NetworkInterface.GetAllNetworkInterfaces().Any(n => n.Name == adapter)) break;
                await Task.Delay(100);
            }
            if (!NetworkInterface.GetAllNetworkInterfaces().Any(n => n.Name == adapter)) throw new Exception("Test adapter did not appear");
            await Netsh("interface", "ipv4", "set", "address", $"name={adapter}", "source=static", "address=192.0.2.1", "mask=255.255.255.252", "gateway=none");
            await Netsh("interface", "ipv4", "set", "dnsservers", $"name={adapter}", "source=static", "address=1.1.1.1", "register=none", "validate=no");
            await Netsh("interface", "ipv4", "set", "interface", $"interface={adapter}", "metric=1");
            await Netsh("interface", "ipv6", "add", "address", adapter, "2001:db8:5350::1/64", "store=active");
            // Only documentation test destinations: never install a default route or change the user's proxy.
            foreach (var route in new[] { ("ipv4", "192.0.2.2/32"), ("ipv6", "2001:db8:5350::2/128") })
            {
                await Netsh("interface", route.Item1, "add", "route", $"prefix={route.Item2}", $"interface={adapter}", route.Item1 == "ipv4" ? "nexthop=0.0.0.0" : "nexthop=::", "metric=1", "store=active");
                ownedRoutes.Add(route);
            }
            await TunDeviceManager.WaitForAddressesAsync(adapter, "192.0.2.1", "2001:db8:5350::1");
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.Host = $"127.0.0.1:{httpPort}";
            foreach (var address in new[] { "192.0.2.2", "[2001:db8:5350::2]" })
            {
                var body = await client.GetStringAsync($"http://{address}:{httpPort}/tun-check");
                if (body != payload) throw new Exception("TUN payload mismatch: " + address);
            }
            Console.WriteLine("PASS real Wintun adapter: IPv4/IPv6 HTTP payload via tun2socks -> local SOCKS -> local HTTP; no default routes");
        }
        finally
        {
            foreach (var route in ownedRoutes.AsEnumerable().Reverse())
                await Netsh("interface", route.Family, "delete", "route", $"prefix={route.Prefix}", $"interface={adapter}", "store=active");
            if (tun != null)
            {
                if (!tun.HasExited) tun.Kill(true);
                await tun.WaitForExitAsync();
                if (output != null) await output;
                if (errors != null) { var stderr = await errors; if (!string.IsNullOrWhiteSpace(stderr)) Console.WriteLine(stderr[..Math.Min(stderr.Length, 1000)]); }
                tun.Dispose();
            }

            core.Stop(); fixtureCancel.Cancel(); http.Stop(); await responder;
        }
    }

    static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    static Task<string> Netsh(params string[] args) => TunDeviceManager.RunAsync("netsh.exe", args);
}
