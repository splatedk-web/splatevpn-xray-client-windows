using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class RoutingTrafficChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static int Port() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    public static async Task RunAsync(string root)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var direct = new TcpListener(IPAddress.Loopback, 0); direct.Start();
        var vpn = new TcpListener(IPAddress.Loopback, 0); vpn.Start();
        var directPort = ((IPEndPoint)direct.LocalEndpoint).Port;
        var vpnPort = ((IPEndPoint)vpn.LocalEndpoint).Port;
        int directHits = 0;
        using var rsa = RSA.Create(2048);
        var certRequest = new CertificateRequest("CN=gemini.google.com", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generatedCertificate = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        // Import into a Windows key provider for Schannel; do not install a trusted certificate.
        using var certificate = new X509Certificate2(generatedCertificate.Export(X509ContentType.Pfx));
        var workers = new System.Collections.Concurrent.ConcurrentBag<Task>();
        async Task Serve(TcpListener listener, string body)
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    var socket = await listener.AcceptTcpClientAsync(lifetime.Token);
                    workers.Add(Task.Run(async () =>
                    {
                        using (socket)
                        {
                            try
                            {
                                Stream stream = socket.GetStream(); var buffer = new byte[4096];
                                SslStream? tls = null;
                                var peek = new byte[1];
                                if(await socket.Client.ReceiveAsync(peek,SocketFlags.Peek,lifetime.Token)>0 && peek[0]==0x16)
                                {
                                    tls = new SslStream(stream,leaveInnerStreamOpen:false);
                                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate=certificate },lifetime.Token);
                                    stream=tls;
                                }
                                using var tlsLifetime = tls;
                                if (await stream.ReadAsync(buffer, lifetime.Token) == 0) return;
                                if (body == "DIRECT") Interlocked.Increment(ref directHits);
                                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"), lifetime.Token);
                            }
                            catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
                        }
                    }));
                }
            }
            catch (OperationCanceledException) { }
        }
        var servers = Task.WhenAll(Serve(direct, "DIRECT"), Serve(vpn, "VPN"));
        var upstream = new XrayProcessManager(root); var client = new XrayProcessManager(root);
        try
        {
            var vlessPort = Port(); var serverSocks = Port(); var httpPort = Port(); var socksPort = Port();
            const string uuid = "00000000-0000-0000-0000-000000000001";
            var serverConfig = new JsonObject
            {
                ["inbounds"] = new JsonArray
                {
                    new JsonObject { ["listen"]="127.0.0.1", ["port"]=vlessPort, ["protocol"]="vless",
                        ["settings"]=new JsonObject { ["decryption"]="none", ["clients"]=new JsonArray { new JsonObject { ["id"]=uuid } } } },
                    new JsonObject { ["listen"]="127.0.0.1", ["port"]=serverSocks, ["protocol"]="socks" }
                },
                ["outbounds"] = new JsonArray { TaggedFreedom(vpnPort,"vpn"), TaggedFreedom(directPort,"stale-relay") },
                ["routing"] = new JsonObject { ["domainStrategy"]="AsIs", ["rules"]=new JsonArray {
                    new JsonObject { ["type"]="field", ["ip"]=new JsonArray {"203.0.113.88/32"}, ["outboundTag"]="stale-relay" } } }
            }.ToJsonString();
            var node = new ServerNode { Address="127.0.0.1", Port=vlessPort, Security="none", Uuid=uuid };
            JsonObject Config(string mode, bool sniff, string directRules)
            {
                var config = JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(node, mode:mode,
                    enableSniffing:sniff, enableAiDns:mode=="global", enableAiXboxDns:mode=="global", enableAiBypass:true,
                    aiRoutingMode:mode=="global"?"dns_ai":"vpn", dnsProvider:mode=="global"?"dns_ai":"cloudflare",
                    customDirectRules:directRules, httpPort:httpPort, socksPort:socksPort))!.AsObject();
                foreach (var host in new[] { "chatgpt.com", "gemini.google.com", "claude.ai", "fixture.example.ru" })
                    config["dns"]!["hosts"]![host] = "1.1.1.1";
                var directOutbound = config["outbounds"]!.AsArray().Single(o => o!["tag"]!.ToString()=="direct")!;
                directOutbound["settings"] = Freedom(directPort)["settings"]!.DeepClone();
                Check(!config["outbounds"]!.AsArray().Any(o => o!["tag"]!.ToString()=="ai-smartdns"), "VPN mode unexpectedly created a direct SmartDNS outbound");
                if(mode=="global")
                    Check(config["routing"]!["rules"]!.AsArray().Where(r=>r!["outboundTag"]?.ToString()=="direct")
                        .All(r=>r!["ip"]?.ToJsonString()=="[\"geoip:private\"]"), "global config contains a non-LAN direct bypass");
                return config;
            }
            async Task<string> CachedSniRequest()
            {
                using var socket=new TcpClient(); await socket.ConnectAsync(IPAddress.Loopback,socksPort,lifetime.Token);
                var stream=socket.GetStream();
                await stream.WriteAsync(new byte[]{5,1,0},lifetime.Token);
                var greeting=new byte[2];await stream.ReadExactlyAsync(greeting,lifetime.Token);
                Check(greeting.SequenceEqual(new byte[]{5,0}),"SOCKS greeting");
                await stream.WriteAsync(new byte[]{5,1,0,1,203,0,113,88,1,187},lifetime.Token);
                var reply=new byte[10];await stream.ReadExactlyAsync(reply,lifetime.Token);
                Check(reply[1]==0,"SOCKS connect to cached relay");
                using var tls=new SslStream(stream,false,(_,remote,_,_)=>remote!=null && remote.GetCertHashString()==certificate.Thumbprint);
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions{TargetHost="gemini.google.com"},lifetime.Token);
                await tls.WriteAsync("GET /cached-dns HTTP/1.1\r\nHost: gemini.google.com\r\nConnection: close\r\n\r\n"u8.ToArray(),lifetime.Token);
                using var reader=new StreamReader(tls,Encoding.ASCII);
                return await reader.ReadToEndAsync(lifetime.Token);
            }
            foreach (var mode in new[] { "smart_ru", "global" }) foreach (var sniff in new[] { true, false })
            {
                Console.WriteLine($"Routing fixture: {mode}, sniffing={sniff}");
                Check(await upstream.StartAsync(serverConfig, vlessPort, serverSocks, lifetime.Token), "fixture VPN server starts");
                using var http = new HttpClient(new SocketsHttpHandler { UseProxy=true, Proxy=new WebProxy($"http://127.0.0.1:{httpPort}"), AllowAutoRedirect=false, PooledConnectionLifetime=TimeSpan.Zero })
                    { Timeout=TimeSpan.FromSeconds(3) };
                http.DefaultRequestHeaders.ConnectionClose=true;
                Check(await client.StartAsync(Config(mode, sniff, "").ToJsonString(), httpPort, socksPort, lifetime.Token), "client core starts");
                var primary = await http.GetStringAsync("http://fixture.example.ru/check", lifetime.Token);
                Check(primary==(mode=="global"?"VPN":"DIRECT"), "primary routing mode does not affect real traffic: "+mode);
                Check(await client.StartAsync(Config(mode, sniff, "regexp:.*").ToJsonString(), httpPort, socksPort, lifetime.Token), "client applies direct exception");
                foreach (var host in new[] { "chatgpt.com", "gemini.google.com", "claude.ai" })
                    Check(await http.GetStringAsync($"http://{host}/check", lifetime.Token)=="VPN", "AI request escaped VPN: "+host);
                if(sniff)
                {
                    // Simulate a browser's stale SmartDNS cache: SOCKS destination is a relay IP,
                    // but TLS SNI still names Gemini. The VPN server must receive the hostname.
                    var stale = Config(mode,true,"regexp:.*");
                    foreach(var inbound in stale["inbounds"]!.AsArray()) inbound!["sniffing"]!["routeOnly"]=true;
                    Check(await client.StartAsync(stale.ToJsonString(),httpPort,socksPort,lifetime.Token),"cached-IP negative control starts");
                    Check((await CachedSniRequest()).EndsWith("DIRECT",StringComparison.Ordinal),"negative control did not reproduce cached SmartDNS relay bug");
                    Check(await client.StartAsync(Config(mode,true,"regexp:.*").ToJsonString(),httpPort,socksPort,lifetime.Token),"SNI repair config starts");
                    Check((await CachedSniRequest()).EndsWith("VPN",StringComparison.Ordinal),"TLS SNI did not replace a stale SmartDNS relay IP before tunneling");
                    Console.WriteLine("PASS cached TLS: old route reaches relay; repaired route reaches VPN destination");
                }
                Check(await http.GetStringAsync("http://fixture.example.ru/control", lifetime.Token)==(mode=="global"?"VPN":"DIRECT"), "global routing failed to suppress direct exception");
                Check(await http.GetStringAsync($"http://127.0.0.1:{directPort}/lan-control", lifetime.Token)=="DIRECT", "private LAN negative control did not work");
                var before = Volatile.Read(ref directHits);
                upstream.Stop();
                var failed=false;
                try { await http.GetStringAsync("http://chatgpt.com/vpn-down", lifetime.Token); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { failed=true; }
                Check(failed && Volatile.Read(ref directHits)==before, "VPN failure fell back to direct traffic");
                client.Stop();
            }
            Check(await upstream.StartAsync(serverConfig,vlessPort,serverSocks,lifetime.Token),"tunneled SmartDNS fixture server starts");
            var tunneled = JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(node,mode:"global",aiRoutingMode:"dns_vpn",enableAiDns:true,enableAiBypass:true,enableSniffing:false,httpPort:httpPort,socksPort:socksPort))!;
            tunneled["dns"]!["hosts"]!["gemini.google.com"]="203.0.113.88";
            var bypass=tunneled["outbounds"]!.AsArray().Single(o=>o?["tag"]?.ToString()=="direct")!;
            bypass["protocol"]="blackhole";bypass["settings"]=new JsonObject();
            Check(await client.StartAsync(tunneled.ToJsonString(),httpPort,socksPort,lifetime.Token),"tunneled SmartDNS starts with direct path disabled");
            using(var http=new HttpClient(new SocketsHttpHandler{UseProxy=true,Proxy=new WebProxy($"http://127.0.0.1:{httpPort}"),PooledConnectionLifetime=TimeSpan.Zero}){Timeout=TimeSpan.FromSeconds(5)})
                Check(await http.GetStringAsync("http://gemini.google.com/relay",lifetime.Token)=="DIRECT","relay IP reaches the VPN server before remote DNS, even with direct outbound blocked");
            upstream.Stop();
            using(var http=new HttpClient(new SocketsHttpHandler{UseProxy=true,Proxy=new WebProxy($"http://127.0.0.1:{httpPort}")}){Timeout=TimeSpan.FromSeconds(3)})
            { bool failed=false;try{await http.GetStringAsync("http://gemini.google.com/dead-vpn",lifetime.Token);}catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException){failed=true;}Check(failed,"tunneled SmartDNS has no direct fallback when VPN fails"); }
            Console.WriteLine("PASS real Xray traffic: global routing, SNI repair, SmartDNS relay through VLESS with direct path blocked and dead VPN without fallback");
        }
        finally
        {
            client.Stop(); upstream.Stop(); lifetime.Cancel(); await servers;
            direct.Stop(); vpn.Stop(); await Task.WhenAll(workers);
        }
    }
    static JsonObject Freedom(int port) => new()
    {
        ["protocol"]="freedom", ["settings"]=new JsonObject { ["redirect"]=$"127.0.0.1:{port}",
            ["finalRules"]=new JsonArray { new JsonObject { ["action"]="allow", ["ip"]=new JsonArray {"127.0.0.1/32"} } } }
    };
    static JsonObject TaggedFreedom(int port,string tag) { var outbound=Freedom(port);outbound["tag"]=tag;return outbound; }
}
