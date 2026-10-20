using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using SplateClientDesktop.Services;
using SplateClientDesktop.Models;

if (args.Length == 1 && args[0] == "--audit-fixes-only")
{
    await StorageChecks.RunAsync();
    await AuditFixChecks.RunAsync();
    return;
}

if (args.Length == 1 && args[0] == "--node-ping-live")
{
    await NodePingLiveChecks.RunAsync(); return;
}
if (args.Length == 1 && args[0] == "--viewmodel-only") { await ViewModelChecks.RunAsync(); return; }
if (args.Length == 1 && args[0] == "--sandbox-balancer-check")
{
    await SandboxUplinkChecks.RunBalancerAsync(); return;
}
if (args.Length == 1 && args[0] == "--sandbox-full-tun-check")
{
    await SandboxUplinkChecks.RunAsync(); return;
}
if (args.Length == 2 && args[0] == "--gemini-probe-render-live")
{
    await GeminiRenderChecks.RunAsync(args[1]); return;
}
if (args.Length == 1 && args[0] == "--gemini-render-fixture-only")
{
    await GeminiRenderChecks.RunAsync(); return;
}
if (args.Length == 4 && args[0] == "--crash-owner")
{
    var owner = new XrayProcessManager(args[1]);
    var c = new XrayConfigGenerator().GenerateConfig(new ServerNode { Address = "127.0.0.1", Security = "none", Uuid = "00000000-0000-0000-0000-000000000001" }, httpPort: int.Parse(args[2]), socksPort: int.Parse(args[3]));
    if (!await owner.StartAsync(c, int.Parse(args[2]), int.Parse(args[3]))) Environment.Exit(2);
    Console.WriteLine("OWNER_READY");
    Process.GetCurrentProcess().Kill();
    return;
}
if (args.Length == 2 && args[0] == "--speedtest-live-only")
{
    var result = await new SpeedTestService(diagnostics: Console.WriteLine).RunAsync(int.Parse(args[1]), s => Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(s)), CancellationToken.None);
    if (!result.Done || result.Bytes <= 0 || result.Mbps <= 0) throw new Exception("No valid speed sample");
    return;
}
if (args.Length == 2 && args[0] == "--ai-probe-live")
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(await new AiDnsAutoSelector().ProbeAsync(new(args[1], false), CancellationToken.None))); return;
}
if (args.Length == 1 && args[0] == "--ai-selection-live-only")
{
    var winner = await new AiDnsAutoSelector().RunAsync(state => Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(state)), _ => Task.FromResult(true), CancellationToken.None);
    Console.WriteLine("QA only: preferences were not modified. Confirmed provider: " + winner?.Choice.ProviderKey);
    return;
}
if (args.Length == 1 && args[0] == "--ad-filter-live-only")
{
    if (!await AdFilterService.RefreshAsync(AppContext.BaseDirectory, "", null)) throw new Exception(AdFilterService.State.Status);
    var matcher = await AdFilterService.PrepareAsync(AppContext.BaseDirectory, "");
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { matcher, AdFilterService.State }));
    return;
}
if (args.Length == 1 && args[0] == "--updates-network-probe")
{
    foreach (var useProxy in new[] { true, false })
    {
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = useProxy }) { Timeout = TimeSpan.FromSeconds(15) };
        var clock = Stopwatch.StartNew();
        var info = await UpdateCheckerService.CheckForUpdatesAsync("2.10.0", client);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {useProxy,seconds=clock.Elapsed.TotalSeconds,info}));
    }
    return;
}
if (args.Length == 1 && args[0] == "--updates-live-only")
{
    var update = await UpdateCheckerService.CheckForUpdatesAsync("2.10.0");
    if (!update.CheckSucceeded || !update.IsUpdateAvailable) throw new Exception("Live update check failed: " + update.ErrorMessage);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(update));
    return;
}
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
AlgorithmChecks.Run(root);
await AndroidRefreshChecks.RunAsync(root);
await RouteChecks.RunAsync();
await BalancerProbeChecks.RunAsync(root);
BalancerChecks.Run();
await SpeedTestChecks.RunAsync();
await UpdateChecks.RunAsync();
await ViewModelChecks.RunAsync();
if (args.Length == 1 && args[0] == "--tun-only")
{
    await TunChecks.RunAsync(root);
    return;
}
if (args.Length == 1 && args[0] == "--ai-dns-only")
{
    await AiDnsChecks.RunAsync(root);
    await AiDnsChecks.RunAsync(root,modern:true);
    return;
}
if (args.Length == 1 && args[0] == "--xbox-probe")
{
    await AiDnsChecks.ProbeXboxAsync(root);
    return;
}
if (args.Length == 1 && args[0] == "--dns-ai-probe")
{
    await AiDnsChecks.ProbeXboxAsync(root,modern:true);
    return;
}
if (args.Length == 1 && args[0] == "--storage-only")
{
    await StorageChecks.RunAsync();
    return;
}
await StorageChecks.RunAsync();
var core = Path.Combine(root, "core", "xray.exe");
var gen = new XrayConfigGenerator();
var node = new ServerNode { Address = "127.0.0.1", Port = 443, Uuid = "00000000-0000-0000-0000-000000000001", Security = "none", Network = "tcp", Flow = "" };
var cases = new List<(string Name, string Json)>();
cases.Add(("VLESS/TCP proxy", gen.GenerateConfig(node)));
cases.Add(("Selective AI Xbox DNS", gen.GenerateConfig(node, enableAiXboxDns: true)));
cases.Add(("AI Xbox DNS with FakeDNS", gen.GenerateConfig(node, enableAiXboxDns: true, isFakeDns: true, enableTun: true)));
cases.Add(("AI DNS-AI", gen.GenerateConfig(node, enableAiDns:true)));
cases.Add(("Custom DoH and ad blocking", gen.GenerateConfig(node, dnsProvider:"custom",customDohUrl:"https://dns.google/dns-query",enableAdBlock:true,customBlockRules:"ads.example.com")));
cases.Add(("VLESS/TCP TUN", gen.GenerateConfig(node, enableTun: true, uplinkInterface: "Ethernet")));
cases.Add(("FakeDNS TUN", gen.GenerateConfig(node, enableTun: true, isFakeDns: true)));
node.Security = "tls"; node.Sni = "cdn.example.com"; node.Network = "ws";
cases.Add(("TLS/WebSocket", gen.GenerateConfig(node)));
node.Network = "xhttp";
cases.Add(("TLS/XHTTP", gen.GenerateConfig(node)));
cases.Add(("TLS fragment", gen.GenerateConfig(node, enableTlsFragment: true)));
cases.Add(("TLS fragment aggressive", gen.GenerateConfig(node, enableTlsFragment:true,tlsFragmentLength:"50-100",tlsFragmentInterval:"20-40",tlsFragmentPackets:"1-3")));
node.Network="tcp";
cases.Add(("Mux TCP",gen.GenerateConfig(node,enableMux:true)));
cases.Add(("LAN proxy",gen.GenerateConfig(node,shareLan:true)));
node.Network = "tcp"; node.Security = "reality"; node.PublicKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
cases.Add(("Reality/TCP", gen.GenerateConfig(node)));
node.Protocol = "trojan"; node.Security = "tls"; node.Uuid = "test-password";
cases.Add(("Trojan", gen.GenerateConfig(node, mode: "global")));
node.Protocol = "shadowsocks"; node.Security = "none"; node.Encryption = "aes-256-gcm";
cases.Add(("Shadowsocks", gen.GenerateConfig(node)));
node.Protocol = "vmess"; node.Uuid = "00000000-0000-0000-0000-000000000001"; node.Security = "tls";
cases.Add(("VMess", gen.GenerateConfig(node)));
foreach (var (name, json) in cases)
{
    var tree = JsonNode.Parse(json)!;
    var inbounds = tree["inbounds"]!.AsArray();
    Check(inbounds.Count == 2 && inbounds.Select(i => i!["port"]!.GetValue<int>()).Distinct().Count() == 2, name + ": unique ports");
    Check(inbounds.All(i => i!["sniffing"]!["metadataOnly"]!.GetValue<bool>() == false), name + ": sniffing enabled");
    Check(tree["routing"]!["rules"]!.AsArray().All(r => r!["process"] == null), name + ": no unsupported process rules");
    var ai = tree["routing"]!["rules"]!.AsArray().FirstOrDefault(r => r!["domain"] is JsonArray domains &&
        domains.Any(d => d?.ToString() is "domain:openai.com" or "full:openai.com"));
    Check(ai?["outboundTag"]?.GetValue<string>() == (name.Contains("Xbox DNS") || name=="AI DNS-AI" ? "ai-smartdns" : "proxy"), name + ": OpenAI uses correct VPN outbound");
    var proxy = tree["outbounds"]![0]!;
    if (proxy["protocol"]!.GetValue<string>() == "vless")
        Check(proxy["settings"]!["vnext"]![0]!["users"]![0]!["flow"] == null, name + ": flow not invented");
    if (name == "TLS/WebSocket") Check(proxy["streamSettings"]!["tlsSettings"]!["verifyPeerCertByName"] == null, "SNI certificate identity preserved");
    var path = Path.Combine(Path.GetTempPath(), $"splate-check-{Guid.NewGuid():N}.json");
    try
    {
        File.WriteAllText(path, json);
        var psi = new ProcessStartInfo(core) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(core) };
        foreach (var arg in new[] { "run", "-test", "-c", path }) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        Check(p.ExitCode == 0, name + ": core validation\n" + await stdout + await stderr);
        Console.WriteLine("PASS " + name);
    }
    finally { File.Delete(path); }
}
var enabledAiDns = JsonNode.Parse(gen.GenerateConfig(node, enableAiXboxDns: true))!;
var matchedAiDns = enabledAiDns["dns"]!["servers"]!.AsArray().OfType<JsonObject>().Single(d => d["domains"]?.ToJsonString().Contains("domain:chatgpt.com") == true);
Check(matchedAiDns["address"]!.GetValue<string>() == "https://xbox-dns.ru/dns-query" && matchedAiDns["skipFallback"]!.GetValue<bool>(), "AI uses Xbox DoH without fallback");
Check(matchedAiDns["domains"]!.AsArray().Any(d => d!.GetValue<string>() == "domain:gemini.google.com") && !matchedAiDns["domains"]!.AsArray().Any(d => d!.GetValue<string>() == "domain:google.com"), "selective Gemini domains");
Check(enabledAiDns["dns"]!["servers"]!.AsArray().OfType<JsonObject>().Last()["address"]!.GetValue<string>() == "1.1.1.1", "main DNS retained");
var aiDirect = enabledAiDns["outbounds"]!.AsArray().Single(o => o!["tag"]!.GetValue<string>() == "ai-smartdns")!;
Check(aiDirect["protocol"]!.GetValue<string>() == "freedom" && aiDirect["streamSettings"]!["sockopt"]!["domainStrategy"]!.GetValue<string>() == "ForceIPv4", "AI Smart DNS bypasses VPN and forces IPv4");
Check(enabledAiDns["routing"]!["rules"]!.AsArray().Any(r => r!["inboundTag"]?.ToJsonString().Contains("dns-xbox") == true && r["outboundTag"]!.GetValue<string>() == "direct"), "Xbox DNS query bypasses VPN");
var aiTun = JsonNode.Parse(gen.GenerateConfig(node, enableAiXboxDns: true, enableTun: true, uplinkInterface: "Ethernet"))!["outbounds"]!.AsArray().Single(o => o!["tag"]!.GetValue<string>() == "ai-smartdns")!;
Check(aiTun["streamSettings"]!["sockopt"]!["interface"]!.GetValue<string>() == "Ethernet" && aiTun["streamSettings"]!["sockopt"]!["domainStrategy"]!.GetValue<string>() == "ForceIPv4", "TUN preserves forced IPv4 and binds physical uplink");
Check(!JsonNode.Parse(gen.GenerateConfig(node))!["outbounds"]!.AsArray().Any(o => o!["tag"]!.GetValue<string>() == "ai-smartdns"), "disabled mode preserves original outbounds");
Expect<ArgumentException>(() => gen.GenerateConfig(node, socksPort: 10809), "duplicate ports rejected");
Check(JsonNode.Parse(gen.GenerateConfig(node, isFakeDns: true))!["fakedns"] == null, "legacy FakeDNS disabled in proxy mode");
Check(JsonNode.Parse(gen.GenerateConfig(node, perAppProxyList: "codex.exe", perAppDirectList: "bank.exe"))!["routing"]!["rules"]!.AsArray().All(r => r!["process"] == null), "legacy app lists do not block config");
var migrationDir = Path.Combine(Path.GetTempPath(), "splate-migration-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(migrationDir);
try
{
    var original = "{\"IsFakeDns\":true,\"EnableTunMode\":false,\"PerAppProxyList\":\"codex.exe\",\"PerAppDirectList\":\"bank.exe\",\"CustomProxyRules\":\"example.com\"}";
    File.WriteAllText(Path.Combine(migrationDir, "settings.json"), original);
    var migrated = new ServerStorage(migrationDir).LoadSettings();
    Check(!migrated.IsFakeDns && migrated.SettingsSchemaVersion == 2, "legacy profile migration");
    Check(migrated.PerAppProxyList == "codex.exe" && migrated.PerAppDirectList == "bank.exe" && migrated.CustomProxyRules == "example.com", "migration preserves rules and lists");
    Check(File.ReadAllText(Path.Combine(migrationDir, "settings.before-2.9.1.json")) == original, "migration keeps exact backup");
    Check(!new ServerStorage(migrationDir).LoadSettings().IsFakeDns, "migration persisted");
    Console.WriteLine("PASS legacy profile migration");
}
finally { foreach (var file in Directory.GetFiles(migrationDir)) File.Delete(file); Directory.Delete(migrationDir); }
var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("vless://00000000-0000-0000-0000-000000000001@example.com:443?security=tls&type=ws&host=cdn.example.com&path=%2Fws#Test"));
Check(LinkParser.ParseMultiple(encoded).Count == 1, "base64 subscription import");
Check(LinkParser.Parse("vless://00000000-0000-0000-0000-000000000001@example.com:443?security=tls&type=ws#Test")?.Security == "tls", "link TLS preserved");

