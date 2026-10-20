using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace SplateClientDesktop.Services;

/// <summary>Uses only the supplied SOCKS route; never retries on the system connection.</summary>
public static class AiServiceProbe
{
    public static async Task<AiDnsTargetResult[]> CheckAsync(int socksPort, CancellationToken token = default, SmartDnsProvider? dns = null)
    {
        using var handler = new SocketsHttpHandler { UseProxy = true, Proxy = new WebProxy($"socks5://127.0.0.1:{socksPort}"), AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(4), AutomaticDecompression = DecompressionMethods.All };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 65535, DefaultRequestVersion = HttpVersion.Version20, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 SplateVPN/AiCheck");
        client.DefaultRequestHeaders.CacheControl = new() { NoCache = true, NoStore = true };
        return await Task.WhenAll(Request("Gemini", "gemini.google.com"), Request("ChatGPT", "chatgpt.com"), Request("Claude", "claude.ai"));

        async Task<AiDnsTargetResult> Request(string service, string host)
        {
            var clock = Stopwatch.StartNew(); var stage = dns == null ? "Соединение / TLS" : "DNS";
            try
            {
                string? dnsDetail = null;
                if (dns != null)
                {
                    var query = DnsQuery(host);
                    using var content = new ByteArrayContent(query);
                    content.Headers.ContentType = new("application/dns-message");
                    using var request = new HttpRequestMessage(HttpMethod.Post, dns.DohUrl) { Content = content, Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
                    request.Headers.Accept.ParseAdd("application/dns-message");
                    using var reply = await client.SendAsync(request, token);
                    if (!reply.IsSuccessStatusCode) throw new IOException($"DoH HTTP {(int)reply.StatusCode}");
                    if (reply.Content.Headers.ContentType?.MediaType != "application/dns-message") throw new IOException("DoH вернул не DNS-пакет");
                    var addresses = ValidateDnsAnswer(await reply.Content.ReadAsByteArrayAsync(token), query);
                    dnsDetail = "DNS: " + string.Join(", ", addresses);
                }
                stage = "Соединение / TLS";
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await client.GetAsync($"https://{host}/", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                stage = "Ответ сайта";
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var body = new MemoryStream(); var buffer = new byte[8192];
                while (body.Length < 512 * 1024)
                {
                    var count = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, 512 * 1024 - body.Length)), timeout.Token);
                    if (count == 0) break;
                    body.Write(buffer, 0, count);
                }
                var outcome = AiDnsAutoSelector.Classify((int)response.StatusCode, Encoding.UTF8.GetString(body.ToArray()), response.Headers.Location?.ToString(), response.Headers.TryGetValues("cf-mitigated", out var values) && values.Contains("challenge"));
                return new(service, outcome, clock.ElapsedMilliseconds, (dnsDetail == null ? "" : dnsDetail + "; ") + $"HTTP {(int)response.StatusCode}");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
            {
                return new(service, AiDnsOutcome.Failed, clock.ElapsedMilliseconds, stage + ": " + (e is OperationCanceledException ? "время ожидания истекло" : e.Message));
            }
        }
    }

    internal static byte[] DnsQuery(string host)
    {
        var bytes = new List<byte>(new byte[] { (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
        foreach (var label in host.Split('.')) { bytes.Add((byte)label.Length); bytes.AddRange(Encoding.ASCII.GetBytes(label)); }
        bytes.AddRange(new byte[] { 0, 0, 1, 0, 1 });
        return bytes.ToArray();
    }

    internal static string[] ValidateDnsAnswer(byte[] response, byte[] query)
    {
        if (response.Length < query.Length || response.Length > 65535 || response[0] != query[0] || response[1] != query[1] ||
            (response[2] & 0xfa) != 0x80 || (response[3] & 15) != 0 || response[4] != 0 || response[5] != 1 ||
            !response.AsSpan(12, query.Length - 12).SequenceEqual(query.AsSpan(12)))
            throw new IOException("Некорректный ответ DNS, NXDOMAIN или отказ провайдера");
        var answers = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(6, 2)); var offset = query.Length;
        var ips = new List<string>();
        for (var i = 0; i < answers; i++)
        {
            while (true)
            {
                if (offset >= response.Length) throw new IOException("Обрезанный ответ DNS");
                var length = response[offset++]; if (length == 0) break;
                if ((length & 0xc0) == 0xc0) { if (offset >= response.Length || (((length & 63) << 8) | response[offset]) >= response.Length) throw new IOException("Некорректное имя DNS"); offset++; break; }
                if (length > 63 || offset + length >= response.Length) throw new IOException("Некорректное имя DNS"); offset += length;
            }
            if (offset + 10 > response.Length) throw new IOException("Обрезанная запись DNS");
            var type = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset, 2));
            var klass = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset + 2, 2));
            var size = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset + 8, 2)); offset += 10;
            if (offset + size > response.Length) throw new IOException("Обрезанный адрес DNS");
            if (type == 1 && klass == 1 && size == 4)
            {
                var ip = response.AsSpan(offset, 4).ToArray();
                if (ip[0] is 0 or 10 or 127 || ip[0] >= 224 || ip[0] == 169 && ip[1] == 254 || ip[0] == 172 && ip[1] is >= 16 and <= 31 || ip[0] == 192 && ip[1] == 168 || ip[0] == 100 && ip[1] is >= 64 and <= 127)
                    throw new IOException("DNS вернул локальный / блокирующий адрес");
                ips.Add(new IPAddress(ip).ToString());
            }
            offset += size;
        }
        if (ips.Count == 0) throw new IOException("DNS не вернул IPv4-адрес сервиса");
        return ips.Distinct().ToArray();
    }
}
