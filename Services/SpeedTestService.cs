using System.Diagnostics;
using System.Net;
using System.Net.Http;

namespace SplateClientDesktop.Services;

public record SpeedTestSample(string Message, double Mbps, long Bytes, double Seconds, long? HttpsPing, bool Done);

public sealed class SpeedTestService(Func<int, HttpMessageHandler>? handlerFactory = null, Action<string>? diagnostics = null)
{
    public async Task<SpeedTestSample> RunAsync(int port, Action<SpeedTestSample> progress, CancellationToken token)
    {
        using var handler = handlerFactory?.Invoke(port) ?? new SocketsHttpHandler { UseProxy = true, Proxy = new WebProxy($"http://127.0.0.1:{port}"), ConnectTimeout = TimeSpan.FromSeconds(8), MaxConnectionsPerServer = 4, AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SplateVPN-Speedtest/1.0");
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("identity");
        client.DefaultRequestHeaders.CacheControl = new() { NoCache = true, NoStore = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(40));
        var ct = timeout.Token;
        long? ping = null;
        try
        {
            using var pingTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct); pingTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            var timer = Stopwatch.StartNew();
            using var response = await client.GetAsync("https://cp.cloudflare.com/generate_204", HttpCompletionOption.ResponseHeadersRead, pingTimeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NoContent) ping = Math.Max(1, timer.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }
        progress(new("Прогрев двух потоков…", 0, 0, 0, ping, false));
        long total = 0;
        var urls = new[] { "https://speed.cloudflare.com/__down?bytes=15000000", "https://cachefly.cachefly.net/10mb.test" };
        string? selected = null;
        foreach (var url in urls)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                progress(new($"Прогрев: {new Uri(url).Host}…", 0, 0, 0, ping, false));
                await Task.WhenAll(Warmup(), Warmup()).ConfigureAwait(false); selected = url; break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { diagnostics?.Invoke($"{new Uri(url).Host}: прогрев не завершился вовремя"); }
            catch (HttpRequestException error) { diagnostics?.Invoke($"{new Uri(url).Host}: {error}"); }
            catch (System.IO.InvalidDataException error) { diagnostics?.Invoke($"{new Uri(url).Host}: {error}"); }
            catch (System.IO.IOException error) { diagnostics?.Invoke($"{new Uri(url).Host}: {error}"); }
            async Task Warmup()
            {
                try { await Download(url, true, attempt.Token).ConfigureAwait(false); }
                catch { attempt.Cancel(); throw; }
            }
        }
        if (selected == null) throw new HttpRequestException("Тестовые серверы недоступны. VPN остаётся включённым.");
        var clock = Stopwatch.StartNew();
        using var reportCancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reporter = ReportAsync();
        try { await Task.WhenAll(MeasureAsync(), MeasureAsync()).ConfigureAwait(false); }
        catch { timeout.Cancel(); throw; }
        finally { reportCancel.Cancel(); await reporter; }
        clock.Stop();
        if (total == 0) throw new System.IO.InvalidDataException("Тестовый сервер не передал данные.");
        var result = Sample(true); progress(result); return result;

        async Task MeasureAsync()
        {
            try { await Download(selected, false, ct).ConfigureAwait(false); }
            catch { timeout.Cancel(); throw; }
        }

        SpeedTestSample Sample(bool done)
        {
            var bytes = Interlocked.Read(ref total); var seconds = Math.Max(.001, clock.Elapsed.TotalSeconds); var mbps = bytes * 8 / seconds / 1000000;
            return new($"{mbps:F1} Мбит/с · {mbps / 8:F2} МБ/с · {bytes / 1000000.0:F1} МБ · {seconds:F1} сек\nОтклик HTTPS: {(ping.HasValue ? ping + " мс" : "недоступен")}", mbps, bytes, seconds, ping, done);
        }
        async Task ReportAsync()
        {
            try { using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250)); while (await timer.WaitForNextTickAsync(reportCancel.Token)) progress(Sample(false)); }
            catch (OperationCanceledException) { }
        }
        async Task Download(string url, bool warmup, CancellationToken downloadToken)
        {
            var requestUrl = url + (url.Contains('?') ? "&" : "?") + "sample=" + Guid.NewGuid().ToString("N");
            using var response = await client.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, downloadToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (response.StatusCode != HttpStatusCode.OK || type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || type.Contains("json", StringComparison.OrdinalIgnoreCase)) throw new System.IO.InvalidDataException($"Ответ тестового сервера не содержит файл (HTTP {(int)response.StatusCode}, {type}).");
            await using var stream = await response.Content.ReadAsStreamAsync(downloadToken).ConfigureAwait(false);
            var buffer = new byte[65536]; long readTotal = 0;
            var maximum = warmup ? 1000000 : 15000000;
            while (readTotal < maximum)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximum - readTotal)), downloadToken).ConfigureAwait(false);
                if (read == 0) break; readTotal += read;
                if (!warmup) Interlocked.Add(ref total, read);
            }
            if (readTotal == 0 || (!warmup && response.Content.Headers.ContentLength is long expected && readTotal < Math.Min(expected, maximum))) throw new System.IO.InvalidDataException("Тестовая загрузка оборвалась.");
        }
    }
}