// A subscription request must use the supplied active proxy, even if the hostname cannot resolve.
var subProxy = new TcpListener(IPAddress.Loopback, 0); subProxy.Start();
var subProxyPort = ((IPEndPoint)subProxy.LocalEndpoint).Port;
try
{
    var serveSubscription = Task.Run(async () =>
    {
        using var client = await subProxy.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var buf = new byte[8192]; var count = await stream.ReadAsync(buf);
        Check(Encoding.ASCII.GetString(buf, 0, count).Contains("http://subscription.invalid/list"), "subscription uses explicit HTTP proxy");
        var response = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {encoded.Length}\r\nConnection: close\r\n\r\n{encoded}");
        await stream.WriteAsync(response);
    });
    var subscription = await SubscriptionService.FetchSubscriptionAsync("http://subscription.invalid/list", proxyPort: subProxyPort);
    Check(subscription.Nodes.Count == 1, "subscription fetched through active proxy");
    await serveSubscription;
}
finally { subProxy.Stop(); }
var subscriptionLogs = new List<string>();
try
{
    await SubscriptionService.FetchSubscriptionAsync("http://subscription.invalid/list", subscriptionLogs.Add, FreePort());
    throw new Exception("FAILED: unavailable subscription proxy accepted");
}
catch (HttpRequestException ex) { Check(ex.Message.Contains("серверу подписки"), "network failure gives actionable error"); }
Check(!subscriptionLogs.Any(l => l.Contains("альтернативный User-Agent") || l.Contains("Повторная попытка")), "no User-Agent retry for TCP failure");
Console.WriteLine("PASS subscription proxy and network errors");

