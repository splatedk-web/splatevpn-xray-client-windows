using Microsoft.Win32;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;
using SplateClientDesktop.ViewModels;

internal static class SandboxProxyPreflightChecks
{
    public static Task RunAsync()
    {
        if (!Environment.UserName.Equals("WDAGUtilityAccount",StringComparison.OrdinalIgnoreCase)) throw new Exception("Windows Sandbox only: this test changes the guest proxy.");
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            app.Dispatcher.BeginInvoke(async()=>{try{await CheckAsync();done.SetResult();}catch(Exception e){done.SetException(e);}finally{app.Shutdown();}});Dispatcher.Run();
        }){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();return done.Task;
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
    private static async Task CheckAsync()
    {
        const string path=@"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        using var key=Registry.CurrentUser.CreateSubKey(path);
        var before=new[]{"ProxyEnable","ProxyServer","ProxyOverride","AutoConfigURL"}.Select(n=>(Name:n,Value:key.GetValue(n),Kind:key.GetValue(n)==null?RegistryValueKind.String:key.GetValueKind(n))).ToArray();
        var data=Path.Combine(Path.GetTempPath(),"splate-preflight-guest-"+Guid.NewGuid().ToString("N"));
        var store=new ServerStorage(data);var node=new ServerNode{Id="preflight-freedom",Address="1.1.1.1",Protocol="freedom",Security="none",Port=443};
        store.SaveServers(new(){node});store.SaveSubscriptions(new());store.SaveSettings(new AppSettings{SettingsSchemaVersion=2,PresetSubscriptionsInitialized=true,AutoUpdateSubscription=false,EnableTunMode=true,RoutingMode="global",AiRoutingMode="vpn",EnableAdBlock=false});
        MainViewModel? vm=null;
        try
        {
            key.SetValue("ProxyEnable",1);key.SetValue("ProxyServer","http=127.0.0.1:9;https=127.0.0.1:9");key.SetValue("AutoConfigURL","http://127.0.0.1:9/fixture.pac");
            var core=new XrayProcessManager(AppContext.BaseDirectory);
            vm=new(store,core,new ConfigExportImportService(store),new TrafficMonitorService(),false);
            node = vm.Servers.Single();
            vm.LogMessageReceived+=Console.WriteLine;
            var approved=false;var prompts=0;
            vm.ConfirmationRequested=(_,text)=>{prompts++;Check(text.Contains("TUN")&&text.Contains("PAC"),"guest preflight explains the actual manual/PAC conflict");return Task.FromResult(approved);};
            await vm.ToggleConnectionCommand.ExecuteAsync(null);
            Check(!vm.IsConnected&&!core.IsRunning&&SystemProxyManager.ReadStatus().HasConflict,"decline preserves guest proxy and prevents core/TUN startup");
            approved=true;await vm.ToggleConnectionCommand.ExecuteAsync(null);
            Check(vm.IsConnected&&vm.ActiveTunMode&&!SystemProxyManager.ReadStatus().HasConflict,"accept starts real TUN with manual proxy/PAC disabled");
            using(var http=new HttpClient(new SocketsHttpHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(30)})
            using(var response=await http.GetAsync("https://cp.cloudflare.com/generate_204"))Check(response.StatusCode==HttpStatusCode.NoContent,"real HTTPS flows through TUN after proxy suspension");
            var checks=prompts;await vm.SwitchServerAsync(node);Check(vm.IsConnected&&prompts==checks,"clean active TUN switches without repeated proxy prompt");
            vm.Disconnect();Check(SystemProxyManager.ReadStatus() is {Manual:true,Pac:true},"disconnect restores guest manual proxy and PAC");
            vm.EnableTunMode=false;await vm.ToggleConnectionCommand.ExecuteAsync(null);Check(vm.IsConnected&&!vm.ActiveTunMode,"proxy-mode session starts");
            vm.EnableTunMode=true;approved=false;await vm.SwitchServerAsync(node);
            Check(vm.IsConnected&&!vm.ActiveTunMode&&SystemProxyManager.ReadStatus().Manual,"declining proxy-to-TUN retains active proxy session");
            approved=true;await vm.SwitchServerAsync(node);Console.WriteLine($"TRANSITION: requestedTun={vm.Settings.EnableTunMode}; activeTun={vm.ActiveTunMode}; {vm.ConnectionDetail}");Check(vm.IsConnected&&vm.ActiveTunMode&&!SystemProxyManager.ReadStatus().HasConflict,"proxy-to-TUN suspends the original foreign proxy rather than restoring it");
            vm.Disconnect();Check(SystemProxyManager.ReadStatus() is {Manual:true,Pac:true},"proxy/TUN transition still restores original settings on exit");
        }
        finally
        {
            vm?.Disconnect();SystemProxyManager.DisableProxy();
            foreach(var value in before)if(value.Value==null)key.DeleteValue(value.Name,false);else key.SetValue(value.Name,value.Value,value.Kind);
            Directory.Delete(data,true);
        }
    }
}
