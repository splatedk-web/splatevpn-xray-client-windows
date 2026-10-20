using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class SandboxUplinkChecks
{
    public static async Task RunBalancerAsync()
    {
        if (!Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase)) throw new Exception("Only Windows Sandbox is allowed");
        var root=AppContext.BaseDirectory;var corePath=Path.Combine(root,"core","xray.exe");
        var good=new ServerNode{Id="working",Protocol="freedom",Security="none"};
        var bad=new ServerNode{Id="wrong-vless",Address="1.1.1.1",Port=443,Protocol="vless",Security="none",Uuid="00000000-0000-0000-0000-000000000001"};
        async Task<Dictionary<string,long>> Probe()=>await Run();
        async Task<Dictionary<string,long>> Run(){var results=new Dictionary<string,long>();await PingService.MeasureRealPingBatchAsync([good,bad],corePath,timeoutMs:4000,onSingleNodePinged:(node,ping)=>results[node.Id]=ping);return results;}
        var before=await Probe();
        if(before[good.Id]<=0||before[bad.Id]>0)throw new Exception("Baseline internet test unavailable or invalid VLESS passed: "+System.Text.Json.JsonSerializer.Serialize(before));
        var uplink=await TunDeviceManager.GetUplinkAsync();var http=Port();var socks=Port();
        var config=JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(new ServerNode{Address="127.0.0.1",Security="none"},mode:"global",enableTun:true,uplinkInterface:uplink.Name,socksPort:socks,httpPort:http))!;
        config["outbounds"]![0]=new JsonObject{["tag"]="proxy",["protocol"]="blackhole"};
        // Simulate a running VPN that carries no traffic, including OS DNS.
        config["routing"]!["rules"]=new JsonArray{new JsonObject{["type"]="field",["network"]="tcp,udp",["outboundTag"]="proxy"}};
        var core=new XrayProcessManager(root);var tun=new TunDeviceManager();var unbound=new XrayProcessManager(root);
        try{
            if(!await core.StartAsync(config.ToJsonString(),http,socks)||!await tun.StartTunAsync(socks,uplink,true,Console.WriteLine))throw new Exception("Blackhole TUN failed");
            var legacyPort=Port();var legacySecond=Port();
            var legacy=PingService.BuildProbeConfig([(good,legacyPort),(good,legacySecond)],null);
            if(!await unbound.StartAsync(legacy.ToJsonString(),legacySecond,legacyPort))throw new Exception("Unbound negative control failed to start");
            var captured=await PingService.MeasureHttpProxyLatencyAsync(legacyPort,1200);
            if(captured>0)throw new Exception("Negative control escaped blackhole TUN");
            unbound.Stop();
            var after=await Probe();
            if(after[good.Id]<=0||after[bad.Id]>0)
            {
                var debugPort=Port();var debugSecond=Port();
                var debug=PingService.BuildProbeConfig([(good,debugPort),(good,debugSecond)],uplink.Name);
                debug["log"]!["loglevel"]="info";
                unbound.OnLogMessage+=Console.WriteLine;
                Console.WriteLine("DEBUG CONFIG: "+debug.ToJsonString());
                if(await unbound.StartAsync(debug.ToJsonString(),debugSecond,debugPort))Console.WriteLine("DEBUG RESULT: "+await PingService.MeasureHttpProxyLatencyAsync(debugPort,6000));
                throw new Exception("TUN candidate isolation failed: "+System.Text.Json.JsonSerializer.Serialize(after));
            }
            var tcp=await PingService.MeasureTcpLatencyAsync("1.1.1.1",443,3000);
            var deadTcp=await PingService.MeasureTcpLatencyAsync("192.0.2.99",65534,1200);
            if(tcp<=0)throw new Exception($"Physical TCP port is unreachable: {tcp}");
            var route=await ProbeRoute.CaptureAsync(CancellationToken.None);
            using(var boundSocket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp)){
                route.Bind(boundSocket);
                await boundSocket.ConnectAsync(IPAddress.Parse("1.1.1.1"),443);
                if(((IPEndPoint)boundSocket.LocalEndPoint!).Address.ToString()=="172.19.0.1")throw new Exception("TCP probe uses TUN source address");
                Console.WriteLine("Physical TCP source: "+boundSocket.LocalEndPoint);
            }
            var dns=await route.ResolveAsync("www.gstatic.com",CancellationToken.None);
            if(dns.Length==0)throw new Exception("Candidate DNS depends on broken current VPN");
            if(!core.IsRunning||!tun.IsRunning)throw new Exception("Candidate scan stopped the active VPN");
            Console.WriteLine("PASS TUN balancer: unbound negative control fails; bound candidate succeeds with blackholed active VPN; wrong VLESS rejected before/after TUN; physical TCP/DNS independent; current TUN preserved");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{before,after,tcp,deadTcp,dnsAddresses=dns.Length}));
        }finally{unbound.Stop();await tun.StopTunAsync(Console.WriteLine);core.Stop();}
    }

    public static async Task RunAsync()
    {
        // This check installs capture routes. Never run it on the developer's Windows.
        if (!Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase)) throw new Exception("Only Windows Sandbox is allowed");
        var root = AppContext.BaseDirectory;
        var uplink = await TunDeviceManager.GetUplinkAsync();
        if (!uplink.Name.Any(c => c > 127)) throw new Exception("Rename the Sandbox adapter to a Unicode name first");
        var http = Port(); var socks = Port();
        var config = JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(new ServerNode { Address = "127.0.0.1", Security = "none" }, mode: "global", enableTun: true, uplinkInterface: uplink.Name, socksPort: socks, httpPort: http))!;
        config["outbounds"]![0] = new JsonObject { ["tag"] = "proxy", ["protocol"] = "freedom", ["streamSettings"] = new JsonObject { ["sockopt"] = new JsonObject { ["interface"] = uplink.Name, ["domainStrategy"] = "UseIPv4" } } };
        var core = new XrayProcessManager(root); var tun = new TunDeviceManager();
        var bindingError = false;
        core.OnLogMessage += message => { if (message.Contains("failed to find the interface") || message.Contains("local endpoint 172.19.0.1")) bindingError = true; };
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            if (!await core.StartAsync(config.ToJsonString(), http, socks, timeout.Token)) throw new Exception("Core failed");
            if (!await tun.StartTunAsync(socks, uplink, true, Console.WriteLine, timeout.Token)) throw new Exception("TUN failed");
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
            var body = await client.GetStringAsync("https://1.1.1.1/cdn-cgi/trace", timeout.Token);
            if (!body.Contains("ip=") || !body.Contains("colo=")) throw new Exception("No external HTTP payload through TUN");
            var refreshed = await TunDeviceManager.GetUplinkAsync(timeout.Token);
            if (refreshed.Name != uplink.Name || refreshed.Index != uplink.Index || bindingError) throw new Exception("Binding failed or TUN recursion detected");
            Console.WriteLine("PASS Sandbox full TUN: real HTTPS through capture routes, Xray bound to Unicode physical adapter; no interface error/recursion; uplink re-detected while TUN active");
        }
        finally { await tun.StopTunAsync(Console.WriteLine); core.Stop(); }
    }
    static int Port() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
}
