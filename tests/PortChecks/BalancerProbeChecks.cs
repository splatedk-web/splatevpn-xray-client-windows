using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class BalancerProbeChecks
{
    public static async Task RunAsync(string root)
    {
        void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
        var node=new ServerNode{Address="vpn.example",Port=443,Security="reality",PublicKey="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",Uuid="00000000-0000-0000-0000-000000000001"};
        var bound=PingService.BuildProbeConfig([(node,39101)],"Беспроводная сеть");
        Check(bound["outbounds"]!.AsArray().All(o=>o!["streamSettings"]!["sockopt"]!["interface"]!.ToString()=="Беспроводная сеть"),"all candidate/direct sockets bind native uplink");
        Check(bound["dns"]!["servers"]![0]!.ToString()=="https://cloudflare-dns.com/dns-query" && bound["dns"]!["tag"]!.ToString()=="probe-dns","DNS does not use the captured Windows resolver in TUN");
        Check(bound["routing"]!["rules"]![0]!["outboundTag"]!.ToString()=="direct","probe DNS has its own bound direct route");
        Check(bound["outbounds"]![0]!["streamSettings"]!["security"]!.ToString()=="reality","original candidate security preserved");
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();
            try{await PingService.MeasureRealPingBatchAsync([node],Path.Combine(root,"core","xray.exe"),ct:cancelled.Token);throw new Exception("Cancelled scan continued");}catch(OperationCanceledException){}
        }
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var target=new TcpListener(IPAddress.Loopback,0);target.Start();var targetPort=((IPEndPoint)target.LocalEndpoint).Port;
        int status=204;
        var handlers=new System.Collections.Concurrent.ConcurrentBag<Task>();
        var responder=Task.Run(async()=>{
            try{while(!timeout.IsCancellationRequested){var connection=await target.AcceptTcpClientAsync(timeout.Token);handlers.Add(Task.Run(async()=>{using(connection){try{var stream=connection.GetStream();var buffer=new byte[4096];var count=await stream.ReadAsync(buffer,timeout.Token);if(Encoding.ASCII.GetString(buffer,0,count).Contains("/stall")){await Task.Delay(Timeout.Infinite,timeout.Token);return;}var response=status==204?"HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n":"HTTP/1.1 200 OK\r\nContent-Length: 7\r\nConnection: close\r\n\r\nblocked";await stream.WriteAsync(Encoding.ASCII.GetBytes(response),timeout.Token);}catch(OperationCanceledException){}}}));}}
            catch(OperationCanceledException){}
        });
        var vlessPort=Port();var goodPort=Port();var badPort=Port();
        var good=new ServerNode{Address="127.0.0.1",Port=vlessPort,Protocol="vless",Security="none",Uuid="00000000-0000-0000-0000-000000000001"};
        var bad=new ServerNode{Address="127.0.0.1",Port=vlessPort,Protocol="vless",Security="none",Uuid="00000000-0000-0000-0000-000000000002"};
        var config=PingService.BuildProbeConfig([(good,goodPort),(bad,badPort)],null);
        config["inbounds"]!.AsArray().Add(new JsonObject{["tag"]="fixture-vless",["listen"]="127.0.0.1",["port"]=vlessPort,["protocol"]="vless",["settings"]=new JsonObject{["decryption"]="none",["clients"]=new JsonArray{new JsonObject{["id"]=good.Uuid}}}});
        config["outbounds"]!.AsArray().Add(new JsonObject{["tag"]="fixture-direct",["protocol"]="freedom",["settings"]=new JsonObject{["redirect"]=$"127.0.0.1:{targetPort}",["finalRules"]=new JsonArray{new JsonObject{["action"]="allow",["ip"]=new JsonArray{"127.0.0.1/32"}}}}});
        config["routing"]!["rules"]!.AsArray().Insert(0,new JsonObject{["type"]="field",["inboundTag"]=new JsonArray{"fixture-vless"},["outboundTag"]="fixture-direct"});
        var core=new XrayProcessManager(root);
        try{
            Check(await core.StartAsync(config.ToJsonString(),goodPort,badPort,timeout.Token),"real candidate fixture starts");
            var endpoint=new[]{ $"http://127.0.0.1:{targetPort}/generate_204" };
            Check(await PingService.MeasureProxyLatencyToAsync(goodPort,endpoint,2000,timeout.Token)>0,"working VLESS candidate carries real HTTP 204");
            var stalled = new TcpListener(IPAddress.Loopback,0);stalled.Start();
            try
            {
                var stalledPort=((IPEndPoint)stalled.LocalEndpoint).Port;
                var clock=System.Diagnostics.Stopwatch.StartNew();
                Check(await PingService.MeasureProxyLatencyToAsync(goodPort,[$"http://127.0.0.1:{stalledPort}/stall",endpoint[0]],1500,timeout.Token)>0,"second endpoint can confirm a candidate while first endpoint stalls");
                Check(clock.ElapsedMilliseconds<1200,"successful endpoint does not wait for stalled endpoint timeout");
            }
            finally { stalled.Stop(); }
            Check(await PingService.MeasureTcpLatencyAsync("127.0.0.1",vlessPort)>0,"bad-credentials endpoint has a reachable TCP port");
            Check(await PingService.MeasureProxyLatencyToAsync(badPort,endpoint,1200,timeout.Token)<=0,"open TCP port with wrong VLESS credentials is not a working candidate");
            status=200;
            Check(await PingService.MeasureProxyLatencyToAsync(goodPort,endpoint,2000,timeout.Token)<=0,"HTTP denial/captive page is not connectivity success");
        }finally{core.Stop();timeout.Cancel();target.Stop();await responder;await Task.WhenAll(handlers);}
        Console.WriteLine("PASS balancer probe isolation: physical binding and separate DNS; real VLESS success vs reachable TCP/wrong credentials; HTTP denial and cancellation");
    }
    static int Port(){var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();return port;}
}
