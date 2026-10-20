using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SplateClientDesktop.Services;

public record IpDetails(
    string Ip,
    string Country,
    string CountryCode,
    string City,
    string FlagEmoji,
    bool IsSuccess
);

public static class IpLocationService
{
    private static readonly string[] HealthCheckEndpoints =
    [
        "http://cp.cloudflare.com/generate_204",
        "http://connectivitycheck.gstatic.com/generate_204",
        "http://www.msftconnecttest.com/connecttest.txt"
    ];

    public static async Task<(bool Success, long RttMs)> CheckConnectivityAsync(int socksPort = 10808, int timeoutMs = 4000, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        foreach (var endpoint in HealthCheckEndpoints)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var handler = new SocketsHttpHandler
                {
                    ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMs)
                };

                if (socksPort > 0 && socksPort <= 65535)
                {
                    handler.Proxy = new WebProxy($"socks5://127.0.0.1:{socksPort}");
                    handler.UseProxy = true;
                }
                else
                {
                    handler.UseProxy = false;
                }

                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromMilliseconds(timeoutMs)
                };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);
                sw.Restart();
                using var response = await client.GetAsync(endpoint, cts.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode || (int)response.StatusCode == 204)
                {
                    sw.Stop();
                    return (true, sw.ElapsedMilliseconds);
                }
            }
            catch
            {
                // Try next endpoint
            }
        }
        return (false, -1);
    }

    public static async Task<IpDetails> FetchEgressIpAsync(int socksPort = 10808, int timeoutMs = 5000)
    {
        // 1. Try ipwho.is
        try
        {
            var res = await QueryJsonAsync("https://ipwho.is/", socksPort, timeoutMs);
            if (!string.IsNullOrEmpty(res))
            {
                using var doc = JsonDocument.Parse(res);
                var root = doc.RootElement;
                if (root.TryGetProperty("success", out var s) && s.GetBoolean())
                {
                    var ip = root.GetProperty("ip").GetString() ?? "";
                    var country = root.GetProperty("country").GetString() ?? "";
                    var countryCode = root.GetProperty("country_code").GetString() ?? "";
                    var city = root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";
                    
                    var flag = "";
                    if (root.TryGetProperty("flag", out var f) && f.TryGetProperty("emoji", out var emoji))
                    {
                        flag = emoji.GetString() ?? "";
                    }
                    if (string.IsNullOrEmpty(flag))
                    {
                        flag = CountryCodeToEmoji(countryCode);
                    }

                    if (IPAddress.TryParse(ip, out _)) return new IpDetails(ip, country, countryCode, city, flag, true);
                }
            }
        }
        catch { }

        // 2. Try api.ip.sb
        try
        {
            var res = await QueryJsonAsync("https://api.ip.sb/geoip", socksPort, timeoutMs);
            if (!string.IsNullOrEmpty(res))
            {
                using var doc = JsonDocument.Parse(res);
                var root = doc.RootElement;
                var ip = root.TryGetProperty("ip", out var i) ? i.GetString() ?? "" : "";
                var country = root.TryGetProperty("country", out var cnt) ? cnt.GetString() ?? "" : "";
                var countryCode = root.TryGetProperty("country_code", out var cc) ? cc.GetString() ?? "" : "";
                var city = root.TryGetProperty("city", out var ct) ? ct.GetString() ?? "" : "";

                if (IPAddress.TryParse(ip, out _))
                {
                    return new IpDetails(ip, country, countryCode, city, CountryCodeToEmoji(countryCode), true);
                }
            }
        }
        catch { }

        // 3. Try ipinfo.io
        try
        {
            var res = await QueryJsonAsync("https://ipinfo.io/json", socksPort, timeoutMs);
            if (!string.IsNullOrEmpty(res))
            {
                using var doc = JsonDocument.Parse(res);
                var root = doc.RootElement;
                var ip = root.TryGetProperty("ip", out var i) ? i.GetString() ?? "" : "";
                var country = root.TryGetProperty("country", out var cnt) ? cnt.GetString() ?? "" : "";
                var city = root.TryGetProperty("city", out var ct) ? ct.GetString() ?? "" : "";

                if (IPAddress.TryParse(ip, out _))
                {
                    return new IpDetails(ip, country, country, city, CountryCodeToEmoji(country), true);
                }
            }
        }
        catch { }

        return new IpDetails("Не определен", "Неизвестно", "", "", "🌐", false);
    }

    private static async Task<string?> QueryJsonAsync(string url, int socksPort, int timeoutMs)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMs)
        };

        if (socksPort > 0 && socksPort <= 65535)
        {
            handler.Proxy = new WebProxy($"socks5://127.0.0.1:{socksPort}");
            handler.UseProxy = true;
        }
        else
        {
            handler.UseProxy = false;
        }

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMs)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        using var cts = new CancellationTokenSource(timeoutMs);
        using var response = await client.GetAsync(url, cts.Token).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        return null;
    }

    public static string CountryCodeToEmoji(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2) return "🌐";
        countryCode = countryCode.ToUpperInvariant();
        try
        {
            int firstChar = char.ConvertToUtf32(countryCode, 0) - 0x41 + 0x1F1E6;
            int secondChar = char.ConvertToUtf32(countryCode, 1) - 0x41 + 0x1F1E6;
            return char.ConvertFromUtf32(firstChar) + char.ConvertFromUtf32(secondChar);
        }
        catch
        {
            return "🌐";
        }
    }
}
