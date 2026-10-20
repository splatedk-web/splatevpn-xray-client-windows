using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class AndroidRefreshChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task RunAsync(string root)
    {
        var node = new ServerNode { Address="127.0.0.1", Port=9, Security="none", Uuid="00000000-0000-0000-0000-000000000001" };
        foreach(var provider in SmartDnsProviders.All) foreach(var fragment in new[]{false,true}) {
            var config=JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(node,enableAiDns:true,aiDnsProvider:provider.Key,aiTlsFragment:fragment))!;
            var ai=config["outbounds"]!.AsArray().Single(o=>o!["tag"]?.ToString()=="ai-smartdns")!;
            Check((ai["settings"]?["fragment"]!=null)==fragment,"SmartDNS fragmentation is optional and isolated");
            Check(config["dns"]!["servers"]!.AsArray().Any(s=>s is JsonObject o&&o["address"]?.ToString()==provider.DohUrl),"selected AI DNS endpoint");
            Check(config["dns"]!["hosts"]![provider.Host]!.AsArray().Count==provider.BootstrapIpv4.Length,"bootstrap addresses");
            Check(config["routing"]!["rules"]!.AsArray().Single(r=>r?["outboundTag"]?.ToString()=="ai-smartdns")!["domain"]!.AsArray().All(d=>d!.ToString().StartsWith("full:")),"exact AI hosts only");
            await Validate(root,config.ToJsonString());
            var probe=SmartDnsProviders.ProbeConfig(provider.Key,fragment,39001);await Validate(root,probe);
            var probeNode=JsonNode.Parse(probe)!;
            Check(probeNode["outbounds"]![1]!["settings"]?["fragment"]==null,"DNS connection never fragmented");
            var general=JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(node,dnsProvider:provider.Key,aiDnsProvider:"dns_ai",enableAiDns:true))!;
            Check(general["dns"]!["servers"]!.AsArray().OfType<JsonObject>().Last()["address"]!.ToString()==provider.DohUrl,"general DNS independent of AI DNS");
        }
        Check(AiDnsAutoSelector.Classify(200,"unsupported_country")==AiDnsOutcome.GeoBlocked,"regional block");
        Check(AiDnsAutoSelector.Classify(403,"challenge")==AiDnsOutcome.Limited,"403 not success");
        Check(AiDnsAutoSelector.Classify(302,"","/login")==AiDnsOutcome.Limited,"redirect not success");
        Check(AiDnsAutoSelector.Classify(200,"<html>service</html>")==AiDnsOutcome.Accessible,"public page success");
        AiDnsCandidateResult Result(string provider,bool fragment,int success)=>new(new(provider,fragment),new[]{new AiDnsTargetResult("ChatGPT",success>0?AiDnsOutcome.Accessible:AiDnsOutcome.Limited,200),new AiDnsTargetResult("Gemini",success>1?AiDnsOutcome.Accessible:AiDnsOutcome.Failed,100)});
        var ranked=new[]{Result("dns_ai",true,2),Result("malw",false,2),Result("geohide",false,0)};
        Check(AiDnsAutoSelector.Rank(ranked).First().Choice.ProviderKey=="malw","unfragmented wins tie");
        var confirmed=await AiDnsAutoSelector.ConfirmAsync(ranked,c=>Task.FromResult(Result(c.ProviderKey,c.TlsFragment,c.ProviderKey=="malw"?0:2)));
        Check(confirmed?.Choice.ProviderKey=="dns_ai","reject unstable leader and confirm next");
        Check(await AiDnsAutoSelector.ConfirmAsync(new[]{Result("dns_ai",false,0)},_=>throw new Exception("Should not verify challenge-only result"))==null,"challenge-only results cannot win");
        Check(XrayConfigGenerator.ValidateDoh("https://geohide.ru/")=="https://geohide.ru/dns-query","known SmartDNS root normalized to DoH path");
        var applied=false;using(var stop=new CancellationTokenSource()) {
            try { await new AiDnsAutoSelector().RunAsync(_=>{},_=>{applied=true;return Task.FromResult(true);},stop.Token,(choice,token)=>{stop.Cancel();token.ThrowIfCancellationRequested();return Task.FromResult(Result(choice.ProviderKey,false,2));}); } catch(OperationCanceledException){}
            Check(!applied,"cancelled selection cannot apply");
        }
        var parsed=AdFilterService.Parse("||ads.example.com^\n||example.com^\n@@||good.example.com^\n||yandex.ru^\n||track.yandex.ru^\n||other.test^$script",[],[]);
        Check(parsed.Contains("ads.example.com")&&!parsed.Contains("example.com")&&!parsed.Contains("yandex.ru")&&parsed.Contains("track.yandex.ru")&&!parsed.Contains("other.test"),"conservative rules, exclusions, protected roots");
        Check(AdFilterService.Allowlist("пример.рф").Single().StartsWith("xn--"),"IDN allowlist");
        var matcher=await AdFilterService.PrepareAsync(root,"ads.example.com");
        Check(AdFilterService.State.Count>180000,"offline extended base");
        var full=JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(node,enableAdBlock:true,adBlockMatcher:matcher,adBlockAllowlist:"ads.example.com",customDirectRules:"an.yandex.ru"))!;
        Check(full["routing"]!["rules"]![0]!["outboundTag"]!.ToString()=="block","adblock before DNS/direct");
        Check(full["dns"]!["hosts"]![matcher]!=null,"DNS blocks ads before upstream/FakeDNS");
        Check(!full.ToJsonString().Contains("category-ads-all"),"opaque legacy base disabled for exceptions");
        await Validate(root,full.ToJsonString());
        await Validate(root,new XrayConfigGenerator().GenerateConfig(node,enableAdBlock:true,isFakeDns:true,enableTun:true,adBlockMatcher:matcher));
        Check(!new XrayConfigGenerator().GenerateConfig(node,enableAdBlock:false).Contains("category-ads-all"),"disabled adblock removes filter");
        await DnsFixture(root,full,matcher);
        Console.WriteLine($"PASS Android refresh: 28 real Xray AI configurations, seven providers, selection ranking/confirmation/cancellation; {AdFilterService.State.Count} offline ad domains, exclusions, real DNS filtering");
    }
    static async Task Validate(string root,string config) {
        var file=Path.Combine(Path.GetTempPath(),"splate-refresh-"+Guid.NewGuid().ToString("N")+".json");
        try { await File.WriteAllTextAsync(file,config);var start=new ProcessStartInfo(Path.Combine(root,"core","xray.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"run","-test","-c",file})start.ArgumentList.Add(arg);start.Environment["XRAY_LOCATION_ASSET"]=AdFilterService.EnsureAssets(root);using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(15000);try {await process.WaitForExitAsync(timeout.Token);}finally {if(!process.HasExited)process.Kill(true);}Check(process.ExitCode==0,"Xray validation: "+await stdout+await stderr); }
        finally {File.Delete(file);}
    }
    static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    static async Task DnsFixture(string root,JsonNode config,string matcher) {
        using var timeout=new CancellationTokenSource(15000);
        using var upstream=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));var upstreamPort=((IPEndPoint)upstream.Client.LocalEndPoint!).Port;
        var worker=Task.Run(async()=>{try{while(!timeout.IsCancellationRequested){var request=await upstream.ReceiveAsync(timeout.Token);var response=request.Buffer.Concat(new byte[]{0xc0,0x0c,0,1,0,1,0,0,0,60,0,4,127,0,0,99}).ToArray();response[2]=0x81;response[3]=0x80;response[6]=0;response[7]=1;await upstream.SendAsync(response,request.RemoteEndPoint,timeout.Token);}}catch(OperationCanceledException){} });
        var dnsPort=FreePort();var http=FreePort();var socks=FreePort();
        config["inbounds"]![0]!["port"]=socks;config["inbounds"]![1]!["port"]=http;
        config["inbounds"]!.AsArray().Add(new JsonObject{["tag"]="test-dns",["listen"]="127.0.0.1",["port"]=dnsPort,["protocol"]="dokodemo-door",["settings"]=new JsonObject{["address"]="1.1.1.1",["port"]=53,["network"]="udp"}});
        config["dns"]!["servers"]=new JsonArray(new JsonNode?[]{new JsonObject{["address"]="127.0.0.1",["port"]=upstreamPort}});
        config["routing"]!["rules"]!.AsArray().Insert(0,new JsonObject{["type"]="field",["inboundTag"]=new JsonArray(new JsonNode?[]{JsonValue.Create("test-dns")}),["outboundTag"]="dns-out"});
        config["routing"]!["rules"]!.AsArray().Insert(0,new JsonObject{["type"]="field",["inboundTag"]=new JsonArray(new JsonNode?[]{JsonValue.Create("dns-query")}),["outboundTag"]="direct"});
        var core=new XrayProcessManager(root);
        try {Check(await core.StartAsync(config.ToJsonString(),http,socks,timeout.Token),"DNS filter fixture starts");
            async Task<byte[]> Query(string domain){using var udp=new UdpClient();var packet=new List<byte>(new byte[]{1,2,1,0,0,1,0,0,0,0,0,0});foreach(var label in domain.Split('.')){packet.Add((byte)label.Length);packet.AddRange(Encoding.ASCII.GetBytes(label));}packet.AddRange(new byte[]{0,0,1,0,1});await udp.SendAsync(packet.ToArray(),new IPEndPoint(IPAddress.Loopback,dnsPort),timeout.Token);return (await udp.ReceiveAsync(timeout.Token)).Buffer;}
            var blocked=await Query("an.yandex.ru");Check(blocked.TakeLast(4).SequenceEqual(new byte[]{0,0,0,0}),"actual DNS ad response zero");
            var allowed=await Query("ads.example.com");Check(allowed.TakeLast(4).SequenceEqual(new byte[]{127,0,0,99}),"actual DNS allowlist reaches upstream");
        } finally {core.Stop();timeout.Cancel();await worker;}
    }
}
