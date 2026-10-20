using System.Net;
using SplateClientDesktop.Services;

internal static class UpdateChecks
{
    public static async Task RunAsync()
    {
        const string release = "https://github.com/splatedk-web/splatevpn-xray-client-windows/releases/tag/2.11.4";
        async Task<AppUpdateInfo> Check(string current,string tag="2.11.4",string url=release,HttpStatusCode status=HttpStatusCode.OK,bool prerelease=false)
        {
            using var client=new HttpClient(new Handler(_=>new HttpResponseMessage(status) {Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(new {tag_name=tag,html_url=url,draft=false,prerelease}))}));
            return await UpdateCheckerService.CheckForUpdatesAsync(current,client);
        }
        var newer=await Check("2.10.0");
        Assert(newer.CheckSucceeded&&newer.IsUpdateAvailable&&newer.ReleaseUrl==release,"2.10 detects 2.11.4");
        Assert(!(await Check("2.11.4.0","v2.11.4")).IsUpdateAvailable,"normalized equal version has no update");
        Assert(!(await Check("2.11.5")).IsUpdateAvailable,"local newer build has no downgrade");
        Assert((await Check("2.9.9","2.11.4")).IsUpdateAvailable,"numeric rather than string ordering");
        Assert(!(await Check("2.10.0","release-latest")).CheckSucceeded,"malformed tags not treated as current");
        Assert(!(await Check("2.10.0",prerelease:true)).CheckSucceeded,"prerelease excluded");
        Assert(!(await Check("2.10.0",url:"https://github.com.attacker.example/releases/tag/2.11.4")).CheckSucceeded,"release links constrained to GitHub repository");
        var limited=await Check("2.10.0",status:HttpStatusCode.Forbidden);
        Assert(!limited.CheckSucceeded&&limited.ErrorMessage.Contains("GitHub"),"rate limit distinct from no update");
        using var offline=new HttpClient(new Handler(_=>throw new HttpRequestException("offline")));
        var failure=await UpdateCheckerService.CheckForUpdatesAsync("2.10.0",offline);
        Assert(!failure.CheckSucceeded&&failure.ErrorMessage.Length>0,"network error is visible");
        using var malformed=new HttpClient(new Handler(_=>new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent("not json")}));
        Assert(!(await UpdateCheckerService.CheckForUpdatesAsync("2.10.0",malformed)).CheckSucceeded,"invalid JSON distinct from up to date");
        Assert(Version.TryParse(UpdateCheckerService.CurrentVersion,out _),"current version comes from built assembly");
        Console.WriteLine("PASS updates: old/equal/new versions, tag normalization, stable releases, GitHub URL, rate limit, network and invalid responses");
    }
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Assert(request.RequestUri!.AbsoluteUri==UpdateCheckerService.LatestReleaseApi,"correct release API");
            Assert(request.Headers.UserAgent.Count>0,"GitHub User-Agent present");
            return Task.FromResult(response(request));
        }
    }
}
