using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SplateClientDesktop.Services;

public static class PingService
{
    private static readonly string TestUrl = "http://cp.cloudflare.com/generate_204";

    /// <summary>
    /// Measures TCP handshake latency to the server host and port.
    /// </summary>
    public static async Task<long> MeasureTcpLatencyAsync(string host, int port, int timeoutMs = 2500)
    {
        if (string.IsNullOrWhiteSpace(host) || port <= 0) return -1;
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Measures real end-to-end HTTP latency through the running local proxy (HTTP port).
    /// </summary>
    public static async Task<long> MeasureHttpProxyLatencyAsync(int proxyPort = 10809, int timeoutMs = 3500)
    {
        try
        {
            var handler = new HttpClientHandler
            {
                Proxy = new System.Net.WebProxy($"http://127.0.0.1:{proxyPort}"),
                UseProxy = true
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(timeoutMs)
            };

            var sw = Stopwatch.StartNew();
            using var response = await client.GetAsync(TestUrl, HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();

            if (response.IsSuccessStatusCode || (int)response.StatusCode == 204)
            {
                return sw.ElapsedMilliseconds;
            }
        }
        catch
        {
            // Proxy not running or request failed
        }
        return -1;
    }
}