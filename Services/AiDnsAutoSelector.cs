using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace SplateClientDesktop.Services;

public enum AiDnsOutcome { Accessible, Limited, GeoBlocked, Failed }
public record AiDnsChoice(string ProviderKey, bool TlsFragment);
public record AiDnsTargetResult(string Service, AiDnsOutcome Outcome, long ElapsedMs = 0, string? Detail = null);
public record AiDnsCandidateResult(AiDnsChoice Choice, AiDnsTargetResult[] Targets)
{
    public int Accessible => Targets.Count(t => t.Outcome == AiDnsOutcome.Accessible);
    public int Limited => Targets.Count(t => t.Outcome == AiDnsOutcome.Limited);
    public int GeoBlocked => Targets.Count(t => t.Outcome == AiDnsOutcome.GeoBlocked);
    public long MedianMs => Targets.Where(t => t.Outcome != AiDnsOutcome.Failed).Select(t => t.ElapsedMs).Order().Skip(Targets.Count(t => t.Outcome != AiDnsOutcome.Failed) / 2).FirstOrDefault(long.MaxValue);
}
public record AiDnsSelectionState(string Phase, int Completed, AiDnsChoice? Current, AiDnsCandidateResult[] Results, AiDnsCandidateResult? Winner = null, string? Error = null)
{
    public int Total => SmartDnsProviders.All.Length * 2;
}
public sealed class AiDnsAutoSelector
{
    public static AiDnsOutcome Classify(int code, string body, string? location = null, bool challenge = false)
    {
        var visible = System.Text.RegularExpressions.Regex.Replace(body, @"<(script|style)\b[^>]*>.*?</\1\s*>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        var content = NormalizePageText(visible + "\n" + location);
        if (new[] { "location=unsupported", "unsupported_country", "country_not_supported", "region_not_supported", "not available in your country", "isn't available in your country", "isn't currently supported in your country", "is not currently supported in your country", "not supported in your country", "not available in your region", "недоступен в вашей стране", "недоступно в вашей стране", "не поддерживается в вашей стране", "не поддерживается в вашем регионе" }.Any(content.Contains)) return AiDnsOutcome.GeoBlocked;
        if (challenge || code is 401 or 403 or 429 || new[] { "just a moment", "attention required!", "our systems have detected unusual traffic", "/sorry/index" }.Any(content.Contains)) return AiDnsOutcome.Limited;
        if (code is >= 300 and < 400) return AiDnsOutcome.Limited;
        return code is >= 200 and < 300 && !string.IsNullOrWhiteSpace(body) ? AiDnsOutcome.Accessible : AiDnsOutcome.Failed;
    }
    public static string NormalizePageText(string value) => System.Text.RegularExpressions.Regex.Replace(
        WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(value, "<[^>]+>", " ")).Replace('’', '\'').Replace('‘', '\'').Replace('ʼ', '\'').Replace("\\u2019", "'").Replace("\\u0027", "'"), @"\s+", " ").ToLowerInvariant();

