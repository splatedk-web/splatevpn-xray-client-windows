using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class BalancerChecks
{
    public static void Run()
    {
        long now=0;var policy=new BalancerPolicy(()=>now);
        var s=new AppSettings {AutoFallbackFastestServer=true};policy.Start(s);
        Check(policy.Observe(1800,s,false,0,true)==BalancerPolicy.Decision.Keep,"successful high ping does not trigger failure");
        Check(policy.Observe(-1,s,false,0,true)==BalancerPolicy.Decision.ConfirmFailure,"two confirmations require the second independent check");
        policy.ConfirmedSuccess();s.BalancerFailureConfirmations=5;
        for(var i=0;i<3;i++)Check(policy.Observe(-1,s,false,0,true)==BalancerPolicy.Decision.Keep,"failure threshold honored");
        Check(policy.Observe(-1,s,false,0,true)==BalancerPolicy.Decision.ConfirmFailure,"fifth check requested only after four failures");
        policy.Start(s);Check(policy.Observe(-1,s,false,0,false)==BalancerPolicy.Decision.Keep,"offline physical network does not cause churn");
        s.BalancerMode="initial_only";Check(policy.Observe(-1,s,false,0,true)==BalancerPolicy.Decision.Keep,"initial-only does not switch");
        s.BalancerMode="failover";policy.Start(s);now=600000;
        Check(policy.Observe(100,s,false,0,true)==BalancerPolicy.Decision.Keep,"failover never optimizes healthy connection");
        s.BalancerMode="balanced";policy.Start(s);now+=301000;
        Check(policy.Observe(100,s,true,0,true)==BalancerPolicy.Decision.Keep,"speed test pauses optimization");
        policy.Start(s);now+=301000;Check(policy.Observe(100,s,false,200000,true)==BalancerPolicy.Decision.Keep,"active download pauses optimization");
        policy.Start(s);now+=301000;Check(policy.Observe(100,s,false,0,true)==BalancerPolicy.Decision.Optimize,"periodic optimization when idle");
        policy.Switched();Check(!policy.CanSwitch(s,false)&&!policy.CanSwitch(s,true),"cooldown after switch");now+=3001;
        Check(policy.CanSwitch(s,true)&&!policy.CanSwitch(s,false),"dead-node reserve has independent short cooldown");now+=60000;
        Check(policy.CanSwitch(s,false),"optimization cooldown expires");
        Check(BalancerPolicy.IsImprovement(100,70,s)&&!BalancerPolicy.IsImprovement(100,71,s),"minimum gain boundary");
        var traffic=new TrafficAccumulator();traffic.NewCore(false);
        var first=traffic.Observe(100,200,1);var next=traffic.Observe(400,1000,2);
        Check(first.Download==100&&first.Upload==200&&next.DownBps==150&&next.UpBps==400,"first bytes retained and elapsed time used");
        traffic.NewCore(true);var switched=traffic.Observe(50,90,.1);
        Check(switched.Download==450&&switched.Upload==1090,"traffic session survives core switch");
        traffic.NewCore(false);Check(traffic.Observe(1,2,1).Upload==2,"new session resets totals");
        Console.WriteLine("PASS balancer policies: modes, thresholds, network failures, cooldown, speedtest/traffic pause, improvement; traffic continuity");
    }
    private static void Check(bool condition,string name){if(!condition)throw new Exception(name);}
}
