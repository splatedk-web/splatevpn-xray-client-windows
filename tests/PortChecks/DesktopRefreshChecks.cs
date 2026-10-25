using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class DesktopRefreshChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
    public static async Task RunAsync(string root)
    {
        foreach (var region in new[] { "ru", "cn", "ir", "global" })
        {
            var json = new XrayConfigGenerator().GenerateConfig(new ServerNode { Address = "127.0.0.1", Security = "none" }, routingRegion: region,
                shareLan: region == "ru", lanBindAddress: "192.0.2.10", lanAuthEnabled: true, lanPassword: "test-password");
            var config = JsonNode.Parse(json)!;
            var dns = config["dns"]!["servers"]!.ToJsonString();
            Check(region == "ru" ? dns.Contains("77.88.8.8") : !dns.Contains("77.88.8.8"), region + " DNS scoped to region");
            Check(region == "ru" || !config["routing"]!.ToJsonString().Contains("geoip:ru"), region + " no foreign region routes");
            var path = Path.Combine(Path.GetTempPath(), "splate-region-" + Guid.NewGuid() + ".json");
            try
            {
                await File.WriteAllTextAsync(path, json);
                var start = new ProcessStartInfo(Path.Combine(root, "core/xray.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "run", "-test", "-c", path }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(); Check(process.ExitCode == 0, "Xray accepts " + region + ": " + await stdout + await stderr);
            }
            finally { File.Delete(path); }
        }
        Check(SiteRules.Domain("https://Пример.рф/page") == "xn--e1afmkfd.xn--p1ai", "IDN URL normalizes to domain");
        foreach (var invalid in new[] { "127.0.0.1", "example.com\nother.com", "-bad.example", "example.com:443", "https://" })
        { var rejected = false; try { SiteRules.Domain(invalid); } catch (ArgumentException) { rejected = true; } Check(rejected, "invalid domain rejected: " + invalid); }
        var s = new AppSettings { SettingsSchemaVersion = 2, EnableAiBypass = false, AiRoutingMode = "off" };
        s.TemporaryRoutes.Add(new("example.com", "direct", DateTimeOffset.UtcNow.AddMinutes(15)));
        s.TemporaryRoutes.Add(new("expired.example", "block", DateTimeOffset.UtcNow.AddMinutes(-1)));
        var configWithTemporary = new XrayConfigGenerator().GenerateConfig(null, temporaryRoutes: s.TemporaryRoutes);
        Check(configWithTemporary.Contains("domain:example.com") && !configWithTemporary.Contains("expired.example"), "only live temporary rules enter core");
        var activeWithExpired = ConnectionSettings.Signature(s);
        Check(SiteRules.Preview(s, "sub.example.com").Contains("напрямую"), "preview uses generated rule and subdomains");
        SettingsCompatibility.Normalize(s); Check(s.TemporaryRoutes.Count == 1, "expired rules pruned on restore");
        Check(activeWithExpired != ConnectionSettings.Signature(s), "expired active rule requires core reconfiguration");
        Check(new AppSettings().PingMode == "http", "new preferences use Real ping");
        var chain = """{"outbounds":[{"type":"vless","server":"node.example","server_port":443,"uuid":"00000000-0000-0000-0000-000000000001","detour":"missing-hop"}]}""";
        Check(LinkParser.ParseMultiple(chain).Count == 0, "unsupported JSON chain is rejected rather than flattened");
        Check(LinkParser.ParseMultiple(chain.Replace(",\"detour\":\"missing-hop\"", "")).Count == 1, "supported standalone JSON outbound remains importable");
        var lan = JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(null, shareLan: true, lanBindAddress: "192.0.2.10", lanAuthEnabled: true, lanUsername: "splate", lanPassword: "test-password"))!;
        var inbounds = lan["inbounds"]!.AsArray();
        Check(inbounds.Count == 4 && inbounds.Where(i => i!["listen"]!.ToString() == "127.0.0.1").All(i => i!["settings"]?["accounts"] == null), "LAN authentication never changes local client access");
        Check(inbounds.Where(i => i!["listen"]!.ToString() == "192.0.2.10").All(i => i!["settings"]!["accounts"]![0]!["pass"]!.ToString() == "test-password"), "credentials apply only to chosen LAN address");
        var invalidLan = false; try { new XrayConfigGenerator().GenerateConfig(null, shareLan: true, lanAuthEnabled: true, lanPassword: "test-password"); } catch (ArgumentException) { invalidLan = true; }
        Check(invalidLan, "LAN auth requires an explicit interface");
        var packet = new byte[] { 0x12, 0x34, 0x81, 0x80, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, 203, 0, 113, 8 };
        Check(DnsDiagnosticService.ParseResponse(packet, 0x1234).Single() == "203.0.113.8", "DNS response A record parsed");
        foreach (var bad in new[] { packet[..^1], packet.Select((b, i) => i == 3 ? (byte)0x83 : b).ToArray(), new byte[12] })
        { var rejected = false; try { DnsDiagnosticService.ParseResponse(bad, 0x1234); } catch (InvalidDataException) { rejected = true; } Check(rejected, "invalid DNS reply rejected"); }
        var dir = Path.Combine(Path.GetTempPath(), "splate-merge-" + Guid.NewGuid());
        try
        {
            var store = new ServerStorage(dir); store.SaveServers(new() { new() { Id = "existing", Address = "existing.example" } }); store.SaveSettings(new() { Theme = "light" });
            var backup = Path.Combine(dir, "legacy.json");
            await File.WriteAllTextAsync(backup, JsonSerializer.Serialize(new { Servers = new[] { new ServerNode { Id = "existing", Address = "replaced.example" }, new ServerNode { Id = "new", Address = "new.example" } }, Subscriptions = Array.Empty<SubscriptionItem>(), Settings = new AppSettings { Theme = "dark", RoutingRegion = "ir" } }));
            var importer = new ConfigExportImportService(store); await importer.ImportAsync(backup, "", true);
            Check(store.LoadServers().Count == 2 && store.LoadServers().First().Address == "existing.example" && store.LoadSettings().Theme == "light", "merge keeps existing entries and preferences");
            await importer.ImportAsync(backup, "", false);
            Check(store.LoadServers().First().Address == "replaced.example" && store.LoadSettings().RoutingRegion == "ir", "replacement restores source preferences");
            await File.WriteAllTextAsync(backup, "{\"Servers\":null,\"Subscriptions\":[],\"Settings\":{}}");
            var rejected = false; try { await importer.ImportAsync(backup, ""); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && store.LoadServers().Count == 2, "malformed backup preserves catalog");
        }
        finally { Directory.Delete(dir, true); }
        await CheckSubscriptionCancellationAsync();
    }
    private static async Task CheckSubscriptionCancellationAsync()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var headersSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = Task.Run(async () =>
        {
            try
            {
                using var socket = await listener.AcceptTcpClientAsync(cancellation.Token); await using var stream = socket.GetStream();
                var request = new List<byte>(); var one = new byte[1];
                while (request.Count < 16384) { await stream.ReadExactlyAsync(one, cancellation.Token); request.Add(one[0]); if (request.Count >= 4 && request.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break; }
                await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 1024\r\n\r\nabc"), cancellation.Token);
                headersSent.TrySetResult(); await Task.Delay(10000, cancellation.Token);
            }
            catch (OperationCanceledException) { }
        });
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var download = SubscriptionService.FetchSubscriptionAsync($"http://127.0.0.1:{port}/slow", ct: cancellation.Token);
            await headersSent.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation.Cancel();
            var cancelled = false; try { await download; } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && !listener.Pending(), "cancellation interrupts subscription body without retry");
        }
        finally { cancellation.Cancel(); listener.Stop(); await fixture; }
    }
}
