using System.Text.Json;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class NodePingLiveChecks
{
    public static async Task RunAsync()
    {
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SplateVPN");
        var nodes = JsonSerializer.Deserialize<List<ServerNode>>(await File.ReadAllTextAsync(Path.Combine(data,"servers.json")))!;
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(data,"settings.json")));
        var route = await ProbeRoute.CaptureAsync(CancellationToken.None);
        Console.WriteLine($"TUN={route.HadTun}; uplink={route.Uplink?.Name}; index={route.Uplink?.Index}");
        var selected = settings.RootElement.TryGetProperty("SelectedServerId", out var id) ? id.GetString() : null;
        var sample = nodes.OrderByDescending(n=>n.IsSelected||n.Id==selected).ThenBy(n=>n.Ping>0?n.Ping:long.MaxValue).Take(10).ToList();
        var core = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SplateVPN", "core", "xray.exe");
        var domainNode=nodes.Where(n=>n.Ping>0&&!System.Net.IPAddress.TryParse(n.Address,out _)).OrderBy(n=>n.Ping).FirstOrDefault();
        if(domainNode!=null){using var limit=new CancellationTokenSource(6000);try{var bound=await route.ResolveAsync(domainNode.Address,limit.Token);Console.WriteLine($"Domain direct DNS answers={bound.Length}");}catch(Exception ex){Console.WriteLine("Domain direct DNS error="+ex.GetType().Name);}try{var system=await System.Net.Dns.GetHostAddressesAsync(domainNode.Address);Console.WriteLine($"Domain system DNS answers={system.Length}");}catch(Exception ex){Console.WriteLine("Domain system DNS error="+ex.GetType().Name);}}
        foreach (var node in sample.Take(3))
        {
            var tcp = await PingService.MeasureTcpOnRouteAsync(node.Address,node.Port,3000,route);
            var http = await PingService.MeasureRealPingSingleAsync(node,core,timeoutMs:5000);
            Console.WriteLine($"sample={sample.IndexOf(node)}; protocol={node.Protocol}/{node.Network}/{node.Security}; stored={node.Ping}; TCP={tcp}; HTTPS={http}; hostname={!System.Net.IPAddress.TryParse(node.Address,out _)}");
        }
        var clock=System.Diagnostics.Stopwatch.StartNew();
        await PingService.MeasureRealPingBatchAsync(sample,core,timeoutMs:3000,onSingleNodePinged:(node,ping)=>Console.WriteLine($"batch sample={sample.IndexOf(node)} HTTPS={ping}; elapsed={clock.ElapsedMilliseconds}"));
    }
}