// End-to-end local proxy regression: no external server or Windows routes/proxy settings touched.
var target = new TcpListener(IPAddress.Loopback, 0); target.Start();
var targetPort = ((IPEndPoint)target.LocalEndpoint).Port;
var httpPort = FreePort(); var socksPort = FreePort(); var metricsPort = FreePort();
var aliasNode=new ServerNode {Protocol="vless",Address="fixture-vpn.example",Port=FreePort(),Security="none",Uuid="00000000-0000-0000-0000-000000000001"};
var config = JsonNode.Parse(gen.GenerateConfig(aliasNode, mode: "global", socksPort: socksPort, httpPort: httpPort, metricsPort: metricsPort, customProxyRules: "127.0.0.1"))!;
config["dns"]!["hosts"]![XrayConfigGenerator.ServerAddressAlias(aliasNode)!]="127.0.0.1";
config["inbounds"]!.AsArray().Add(new JsonObject { ["tag"]="fixture-vless",["listen"]="127.0.0.1",["port"]=aliasNode.Port,["protocol"]="vless",
    ["settings"]=new JsonObject { ["decryption"]="none",["clients"]=new JsonArray {new JsonObject { ["id"]=aliasNode.Uuid } } } });
config["routing"]!["rules"]!.AsArray().Insert(0,new JsonObject { ["type"]="field",["inboundTag"]=new JsonArray {"fixture-vless"},["outboundTag"]="direct" });
// The test VLESS server explicitly allows only its loopback target. Xray's server defaults block private destinations.
config["outbounds"]!.AsArray().Single(o=>o!["tag"]!.ToString()=="direct")!["settings"]=new JsonObject {
    ["finalRules"]=new JsonArray {new JsonObject { ["action"]="allow",["ip"]=new JsonArray {"127.0.0.1/32"} }} };
