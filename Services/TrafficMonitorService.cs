using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace SplateClientDesktop.Services;

public class TrafficMonitorService : ITrafficMonitorService, IDisposable
{
    private CancellationTokenSource? _cts;
    private readonly TrafficAccumulator _totals = new();
    private readonly object _gate = new();
    private int _generation;
    public event Action<string, string>? SpeedUpdated;
    public event Action<long, long>? BytesPerSecUpdated;
    public event Action<long, long>? SessionBytesUpdated;

    public void Start(int metricsPort)
    {
        Begin(metricsPort, false);
    }
    public void Resume(int metricsPort) => Begin(metricsPort, true);
    private void Begin(int port, bool preserve)
    {
        lock (_gate) { Stop(); _totals.NewCore(preserve); _cts = new(); _ = MonitorAsync(port, _generation, _cts.Token); }
    }

    private async Task MonitorAsync(int port, int generation, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(1) };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var clock = Stopwatch.StartNew();
        double previousTime = 0;
        try
        {
            do
            {
                try
                {
                    var json = await client.GetStringAsync($"http://127.0.0.1:{port}/debug/vars", ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    var outbounds = doc.RootElement.GetProperty("stats").GetProperty("outbound");
                    long down = 0, up = 0;
                    foreach (var tag in new[] { "proxy", "ai-smartdns" })
                        if (outbounds.TryGetProperty(tag, out var stats))
                        {
                            down += stats.GetProperty("downlink").GetInt64();
                            up += stats.GetProperty("uplink").GetInt64();
                        }
                    var now = clock.Elapsed.TotalSeconds;
                    lock (_gate)
                    {
                        if (ct.IsCancellationRequested || generation != _generation) return;
                        var sample = _totals.Observe(down, up, now - previousTime); previousTime = now;
                        SpeedUpdated?.Invoke(FormatSpeed(sample.DownBps), FormatSpeed(sample.UpBps));
                        BytesPerSecUpdated?.Invoke(sample.DownBps, sample.UpBps);
                        SessionBytesUpdated?.Invoke(sample.Download, sample.Upload);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { Debug.WriteLine("Xray metrics: " + ex.Message); }
            } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    public void Stop() { lock (_gate) { _generation++; _cts?.Cancel(); _cts?.Dispose(); _cts = null; } }
    public void Dispose() => Stop();
    private static string FormatSpeed(long n) => n < 1024 ? $"{n} B/s" : n < 1048576 ? $"{n / 1024.0:F1} KB/s" : $"{n / 1048576.0:F1} MB/s";
}
