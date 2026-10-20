using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;
using SplateClientDesktop.ViewModels;

internal static class ViewModelChecks
{
    public static Task RunAsync()
    {
        var result=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            app.Dispatcher.BeginInvoke(async ()=>
            {
                try {await CheckAsync();result.SetResult();}
                catch(Exception ex){result.SetException(ex);}
                finally{app.Shutdown();}
            });
            Dispatcher.Run();
        }) {IsBackground=true};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return result.Task;
    }
    private static async Task CheckAsync()
    {
        var store=new MemoryStorage();
        var a=new ServerNode {Id="a",Address="a.example",IsSelected=true};
        var b=new ServerNode {Id="b",Address="b.example",SubscriptionId="sub"};
        var c=new ServerNode {Id="c",Address="c.example"};
        store.Nodes=[a,b,c];store.Subscriptions=[new SubscriptionItem {Id="sub",Name="Subscription",Enabled=true}];
        var vm=new MainViewModel(store,new FakeCore(),new ConfigExportImportService(store),new FakeTraffic(),enableBackgroundWork:false);
        var gate=(SemaphoreSlim)typeof(MainViewModel).GetField("_connectionLock",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(vm)!;
        await gate.WaitAsync();
        var first=vm.SelectServerAsync(b);var latest=vm.SelectServerAsync(c);gate.Release();await Task.WhenAll(first,latest);
        Check(vm.SelectedServer==c,"latest queued choice wins");
        await gate.WaitAsync();var pending=vm.SelectServerAsync(b);await vm.SelectServerAsync(c);gate.Release();await pending;
        Check(vm.SelectedServer==c,"choosing current node cancels an older pending choice");
        await gate.WaitAsync();pending=vm.SelectServerAsync(b);vm.SetSubscriptionOptions(vm.Subscriptions[0],false,null);gate.Release();await pending;
        Check(vm.SelectedServer==c,"disabled subscription rejected after waiting for connection lock");
        try{await vm.SelectServerAsync(b);throw new Exception("Disabled subscription accepted");}catch(InvalidOperationException){}
        vm.SetSubscriptionOptions(vm.Subscriptions[0],true,null);
        await gate.WaitAsync();pending=vm.SelectServerAsync(b);vm.Servers.Remove(b);gate.Release();await pending;
        Check(vm.SelectedServer==c,"removed queued server cannot be selected");
        vm.Servers.Add(b);await vm.SelectServerAsync(b);vm.SetSubscriptionOptions(vm.Subscriptions[0],false,null);
        Check(vm.SelectedServer!=b&&vm.IsServerEnabled(vm.SelectedServer!),"disabling selected subscription chooses eligible node");
        vm.ConfirmationRequested=(_,_)=>Task.FromResult(true);
        await vm.DeleteSubscriptionCommand.ExecuteAsync(vm.Subscriptions[0]);
        Check(!vm.Servers.Contains(b)&&vm.Subscriptions.Count==0&&vm.SelectedServer!=b,"delete subscription removes owned nodes");
        store.FailNextSubscriptionSave=true;
        var oldIds=store.Nodes.Select(n=>n.Id).ToArray();
        try{((IServerStorage)store).SaveCatalog([],[]);throw new Exception("Write failure ignored");}catch(IOException){}
        Check(store.Nodes.Select(n=>n.Id).SequenceEqual(oldIds),"catalog rolls back server list when subscription write fails");
        vm.UiNotificationRequested+=(_,_,_)=>{};
        var before=vm.Servers.Count;
        var linkA="trojan://password%40one@same.example:443?security=tls#one";
        var linkB="trojan://password%40two@same.example:443?security=tls#two";
        await vm.ProcessAnyLinkAsync(linkA+"\n"+linkB);
        Check(vm.Servers.Count==before+2,"different credentials at one endpoint imported independently");
        await vm.ProcessAnyLinkAsync(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(linkA+"\n"+linkB)));
        Check(vm.Servers.Count==before+2,"Base64 import deduplicates identical configurations");
        var share=ServerShare.Link(vm.Servers.Last());
        var qrPath=Path.Combine(Path.GetTempPath(),"splate-qr-test-"+Guid.NewGuid().ToString("N")+".png");
        try
        {
            File.WriteAllBytes(qrPath,Convert.FromBase64String(ServerShare.Qr(share).Split(',')[1]));
            Check(await new QrCodeScannerService().ScanFromFileAsync(qrPath)==share,"QR generation/scanning roundtrip");
        }
        finally{File.Delete(qrPath);}
        await ConnectedSessionChecks();
        await PingProgressChecks();
        Console.WriteLine("PASS view model: queued selection, subscription lifecycle, persistent TUN, rollback, readiness without HTTP gate, balancer confirmations, stale callbacks and cancellation");
    }
    private static async Task PingProgressChecks()
    {
        var first = new ServerNode { Id="ping-first", Ping=77 };
        var second = new ServerNode { Id="ping-second", Ping=88 };
        var store = new MemoryStorage { Nodes=[first,second] };
        var probes = new TestProbes { HoldCandidates = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };
        var platform = new TestPlatform();
        var vm = new MainViewModel(store,new LiveCore(),new ConfigExportImportService(store),new FakeTraffic(),false,platform,probes);
        vm.PingMode="http";
        var updates=new List<(int Completed,int Total,bool Busy)>();
        vm.PingProgressUpdated+=(done,total,busy)=>updates.Add((done,total,busy));
        var scan=vm.PingAllServersInternalAsync();
        await Dispatcher.Yield();
        Check(!scan.IsCompleted && first.Ping==40 && second.Ping==88 && updates.Contains((1,2,true)),"ping result and progress appear before whole scan completes");
        vm.CancelPing();await scan;
        Check(!vm.IsPinging && second.Ping==88 && updates.Last()==(1,2,false),"cancel clears busy state and preserves untested results");
        probes.HoldCandidates=null;
        await vm.PingAllServersInternalAsync();
        Check(first.Ping==40 && second.Ping==40 && updates.Last()==(2,2,false) && platform.TunStops==0,"scan restarts after cancellation without touching TUN");
    }
    private static async Task ConnectedSessionChecks()
    {
        var store=new MemoryStorage();
        var a=new ServerNode {Id="live-a",Address="127.0.0.1",Security="none"};
        var b=new ServerNode {Id="live-b",Address="127.0.0.2",Security="none"};
        var c=new ServerNode {Id="live-c",Address="127.0.0.3",Security="none"};
        store.Nodes=[a,b,c];store.LoadSettings().EnableTunMode=true;
        var core=new LiveCore();var platform=new TestPlatform();var probes=new TestProbes();
        long clock=0;
        var traffic=new FakeTraffic();
        var vm=new MainViewModel(store,core,new ConfigExportImportService(store),traffic,false,platform,probes,new BalancerPolicy(()=>clock));
        await vm.ToggleConnectionCommand.ExecuteAsync(null);
        Check(vm.IsConnected&&platform.TunStarts==1&&probes.CurrentCalls==0,"startup uses local readiness, not an external HTTP gate");
        platform.UplinkName="Беспроводная сеть";
        var uplinkReads=platform.UplinkReads;
        core.EmitExitDuringStart=true;
        await vm.SelectServerAsync(b);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(vm.IsConnected&&vm.SelectedServer==b&&platform.TunStarts==1&&platform.TunStops==0,"switch preserves TUN and ignores old core exit callbacks");
        Check(platform.UplinkReads>uplinkReads && System.Text.Json.Nodes.JsonNode.Parse(core.LastConfig)!["outbounds"]![0]!["streamSettings"]!["sockopt"]!["interface"]!.ToString()==platform.UplinkName,"switch resolves current Unicode interface instead of cached uplink");
        clock=4000;
        core.FailNext=true;
        await vm.SelectServerAsync(c);
        Check(vm.IsConnected&&vm.SelectedServer==b&&core.IsRunning&&platform.TunStops==0,"failed new core rolls back without dropping VPN");
        var resumes=traffic.Resumes;
        core.RejectNext=true;await vm.SelectServerAsync(c);
        Check(vm.IsConnected&&vm.SelectedServer==b&&traffic.Resumes==resumes,"preflight rejection preserves existing traffic baseline");
        vm.AutoFallbackFastestServer=true;vm.Settings.BalancerMode="failover";
        probes.Latencies.Enqueue(2000);
        await Health(vm);
        Check(vm.SelectedServer==b&&vm.IsConnected,"high but successful ping is not a failure");
        probes.Latencies.Enqueue(-1);probes.Latencies.Enqueue(80);
        await Health(vm);
        Check(vm.SelectedServer==b&&probes.CandidateCalls==0,"one failed sample followed by confirmation success cannot switch");
        probes.Latencies.Enqueue(-1);probes.Latencies.Enqueue(-1);probes.CandidatePing=-1;
        await Health(vm);
        Check(vm.IsConnected&&vm.SelectedServer==b&&core.IsRunning,"no verified reserve keeps VPN running");
        probes.Latencies.Enqueue(-1);probes.Latencies.Enqueue(-1);probes.CandidatePing=40;
        var unstable1=new ServerNode{Id="unstable-1",Address="127.0.0.11",Security="none"};
        var unstable2=new ServerNode{Id="unstable-2",Address="127.0.0.12",Security="none"};
        vm.Servers.Add(unstable1);vm.Servers.Add(unstable2);probes.FailConfirmation=true;
        await Health(vm);
        Check(vm.IsConnected&&vm.SelectedServer==b&&platform.TunStops==0,"unstable candidates fail second check without dropping current TUN");
        vm.Servers.Remove(unstable1);vm.Servers.Remove(unstable2);probes.FailConfirmation=false;
        probes.Latencies.Enqueue(-1);probes.Latencies.Enqueue(-1);
        var reserve=new ServerNode {Id="fresh-reserve",Address="127.0.0.4",Security="none"};vm.Servers.Add(reserve);
        await Health(vm);
        Check(vm.IsConnected&&vm.SelectedServer==reserve&&platform.TunStops==0,"confirmed failure switches to verified reserve without dismantling TUN");
        vm.Settings.BalancerMode="initial_only";
        var calls=probes.CurrentCalls;await Health(vm);
        Check(vm.IsConnected&&probes.CurrentCalls==calls,"initial-only mode does not monitor connection");
        vm.Settings.BalancerMode="failover";
        var healthHold=new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);probes.Hold=healthHold;
        var pending=Health(vm);await Dispatcher.Yield();vm.Disconnect();healthHold.TrySetResult(-1);await pending;
        Check(!vm.IsConnected&&!core.IsRunning,"stale health result cannot restart explicitly stopped VPN");
        var coreHold=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);core.HoldStart=coreHold;
        vm.AutoFallbackFastestServer=false;
        var start=vm.ToggleConnectionCommand.ExecuteAsync(null);await Dispatcher.Yield();vm.Disconnect();coreHold.TrySetResult(true);await start;
        Check(!vm.IsConnected&&!core.IsRunning,"cancelled startup stays stopped");
        await vm.ToggleConnectionCommand.ExecuteAsync(null);
        var rollbackHold=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        core.FailNext=true;core.HoldRollback=rollbackHold;
        var replacement=vm.SelectServerAsync(b);await Dispatcher.Yield();
        vm.Disconnect();rollbackHold.TrySetResult(true);await replacement;
        Check(!vm.IsConnected&&!core.IsRunning&&!platform.IsTunRunning,"stop during rollback cannot restart VPN");
        core.FailNext=false;core.RejectNext=false;core.HoldStart=null;core.HoldRollback=null;
        vm.AutoFallbackFastestServer=true;probes.CandidatePing=-1;
        await vm.ToggleConnectionCommand.ExecuteAsync(null);
        Check(!vm.IsConnected&&!platform.IsTunRunning&&vm.StatusText=="НЕТ ПРОВЕРЕННЫХ УЗЛОВ","failed automatic scan cannot silently connect the old unverified node");
        vm.AutoFallbackFastestServer=false;
        await vm.ToggleConnectionCommand.ExecuteAsync(null);
        Check(vm.IsConnected,"manual connection still starts without an HTTP health gate: "+vm.StatusText+"; "+vm.ConnectionDetail+"; "+vm.Logs);
        c.Address="replacement.invalid";
        var resolves=probes.ResolveCalls;
        await vm.SelectServerAsync(c);
        var replacementConfig=System.Text.Json.Nodes.JsonNode.Parse(core.LastConfig)!;
        Check(vm.IsConnected && vm.SelectedServer==c && probes.ResolveCalls>resolves &&
            replacementConfig["dns"]!["hosts"]![c.Address]![0]!.ToString()=="127.0.0.1",
            "replacement hostname uses isolated resolver and pins the resulting address");
        Check(replacementConfig["dns"]!["hosts"]![XrayConfigGenerator.ServerAddressAlias(c)!]![0]!.ToString()=="127.0.0.1",
            "resolved replacement address also pins the VLESS alias");
        vm.Disconnect();
        var pendingStart=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        core.HoldStart=pendingStart;
        var initialStart=vm.ToggleConnectionCommand.ExecuteAsync(null);
        for(var i=0;i<250 && core.HoldStart!=null;i++) await Task.Delay(20);
        Check(core.HoldStart==null,"test reached core startup before selecting a replacement");
        Check(vm.IsConnecting,"startup is in progress before replacement selection");
        var selectDuringStartup=vm.SelectServerAsync(b);
        await Task.WhenAll(initialStart,selectDuringStartup).WaitAsync(TimeSpan.FromSeconds(10));
        Check(vm.IsConnected && vm.SelectedServer==b && core.IsRunning,
            "selecting another server during startup continues connection on the latest selection");
        vm.Disconnect();
        await RuntimeChecks.RunAsync(vm);
        static Task Health(MainViewModel model)=>(Task)typeof(MainViewModel).GetMethod("RunHealthCheckAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(model,null)!;
    }
    private static void Check(bool value,string label){if(!value)throw new Exception(label);}
    private sealed class MemoryStorage:IServerStorage
    {
        public List<ServerNode> Nodes=[];
        public List<SubscriptionItem> Subscriptions=[];
        public bool FailNextSubscriptionSave;
        private AppSettings _settings=new(){PresetSubscriptionsInitialized=true,AutoUpdateSubscription=false};
        public List<ServerNode> LoadServers()=>Nodes.ToList();
        public void SaveServers(List<ServerNode> nodes)=>Nodes=nodes.ToList();
        public Task SaveServersAsync(List<ServerNode> nodes){SaveServers(nodes);return Task.CompletedTask;}
        public List<SubscriptionItem> LoadSubscriptions()=>Subscriptions.ToList();
        public void SaveSubscriptions(List<SubscriptionItem> subscriptions){if(FailNextSubscriptionSave){FailNextSubscriptionSave=false;throw new IOException("Injected write failure");}Subscriptions=subscriptions.ToList();}
        public AppSettings LoadSettings()=>_settings;
        public void SaveSettings(AppSettings settings)=>_settings=settings;
    }
    private sealed class FakeCore:IXrayProcessManager
    {
        public event Action<string>? OnLogMessage {add{} remove{}}
        public event Action<int>? OnProcessExited {add{} remove{}}
        public bool IsRunning=>false;
        public Task<bool> StartAsync(string json,int httpPort=10809,int socksPort=10808,CancellationToken ct=default)=>Task.FromResult(false);
        public void Stop(){}
    }
    private sealed class FakeTraffic:ITrafficMonitorService
    {
        public int Resumes;
        public event Action<string,string>? SpeedUpdated {add{} remove{}}
        public event Action<long,long>? BytesPerSecUpdated {add{} remove{}}
        public event Action<long,long>? SessionBytesUpdated {add{} remove{}}
        public void Start(int port){}
        public void Resume(int port){Resumes++;}
        public void Stop(){}
    }
    private sealed class LiveCore:IXrayProcessManager
    {
        public event Action<string>? OnLogMessage {add{} remove{}}
        public event Action<int>? OnProcessExited;
        public bool IsRunning{get;private set;}
        public bool FailNext,EmitExitDuringStart;
        public string LastConfig="";
        public bool RejectNext;
        public TaskCompletionSource<bool>? HoldStart;
        public TaskCompletionSource<bool>? HoldRollback;
        public async Task<bool> StartAsync(string json,int httpPort=10809,int socksPort=10808,CancellationToken ct=default)
        {
            if(RejectNext){RejectNext=false;return false;}
            if(EmitExitDuringStart){OnProcessExited?.Invoke(0);EmitExitDuringStart=false;}
            IsRunning=false;
            if(HoldStart!=null) {var held=HoldStart;HoldStart=null;await held.Task.WaitAsync(ct);}
            ct.ThrowIfCancellationRequested();
            if(FailNext){FailNext=false;HoldStart=HoldRollback;HoldRollback=null;return false;}
            LastConfig=json;IsRunning=true;return true;
        }
        public void Stop()=>IsRunning=false;
    }
    private sealed class TestPlatform:IConnectionPlatform
    {
        public int TunStarts,TunStops,UplinkReads;
        public string UplinkName="Test";
        public bool IsTunRunning{get;private set;}
        public bool IsAdministrator=>true;
        public event Action<int>? OnTunExited {add{}remove{}}
        public Task<NetworkUplink> GetUplinkAsync(CancellationToken token){UplinkReads++;return Task.FromResult(new NetworkUplink(UplinkName,1,"192.0.2.1"));}
        public Task<bool> StartTunAsync(int port,NetworkUplink uplink,bool blockIpv6,Action<string> log,CancellationToken token){TunStarts++;IsTunRunning=true;return Task.FromResult(true);}
        public void StopTun(Action<string> log){if(IsTunRunning)TunStops++;IsTunRunning=false;}
        public void EnableProxy(int h,int s){}
        public void DisableProxy(){}
    }
    private sealed class TestProbes:IConnectionProbes
    {
        public Queue<long> Latencies=new();
        public long CandidatePing=40;
        public bool FailConfirmation;
        public int CurrentCalls,CandidateCalls;
        public int ResolveCalls;
        public Task<System.Net.IPAddress[]> ResolveServerAsync(string host,CancellationToken token)
        { token.ThrowIfCancellationRequested(); ResolveCalls++; return Task.FromResult(new[]{System.Net.IPAddress.Loopback}); }
        public TaskCompletionSource<long>? Hold;
        public TaskCompletionSource<bool>? HoldCandidates;
        public bool NetworkAvailable=>true;
        public async Task<long> CurrentAsync(int port,int timeout,CancellationToken token)
        {CurrentCalls++;if(Hold!=null){var held=Hold;Hold=null;return await held.Task.WaitAsync(token);}return Latencies.TryDequeue(out var n)?n:100;}
        public async Task CandidatesAsync(IReadOnlyList<ServerNode> nodes,Action<ServerNode,long> result,CancellationToken token)
        {CandidateCalls++;foreach(var n in nodes){token.ThrowIfCancellationRequested();result(n,FailConfirmation&&nodes.Count==1?-1:CandidatePing);if(HoldCandidates!=null)await HoldCandidates.Task.WaitAsync(token);}}
    }
}
