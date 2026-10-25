using Microsoft.Win32;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;
using System.Net;
using System.Net.Sockets;

internal static class ProxyPreflightChecks
{
    static void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
    public static void Run()
    {
        var path=@"Software\SplateVPN-Tests\Proxy-"+Guid.NewGuid().ToString("N");
        var directory=Path.Combine(Path.GetTempPath(),"splate-preflight-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var snapshot=Path.Combine(directory,"proxy.json");
        try
        {
            using var key=Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("ProxyEnable",1);key.SetValue("ProxyServer","http=127.0.0.1:9");key.SetValue("ProxyOverride","example.local");key.SetValue("AutoConfigURL","https://fixture.invalid/proxy.pac");
            string Original()=>string.Join('|',new[]{"ProxyEnable","ProxyServer","ProxyOverride","AutoConfigURL"}.Select(n=>n+":"+key.GetValue(n)));
            var original=Original();var session=new ProxySession(path,snapshot,()=>{});
            var conflict=session.InspectForTun();Check(conflict.Manual&&conflict.Pac&&Original()==original,"manual/PAC inspection is read-only");
            session.SuspendForTun(conflict);Check(!session.ReadStatus().HasConflict&&File.Exists(snapshot),"TUN disables manual proxy and PAC with durable restore snapshot");
            session.SuspendForTun(conflict);Check(!session.ReadStatus().HasConflict,"TUN rollback may reuse the original approval");
            session.Restore();Check(Original()==original&&!File.Exists(snapshot),"disconnect restores exact original manual/PAC values");
            session.Enable(10809);Check(session.InspectForTun()==conflict,"proxy-to-TUN detects the hidden original foreign proxy");
            Check(session.OriginalHttpProxy() == new HttpProxyEndpoint("127.0.0.1",9),"import while connected reads the original proxy rather than Splate itself");
            session.SuspendForTun(conflict);session.Enable(11809);session.Enable(12809);session.Restore();Check(Original()==original,"proxy/TUN/proxy and port changes retain original restoration ownership");
            session.SuspendForTun(conflict);new ProxySession(path,snapshot,()=>{}).Recover();Check(Original()==original,"fresh process restores proxy after interrupted TUN session");
            session=new(path,snapshot,()=>{});session.Enable(10809);key.SetValue("ProxyEnable",0);key.SetValue("AutoConfigURL","https://changed.invalid/new.pac");var changed=Original();session.Restore();Check(Original()==changed,"disconnect preserves external changes, including PAC-only edits");
            session=new(path,snapshot,()=>{});var approved=session.InspectForTun();key.SetValue("ProxyServer","changed-after-inspection");changed=Original();bool refused=false;try{session.SuspendForTun(approved);}catch(IOException){refused=true;}Check(refused&&Original()==changed,"stale proxy approval cannot overwrite newer Windows settings");
            Directory.CreateDirectory(Path.Combine(directory,"core"));File.WriteAllText(Path.Combine(directory,"core/xray.exe"),"");
            int Port(){using var l=new TcpListener(IPAddress.Loopback,0);l.Start();return ((IPEndPoint)l.LocalEndpoint).Port;}
            var settings=new AppSettings{HttpPort=Port(),SocksPort=Port()};ConnectionPreflight.Check(directory,settings,Array.Empty<int>());
            using(var occupied=new TcpListener(IPAddress.Loopback,settings.HttpPort))
            {occupied.Start();refused=false;try{ConnectionPreflight.Check(directory,settings,Array.Empty<int>());}catch(IOException ex){refused=ex.Message.Contains(settings.HttpPort.ToString());}Check(refused,"preflight reports the exact occupied TCP port");ConnectionPreflight.Check(directory,settings,new[]{settings.HttpPort});}
            using(var udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,settings.SocksPort)))
            {refused=false;try{ConnectionPreflight.Check(directory,settings,Array.Empty<int>());}catch(IOException){refused=true;}Check(refused,"preflight detects occupied SOCKS UDP port");}
            settings.EnableTunMode=true;refused=false;try{ConnectionPreflight.Check(directory,settings,Array.Empty<int>());}catch(IOException ex){refused=ex.Message.Contains("tun2socks.exe");}Check(refused,"missing TUN component is detected before starting core");
        }
        finally {Registry.CurrentUser.DeleteSubKeyTree(path,false);Directory.Delete(directory,true);}
    }
}