var manager = new XrayProcessManager(root);
manager.OnLogMessage += Console.WriteLine;
try
{
    Check(await manager.StartAsync(config.ToJsonString(), httpPort, socksPort), "core ports become ready");
    Check(!await manager.StartAsync("{\"inbounds\": [invalid]}", httpPort, socksPort), "invalid replacement is rejected before shutdown");
    Check(manager.IsRunning, "existing core survives invalid replacement");
    var serve = Task.Run(async () =>
    {
        using var client = await target.AcceptTcpClientAsync();
        var stream = client.GetStream(); var buffer = new byte[4096];
        await stream.ReadAsync(buffer);
        var bytes = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 4096\r\nConnection: close\r\n\r\n" + new string('x', 4096));
        await stream.WriteAsync(bytes);
    });
    using var handler = new HttpClientHandler { UseProxy = true, Proxy = new WebProxy($"http://127.0.0.1:{httpPort}") };
    using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    var payload = await http.GetStringAsync($"http://127.0.0.1:{targetPort}/test");
    Check(payload.Length == 4096, "HTTP proxy carries payload"); await serve;
    using var metricsClient = new HttpClient(new HttpClientHandler { UseProxy = false });
    var metrics = JsonNode.Parse(await metricsClient.GetStringAsync($"http://127.0.0.1:{metricsPort}/debug/vars"))!;
    Check(metrics["stats"]!["outbound"]!["proxy"]!["downlink"]!.GetValue<long>() >= 4096, "VPN metrics count real bytes");
    var second = new XrayProcessManager(root);
    Check(!await second.StartAsync(config.ToJsonString(), httpPort, socksPort), "busy ports fail without killing running core");
    Check(manager.IsRunning, "existing core survives port conflict"); second.Stop();
    Console.WriteLine("PASS local proxy, metrics and process isolation");
}
finally { manager.Stop(); target.Stop(); }
var crashHttp = FreePort(); var crashSocks = FreePort();
var crashPsi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
foreach (var arg in new[] { "--crash-owner", root, crashHttp.ToString(), crashSocks.ToString() }) crashPsi.ArgumentList.Add(arg);
using (var crashOwner = Process.Start(crashPsi)!)
{
    var output = crashOwner.StandardOutput.ReadToEndAsync();
    await crashOwner.WaitForExitAsync();
    Check((await output).Contains("OWNER_READY"), "crash owner started core");
    await Task.Delay(300);
    using var probe = new TcpClient();
    var closed = false;
    try { await probe.ConnectAsync(IPAddress.Loopback, crashHttp); } catch (SocketException) { closed = true; }
    Check(closed, "owner termination closes its core through Windows job");
}
Console.WriteLine("PASS owner crash releases core");
Console.WriteLine("All port checks passed.");

static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
static void Check(bool value, string message) { if (!value) throw new Exception("FAILED: " + message); }
static void Expect<T>(Action action, string message) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("FAILED: " + message); }
