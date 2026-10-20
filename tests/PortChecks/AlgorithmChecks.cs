using System.Text.Json.Nodes;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class AlgorithmChecks
{
    public static void Run(string root)
    {
        var subscriptions=new List<SubscriptionItem>();var servers=new List<ServerNode>();
        var count=PresetSubscriptions.Seed(subscriptions,servers,Path.Combine(root,"Assets","presets"));
        Check(count>100 && subscriptions.Count==3,"all Android presets loaded offline");
        var batch=JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(servers[0]))!;
        var allOutbounds=batch["outbounds"]!.AsArray();
        for(var index=0;index<servers.Count;index++)
        {
            allOutbounds.Add(XrayConfigGenerator.BuildProxyOutbound(servers[index],tag:"preset-"+index));
            var alias=XrayConfigGenerator.ServerAddressAlias(servers[index]);
            if(alias!=null)batch["dns"]!["hosts"]![alias]=servers[index].Address;
        }
        var batchPath=Path.Combine(Path.GetTempPath(),"splate-preset-validate-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            File.WriteAllText(batchPath,batch.ToJsonString());
            var start=new System.Diagnostics.ProcessStartInfo(Path.Combine(root,"core","xray.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.Combine(root,"core")};
            foreach(var arg in new[]{"run","-test","-c",batchPath})start.ArgumentList.Add(arg);
            using var process=System.Diagnostics.Process.Start(start)!;
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            Check(process.WaitForExit(15000),"preset validation timeout");
            Check(process.ExitCode==0,"all preset transports accepted by Xray: "+stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult());
        }
        finally{File.Delete(batchPath);}
        Check(servers.All(n=>subscriptions.Any(s=>s.Id==n.SubscriptionId)) && subscriptions.All(s=>s.NodeCount==servers.Count(n=>n.SubscriptionId==s.Id)),"preset ownership/counts");
        Check(PresetSubscriptions.Seed(subscriptions,servers,Path.Combine(root,"Assets","presets"))==0,"seeding is idempotent");
        var removed=subscriptions[0];subscriptions.RemoveAt(0);servers.RemoveAll(s=>s.SubscriptionId==removed.Id);
        var retained=servers.Select(s=>s.Id).ToArray();
        Check(PresetSubscriptions.Seed(subscriptions,servers,Path.Combine(root,"Assets","presets"))==removed.NodeCount,"restore only missing source");
        Check(retained.All(id=>servers.Any(s=>s.Id==id)),"restore preserves existing nodes");
        var a=new ServerNode {Id="stable",Address="vpn.example",Uuid="credential",IsFavorite=true,IsSelected=true,Ping=35};
        var changed=new ServerNode {Address="vpn.example",Uuid="credential",Network="xhttp"};
        var merged=PresetSubscriptions.Merge([a],[changed]).Single();
        Check(merged.Id=="stable" && merged.IsFavorite && merged.IsSelected && merged.Network=="xhttp" && merged.Ping==35,"refresh preserves preferences, updates transport");
        Check(PresetSubscriptions.Merge([a],[new ServerNode {Address="vpn.example",Uuid="other"}]).Single().Id!="stable","different credentials do not inherit identity");
        Check(!PresetSubscriptions.Owns(new SubscriptionItem {Name="Личные"},new ServerNode {SubscriptionGroup="Личные"}),"subscription name cannot capture personal nodes");
        Check(LinkParser.UnwrapImportLink("happ://add/https%3A%2F%2Fexample.com%2Fsub%3Ftoken%3Da%2Bb")=="https://example.com/sub?token=a+b","Happ URL wrapping preserves plus in tokens");
        long now=1000000;var quality=new ServerQualityTracker(()=>now);
        quality.Record("steady",100);quality.Record("steady",105);
        quality.Record("flapping",20);quality.Record("flapping",900);quality.Record("flapping",30);
        Check(quality.Score("steady",105)<quality.Score("flapping",30),"jitter outweighs isolated low ping");
        quality.Record("dead",0);Check(quality.IsQuarantined("dead") && quality.Score("dead",10)==long.MaxValue,"dead node excluded");
        now+=60001;Check(!quality.IsQuarantined("dead"),"quarantine expires");quality.Record("dead",0);
        now+=60001;Check(quality.IsQuarantined("dead"),"repeat failure increases quarantine");
        quality.Record("dead",10);Check(!quality.IsQuarantined("dead") && quality.Score("dead",10)>10,"success clears quarantine, retains flapping penalty");
        var midnight=new DateTimeOffset(2026,10,14,0,0,1,TimeSpan.Zero);
        var samples=TrafficHistory.Split(midnight,TimeSpan.FromSeconds(3),100,77,TimeZoneInfo.Utc);
        Check(samples.Count==2 && samples.Sum(s=>s.Download)==100 && samples.Sum(s=>s.Upload)==77 && samples.Sum(s=>s.ConnectedMs)==3000,"midnight split preserves bytes/time");
        Check(samples[0].Date==new DateOnly(2026,10,13)&&samples[1].Date==new DateOnly(2026,10,14),"local day boundaries");
        var n=new ServerNode {Protocol="vless",Address="2001:db8::1",Uuid="00000000-0000-0000-0000-000000000001",Security="tls",Name="Тест #1",Path="/api?foo=bar&x=y",Network="xhttp",XmuxMaxConnections=4};
        var roundtrip=LinkParser.Parse(ServerShare.Link(n))!;
        Check(roundtrip!=null&&roundtrip.Address==n.Address&&roundtrip.Path==n.Path&&roundtrip.XmuxMaxConnections==4,"edited link IPv6/path/XMUX roundtrip");
        var gen=new XrayConfigGenerator();
        var speedRules=JsonNode.Parse(gen.GenerateConfig(n,customDirectRules:"cachefly.net,speed.cloudflare.com"))!["routing"]!["rules"]!.AsArray();
        foreach(var host in new[]{"cachefly.net","speed.cloudflare.com","cp.cloudflare.com"})
        {
            var first=speedRules.First(r=>r?["domain"] is JsonArray domains && domains.Any(d=>d?.ToString()=="domain:"+host));
            Check(first!["outboundTag"]!.ToString()=="proxy","speed test stays on VPN before direct rules: "+host);
        }
        var ai=JsonNode.Parse(gen.GenerateConfig(n,enableAiDns:true))!;
        Check(ai["dns"]!["servers"]!.AsArray().OfType<JsonObject>().Any(d=>d["address"]?.ToString()=="https://dns.dns-ai.ru/dns-query"),"Android DNS-AI endpoint");
        Check(ai["routing"]!["rules"]!.AsArray().Any(r=>r!["outboundTag"]?.ToString()=="ai-smartdns"&&r["domain"]?.ToJsonString().Contains("robinfrontend-pa.googleapis.com")==true),"Android Gemini backend");
        var vpn=JsonNode.Parse(gen.GenerateConfig(n,enableAiDns:true,aiRoutingMode:"vpn"))!;
        Check(vpn["outbounds"]!.AsArray().All(o=>o!["tag"]?.ToString()!="ai-smartdns"),"VPN AI mode does not use Smart DNS");
        Check(JsonNode.Parse(gen.GenerateConfig(n,enableMux:true))!["outbounds"]![0]!["mux"]==null,"Mux excluded on XHTTP");
        n.Network="tcp";n.Security="tls";
        Check(JsonNode.Parse(gen.GenerateConfig(n,enableMux:true))!["outbounds"]![0]!["mux"]!["enabled"]!.GetValue<bool>(),"Mux on compatible transport");
        Console.WriteLine($"PASS algorithms: {count} preset nodes, restoration, identity merge, quality/quarantine, traffic midnight, links, AI modes, Mux");
    }
    private static void Check(bool condition,string name){if(!condition)throw new Exception(name);}
}
