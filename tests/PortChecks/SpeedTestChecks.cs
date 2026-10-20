using System.Net;
using SplateClientDesktop.Services;

internal static class SpeedTestChecks
{
    public static async Task RunAsync()
    {
        var handler=new FixtureHandler();var samples=new List<SpeedTestSample>();
        var result=await new SpeedTestService(_=>handler).RunAsync(10809,s=>{lock(samples)samples.Add(s);},CancellationToken.None);
        Check(result.Done&&result.Bytes==4*1024*1024&&result.Seconds>0&&result.Mbps>0,"two measured downloads exclude warmup bytes");
        Check(handler.Peak>=2&&handler.Active==0,"two concurrent streams, all disposed");
        Check(result.HttpsPing==null&&result.Message.Contains("недоступен"),"unavailable HTTPS response is not zero ping");
        var bad=new FixtureHandler {Html=true};
        try{await new SpeedTestService(_=>bad).RunAsync(10809,_=>{},CancellationToken.None);throw new Exception("HTML test page accepted");}catch(HttpRequestException){}
        Check(bad.Active==0,"rejected response streams disposed");
        var fallback = new FixtureHandler { RejectCloudflare = true };
        var fallbackResult = await new SpeedTestService(_ => fallback).RunAsync(10809, _ => {}, CancellationToken.None);
        Check(fallbackResult.Done && fallback.CacheflyRequests == 4, "403 falls back to uncached CacheFly warmup and measurement");
        var cancelHandler=new FixtureHandler();using var stop=new CancellationTokenSource(40);
        try{await new SpeedTestService(_=>cancelHandler).RunAsync(10809,_=>{},stop.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
        Check(cancelHandler.Active==0,"cancel closes warmup and download streams");
        Console.WriteLine("PASS speedtest: two streams, warmup exclusion, units/time, response validation, unavailable HTTPS ping and cancellation");
    }
    private static void Check(bool condition,string label){if(!condition)throw new Exception(label);}
    private sealed class FixtureHandler:HttpMessageHandler
    {
        public int Active,Peak,CacheflyRequests;public bool Html,RejectCloudflare;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri!.AbsolutePath.Contains("generate_204"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            Check(request.RequestUri.Query.Contains("sample="), "unique request avoids stale test response");
            Check(request.Headers.UserAgent.Count > 0 && request.Headers.CacheControl?.NoCache == true && request.Headers.AcceptEncoding.Any(v=>v.Value=="identity"), "uncached uncompressed download headers");
            if (RejectCloudflare && request.RequestUri.Host == "speed.cloudflare.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            if (request.RequestUri.Host == "cachefly.cachefly.net") Interlocked.Increment(ref CacheflyRequests);
            var active=Interlocked.Increment(ref Active);Peak=Math.Max(Peak,active);
            var stream=new FixtureStream(()=>Interlocked.Decrement(ref Active));
            var content=new StreamContent(stream);
            content.Headers.ContentType=new(Html?"text/html":"application/octet-stream");content.Headers.ContentLength=stream.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=content});
        }
    }
    private sealed class FixtureStream(Action closed):MemoryStream(new byte[2*1024*1024],false)
    {
        private int _disposed;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        {await Task.Delay(5,ct);return await base.ReadAsync(buffer,ct);}
        protected override void Dispose(bool disposing){if(Interlocked.Exchange(ref _disposed,1)==0)closed();base.Dispose(disposing);}
    }
}
