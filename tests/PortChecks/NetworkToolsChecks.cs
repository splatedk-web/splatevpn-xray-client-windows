using System.Text.Json.Nodes;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class NetworkToolsChecks
{
    static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    public static async Task RunAsync(string root)
    {
        foreach (var mode in new[] { "smart_ru", "global" }) foreach (var provider in SmartDnsProviders.All)
        {
            var config = JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(new ServerNode { Address="127.0.0.1", Security="none", Port=19881, Uuid="00000000-0000-0000-0000-000000000001" },
                mode:mode, aiRoutingMode:"dns_vpn", enableAiDns:true, enableAiBypass:true, aiDnsProvider:provider.Key, aiTlsFragment:true, uplinkInterface:"Ethernet", enableSniffing:false))!;
            var ai = config["outbounds"]!.AsArray().Single(o=>o?["tag"]?.ToString()=="ai-smartdns")!;
            Check(ai["streamSettings"]!["sockopt"]!["dialerProxy"]!.ToString()=="proxy" && ai["streamSettings"]!["sockopt"]!["domainStrategy"]!.ToString()=="ForceIPv4" && ai["settings"]?["fragment"]==null && ai["streamSettings"]!["sockopt"]?["interface"]==null, mode+" tunneled SmartDNS preserves relay resolution: "+provider.Key);
            Check(config["routing"]!["rules"]!.AsArray().Single(r=>r?["inboundTag"]?.ToJsonString().Contains("dns-xbox")==true)!["outboundTag"]!.ToString()=="proxy", "AI DoH travels through VPN");
            Check(config["inbounds"]!.AsArray().Where(i=>i?["tag"]?.ToString() is "http-in" or "socks-in").All(i=>i?["sniffing"]?["enabled"]?.GetValue<bool>()!=true),"sniffing stays off when requested");
            var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
            try { await File.WriteAllTextAsync(path,config.ToJsonString()); var start=new System.Diagnostics.ProcessStartInfo(Path.Combine(root,"core/xray.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"run","-test","-c",path}) start.ArgumentList.Add(arg);using var process=System.Diagnostics.Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();Check(process.ExitCode==0,"real Xray accepts SmartDNS via VPN: "+await stderr+await stdout); } finally { File.Delete(path); }
        }
        var s=new AppSettings {SettingsSchemaVersion=2,AiRoutingMode="dns_vpn",RoutingMode="global",Theme="sakura",SubscriptionUrl="private",AiDnsAutoSwitch=false};SettingsCompatibility.Normalize(s);
        Check(s.AiRoutingMode=="dns_vpn"&&s.EnableAiDns,"tunneled choice survives global normalization");
        var profile=ConnectionProfiles.Save(s,"Saved");s.DnsProvider="google";s.Theme="light";
        var restored=ConnectionProfiles.Target(s,profile.Snapshot);
        Check(restored.DnsProvider=="cloudflare"&&restored.AiRoutingMode=="dns_vpn"&&!restored.AiDnsAutoSwitch&&restored.Theme=="light"&&restored.SubscriptionUrl=="private","profiles restore only network settings");
        var invalid=JsonNode.Parse(profile.Snapshot)!;invalid["HttpPort"]=invalid["SocksPort"]!.DeepClone();bool rejected=false;try{ConnectionProfiles.Target(s,invalid.ToJsonString());}catch(ArgumentException){rejected=true;}Check(rejected,"invalid profile ports rejected before mutation");
        var redacted=DiagnosticRedactor.Redact("https://example.com/private token=secret 203.0.113.4 2001:db8::8 127.0.0.1 uuid=00000000-0000-0000-0000-000000000001 private.example",new[]{"private.example"});
        Check(!redacted.Contains("secret")&&!redacted.Contains("example")&&!redacted.Contains("203.0.113")&&!redacted.Contains("2001:db8")&&redacted.Contains("127.0.0.1"),"extended diagnostics redact secrets and endpoints");
        var dir=Path.Combine(Path.GetTempPath(),"splate-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try {var store=new ServerStorage(dir);store.SaveSettings(restored);File.WriteAllText(Path.Combine(dir,"settings.json"),"{broken");var recovered=store.LoadSettings();Check(recovered.AiRoutingMode=="dns_vpn"&&store.SettingsRecoveryNotice!=null&&Directory.GetFiles(dir,"*.damaged-*").Length==1,"corrupt settings recover from last-good without losing evidence");}
        finally {Directory.Delete(dir,true);}
        var old=new AiDnsChoice("dns_ai",false);var next=new AiDnsChoice("geohide",false);
        AiDnsCandidateResult Result(AiDnsChoice choice, params AiDnsOutcome[] outcomes)=>new(choice,outcomes.Select((o,i)=>new AiDnsTargetResult(new[]{"Gemini","ChatGPT","Claude"}[i],o)).ToArray());
        var probes=0;var replacement=await AiDnsFailover.FindAsync(old,(choice,ct)=>{probes++;return Task.FromResult(choice==old?Result(old,AiDnsOutcome.Failed,AiDnsOutcome.Accessible,AiDnsOutcome.Limited):Result(next,AiDnsOutcome.Accessible,AiDnsOutcome.Accessible,AiDnsOutcome.Limited));},CancellationToken.None,_=>Task.CompletedTask,new[]{next});
        Check(replacement?.Choice==next&&probes==5,"failover requires two failures two wins and final current check");
        probes=0;replacement=await AiDnsFailover.FindAsync(old,(choice,ct)=>{probes++;return Task.FromResult(Result(choice,AiDnsOutcome.Limited,AiDnsOutcome.Accessible,AiDnsOutcome.Limited));},CancellationToken.None,_=>Task.CompletedTask,new[]{next});Check(replacement==null&&probes==1,"CAPTCHA never rotates providers");
        replacement=await AiDnsFailover.FindAsync(old,(choice,ct)=>Task.FromResult(choice==old?Result(old,AiDnsOutcome.Failed,AiDnsOutcome.Accessible,AiDnsOutcome.Limited):Result(next,AiDnsOutcome.Accessible,AiDnsOutcome.Failed,AiDnsOutcome.Limited)),CancellationToken.None,_=>Task.CompletedTask,new[]{next});Check(replacement==null,"failover preserves working services");
        var currentCalls=0;replacement=await AiDnsFailover.FindAsync(old,(choice,ct)=>Task.FromResult(choice==old?Result(old,++currentCalls>=3?AiDnsOutcome.Accessible:AiDnsOutcome.Failed,AiDnsOutcome.Accessible,AiDnsOutcome.Limited):Result(next,AiDnsOutcome.Accessible,AiDnsOutcome.Accessible,AiDnsOutcome.Limited)),CancellationToken.None,_=>Task.CompletedTask,new[]{next});Check(replacement==null,"recovered current provider avoids reconnect");
        using var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;try{await AiDnsFailover.FindAsync(old,(choice,ct)=>{ct.ThrowIfCancellationRequested();return Task.FromResult(Result(choice,AiDnsOutcome.Failed));},cancel.Token,_=>Task.CompletedTask);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"failover propagates cancellation");
    }
}