    // Use rendered DOM text for this check. Searching script bundles for error
    // translations would wrongly reject a healthy page containing those strings.
    public static AiDnsOutcome ClassifyRendered(int code, string visibleText, string? location = null)
    {
        var outcome = Classify(code, visibleText, location);
        if (outcome != AiDnsOutcome.Accessible) return outcome;
        var text = NormalizePageText(visibleText);
        if (new[] { "something went wrong", "что-то пошло не так", "что то пошло не так", "can't access this service", "cannot access this service", "не удалось получить доступ", "verify you are human", "подтвердите, что вы человек", "captcha", "sign in to continue" }.Any(text.Contains)) return AiDnsOutcome.Limited;
        return outcome;
    }
    public static IOrderedEnumerable<AiDnsCandidateResult> Rank(IEnumerable<AiDnsCandidateResult> results) => results.OrderByDescending(r => r.Accessible).ThenBy(r => r.GeoBlocked).ThenByDescending(r => r.Limited).ThenBy(r => r.Choice.TlsFragment).ThenBy(r => r.MedianMs);
    public static async Task<AiDnsCandidateResult?> ConfirmAsync(IEnumerable<AiDnsCandidateResult> results, Func<AiDnsChoice, Task<AiDnsCandidateResult>> verify, string? requiredService = null)
    {
        foreach (var candidate in Rank(results.Where(r => r.Accessible > 0 && (requiredService == null || r.Targets.Any(t => t.Service == requiredService && t.Outcome == AiDnsOutcome.Accessible)))).Take(3))
        {
            var confirmed = await verify(candidate.Choice);
            if (candidate.Targets.Where(t => t.Outcome == AiDnsOutcome.Accessible).All(t => confirmed.Targets.Any(v => v.Service == t.Service && v.Outcome == AiDnsOutcome.Accessible)) && confirmed.GeoBlocked <= candidate.GeoBlocked) return confirmed;
        }
        return null;
    }
    private static string Fingerprint() => string.Join(';', NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && !n.Name.Contains("Splate", StringComparison.OrdinalIgnoreCase)).Select(n => n.Id + string.Join(',', n.GetIPProperties().UnicastAddresses.Select(a => a.Address.ToString()))).Order());
    public async Task<AiDnsCandidateResult?> RunAsync(Action<AiDnsSelectionState> progress, Func<AiDnsChoice, Task<bool>> apply, CancellationToken token, Func<AiDnsChoice, CancellationToken, Task<AiDnsCandidateResult>>? probe = null, Func<AiDnsChoice, CancellationToken, Task<AiDnsCandidateResult>>? confirmationProbe = null)
    {
        var results = new List<AiDnsCandidateResult>(); var fingerprint = Fingerprint();
        void CheckNetwork() { token.ThrowIfCancellationRequested(); if (fingerprint != Fingerprint()) throw new IOException("Сеть изменилась. Настройки сохранены без изменений."); }
        probe ??= (choice, ct) => ProbeAsync(choice, ct);
        try
        {
            foreach (var choice in SmartDnsProviders.All.SelectMany(p => new[] { new AiDnsChoice(p.Key, false), new AiDnsChoice(p.Key, true) }))
            {
                CheckNetwork(); progress(new("testing", results.Count, choice, results.ToArray()));
                results.Add(await probe(choice, token)); progress(new("testing", results.Count, choice, results.ToArray()));
            }
            var winner = await ConfirmAsync(results.ToArray(), async choice => {
                CheckNetwork(); progress(new("verifying", results.Count, choice, results.ToArray()));
                var verified = await (confirmationProbe ?? probe)(choice, token);
                var index = results.FindIndex(r => r.Choice == choice);
                if (index >= 0) results[index] = verified;
                progress(new("verifying", results.Count, choice, results.ToArray()));
                return verified;
            }, "Gemini");
            CheckNetwork();
            if (winner == null) progress(new("no_match", results.Count, null, results.ToArray()));
            else { var applied = await apply(winner.Choice); progress(new(applied ? "applied" : "cancelled", results.Count, null, results.ToArray(), applied ? winner : null)); }
            return winner;
        }
        catch (OperationCanceledException) { progress(new("cancelled", results.Count, null, results.ToArray())); throw; }
        catch (IOException e) { progress(new("error", results.Count, null, results.ToArray(), Error: e.Message)); return null; }
    }
    public async Task<AiDnsCandidateResult> ProbeAsync(AiDnsChoice choice, CancellationToken token, Func<int, CancellationToken, Task<AiDnsTargetResult>>? renderGemini = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var path = Path.Combine(Path.GetTempPath(), "splate-ai-probe-" + Guid.NewGuid().ToString("N") + ".json"); Process? child = null;
        using var job = new ChildProcessJob();
        try
        {
            var uplink = await TunDeviceManager.GetUplinkAsync(token);
            await File.WriteAllTextAsync(path, SmartDnsProviders.ProbeConfig(choice.ProviderKey, choice.TlsFragment, port, uplink.Name), token);
            var core = Path.Combine(AppContext.BaseDirectory, "core", "xray.exe");
            var start = new ProcessStartInfo(core) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(core), RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "run", "-c", path }) start.ArgumentList.Add(arg);
            child = Process.Start(start)!; job.Attach(child);
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(token); readyTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (true)
            {
                readyTimeout.Token.ThrowIfCancellationRequested(); if (child.HasExited) throw new IOException("Временное ядро проверки не запустилось.");
                try { using var socket = new TcpClient(); await socket.ConnectAsync(IPAddress.Loopback, port, readyTimeout.Token); break; } catch (SocketException) { await Task.Delay(50, readyTimeout.Token); }
            }
            var targets = await AiServiceProbe.CheckAsync(port, token, SmartDnsProviders.ForAi(choice.ProviderKey));
            if (renderGemini != null && targets[0].Outcome == AiDnsOutcome.Accessible)
            {
                var rendered = await renderGemini(port, token);
                targets[0] = rendered with { Detail = targets[0].Detail + "; " + rendered.Detail };
            }
            return new(choice, targets);

        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or System.ComponentModel.Win32Exception) { return new(choice, new[] { "Gemini", "ChatGPT", "Claude" }.Select(n => new AiDnsTargetResult(n, AiDnsOutcome.Failed, Detail: "Не запущена проверка: " + e.Message)).ToArray()); }
        finally
        {
            if (child != null) { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } child.Dispose(); }
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
