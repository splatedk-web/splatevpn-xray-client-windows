using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class UpstreamProxyChecks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS upstream: " + message); }
    static int Port() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
    public static async Task RunAsync(string root)
    {
        Check(UpstreamProxy.ParseWindows("http=proxy.example.com:8080;https=proxy.example.com:8443") == new HttpProxyEndpoint("proxy.example.com",8443), "Windows HTTPS endpoint and exact port imported");
        Check(UpstreamProxy.ParseWindows("proxy.example.com:8080").Port == 8080, "single Windows endpoint imported");
        try { UpstreamProxy.ParseWindows("http://user:password@proxy.example.com:8080"); throw new Exception("Credentials accepted"); } catch (ArgumentException) { }
        var disabled = new AppSettings(); var before = ConnectionSettings.Signature(disabled); disabled.UpstreamProxyHost="proxy.example.com";disabled.UpstreamProxyPort=8080;
        Check(before == ConnectionSettings.Signature(disabled),"editing disabled upstream fields does not interrupt a connection");
        var legacy = JsonNode.Parse(ConnectionProfiles.Capture(new AppSettings()))!.AsObject();
        foreach (var key in new[] { "UpstreamProxyEnabled", "UpstreamProxyHost", "UpstreamProxyPort" }) legacy.Remove(key);
        Check(!ConnectionProfiles.Target(new AppSettings { UpstreamProxyEnabled=true, UpstreamProxyHost="proxy.example.com" },legacy.ToJsonString()).UpstreamProxyEnabled,"old profiles remain compatible and clear a newer external proxy");
        foreach (var network in new[] { "tcp", "ws", "grpc", "xhttp" })
        {
            var node = new ServerNode { Address="vpn.fixture.example", Security="tls", Network=network, Uuid="00000000-0000-0000-0000-000000000001" };
            var config = JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(node,enableTlsFragment:true,aiRoutingMode:"dns_vpn",enableAiDns:true,upstreamProxyHost:"proxy.example.com",upstreamProxyPort:8080))!;
            var outs=config["outbounds"]!.AsArray();
            Check(outs.Single(o=>o!["tag"]!.ToString()=="proxy")!["streamSettings"]!["sockopt"]!["dialerProxy"]!.ToString()=="fragment" && outs.Single(o=>o!["tag"]!.ToString()=="fragment")!["streamSettings"]!["sockopt"]!["dialerProxy"]!.ToString()=="upstream-http","TLS fragment preserves external chain: "+network);
            Check(outs.Single(o=>o!["tag"]!.ToString()=="ai-smartdns")!["streamSettings"]!["sockopt"]!["dialerProxy"]!.ToString()=="proxy","SmartDNS relay still goes through VPN: "+network);
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
            try { await File.WriteAllTextAsync(path,config.ToJsonString()); var info=new System.Diagnostics.ProcessStartInfo(Path.Combine(root,"core/xray.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}; foreach(var a in new[]{"run","-test","-c",path})info.ArgumentList.Add(a); using var process=System.Diagnostics.Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();Check(process.ExitCode==0,"real Xray validates external chain "+network+": "+await stderr+await stdout); } finally {File.Delete(path);}
        }
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var ct=lifetime.Token;var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        var port=((IPEndPoint)listener.LocalEndpoint).Port;var vpnPort=Port();var vpnSocks=Port();var httpPort=Port();var socksPort=Port();
        var seen=new System.Collections.Concurrent.ConcurrentBag<string>();int status=200;
        var workers=new System.Collections.Concurrent.ConcurrentBag<Task>();
        var accept=Task.Run(async()=>{try{while(!ct.IsCancellationRequested){var socket=await listener.AcceptTcpClientAsync(ct);workers.Add(Task.Run(async()=>{
            using(socket){try{var input=socket.GetStream();var header=new List<byte>();var one=new byte[1];while(header.Count<16000){if(await input.ReadAsync(one,ct)==0)return;header.Add(one[0]);if(header.TakeLast(4).SequenceEqual(new byte[]{13,10,13,10}))break;}
            var first=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n")[0];seen.Add(first);Console.WriteLine("CONNECT MOCK " + first);
            var code=Volatile.Read(ref status);if(code!=200){await input.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {code} Denied\r\nContent-Length: 0\r\n\r\n"),ct);return;}
            using var forward=new TcpClient();await forward.ConnectAsync(IPAddress.Loopback,vpnPort,ct);
            await input.WriteAsync("HTTP/1.1 200 Connection established\r\nContent-Length: 0\r\n\r\n"u8.ToArray(),ct);
            await Task.WhenAny(input.CopyToAsync(forward.GetStream(),ct),forward.GetStream().CopyToAsync(input,ct));
            }catch(Exception ex)when(ex is IOException or OperationCanceledException or SocketException){}}
        }));}}catch(OperationCanceledException){}});
        var server=new XrayProcessManager(root);var client=new XrayProcessManager(root); server.OnLogMessage += x => Console.WriteLine("SERVER " + x); client.OnLogMessage += x => Console.WriteLine("CLIENT " + x);
        try
        {
            const string uuid="00000000-0000-0000-0000-000000000001";
            var serverConfig=new JsonObject{["log"]=new JsonObject{["loglevel"]="debug"},["inbounds"]=new JsonArray{
                new JsonObject{["listen"]="127.0.0.1",["port"]=vpnPort,["protocol"]="vless",["settings"]=new JsonObject{["decryption"]="none",["clients"]=new JsonArray{new JsonObject{["id"]=uuid}}}},
                new JsonObject{["listen"]="127.0.0.1",["port"]=vpnSocks,["protocol"]="socks"}},
                ["outbounds"]=new JsonArray{new JsonObject{["protocol"]="freedom",["settings"]=new JsonObject{["redirect"]="127.0.0.1:1",["finalRules"]=new JsonArray{new JsonObject{["action"]="allow",["ip"]=new JsonArray{"127.0.0.1/32"}}}}}}};
            Check(await server.StartAsync(serverConfig.ToJsonString(),vpnPort,vpnSocks,ct),"real VLESS fixture starts");
            var endpoint=new HttpProxyEndpoint("127.0.0.1",port);
            await UpstreamProxy.CheckConnectAsync(endpoint,"vpn-fixture.invalid",vpnPort,ct);
            Check(seen.Any(x=>x==$"CONNECT vpn-fixture.invalid:{vpnPort} HTTP/1.1"),"preflight sends remote host without local DNS");
            var destination=new TcpListener(IPAddress.Loopback,0);destination.Start();var destPort=((IPEndPoint)destination.LocalEndpoint).Port;
            serverConfig["outbounds"]![0]!["settings"]!["redirect"]=$"127.0.0.1:{destPort}";
            Check(await server.StartAsync(serverConfig.ToJsonString(),vpnPort,vpnSocks,ct),"VPN fixture redirects to loopback HTTP target");
            var respond=Task.Run(async()=>{using var socket=await destination.AcceptTcpClientAsync(ct);var stream=socket.GetStream();var bytes=new byte[4096];await stream.ReadAsync(bytes,ct);await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 9\r\nConnection: close\r\n\r\nVPN-CHAIN"u8.ToArray(),ct);});
            try
            {
                var node=new ServerNode{Protocol="vless",Address="vpn-fixture.invalid",Port=vpnPort,Uuid=uuid,Security="tls",Sni="vpn-fixture.invalid"};
                // A local unencrypted fixture bypasses DNS only in this test. Production keeps TLS/Reality and node settings.
                var config=JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(node,mode:"global",httpPort:httpPort,socksPort:socksPort,upstreamProxyHost:endpoint.Host,upstreamProxyPort:port))!.AsObject();
                config["log"] = new JsonObject{["loglevel"]="debug"};
                var proxy=config["outbounds"]!.AsArray().Single(x=>x!["tag"]?.ToString()=="proxy")!;
                config["routing"]!["rules"]!.AsArray().Insert(0,new JsonObject{["type"]="field",["port"]=destPort.ToString(),["outboundTag"]="proxy"});
                proxy["streamSettings"]!["security"]="none";proxy["streamSettings"]!.AsObject().Remove("tlsSettings");
                Check(await client.StartAsync(config.ToJsonString(),httpPort,socksPort,ct),"bundled Xray accepts HTTP dialer chain");
                using var http=new HttpClient(new SocketsHttpHandler{UseProxy=true,Proxy=new WebProxy($"http://127.0.0.1:{httpPort}")}){Timeout=TimeSpan.FromSeconds(5)};
                Check(await http.GetStringAsync($"http://127.0.0.1:{destPort}/check",ct)=="VPN-CHAIN","traffic traverses external CONNECT and VPN");
                await respond;
                Check(seen.Count(x=>x.Contains("vpn-fixture.invalid:"))>=2,"actual core sends VPN hostname through CONNECT");
                Volatile.Write(ref status,403);
                try{await UpstreamProxy.CheckConnectAsync(endpoint,"vpn-fixture.invalid",443,ct);throw new Exception("403 accepted");}catch(IOException ex){Check(ex.Message.Contains("403"),"policy denial explained");}
                Volatile.Write(ref status,407);
                try{await UpstreamProxy.CheckConnectAsync(endpoint,"vpn-fixture.invalid",443,ct);throw new Exception("407 accepted");}catch(IOException ex){Check(ex.Message.Contains("авторизац"),"authentication requirement explained");}
                Volatile.Write(ref status,403);
                try{await http.GetStringAsync($"http://127.0.0.1:{destPort}/blocked",ct);throw new Exception("direct fallback occurred");}catch(HttpRequestException){Check(true,"proxy denial does not fall back to a direct connection");}catch(TaskCanceledException){Check(true,"proxy denial does not fall back to a direct connection");}
            }
            finally{destination.Stop();}
        }
        finally{client.Stop();server.Stop();lifetime.Cancel();listener.Stop();await accept;await Task.WhenAll(workers);}
    }
}
