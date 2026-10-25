using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public static class DnsDiagnosticService
{
    public static string Endpoint(AppSettings settings) => SmartDnsProviders.Find(settings.DnsProvider)?.DohUrl ?? settings.DnsProvider switch
    {
        "google" => "https://dns.google/dns-query", "quad9" => "https://dns.quad9.net/dns-query",
        "adguard" => "https://dns.adguard-dns.com/dns-query",
        "custom" => XrayConfigGenerator.ValidateDoh(settings.CustomDohUrl), _ => "https://cloudflare-dns.com/dns-query"
    };
    public static async Task<string> RunAsync(AppSettings settings, string input, int? proxyPort, CancellationToken ct)
    {
        var domain = SiteRules.Domain(input);
        var id = (ushort)System.Security.Cryptography.RandomNumberGenerator.GetInt32(65536);
        using var query = new MemoryStream();
        query.Write(new byte[] { (byte)(id >> 8), (byte)id, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
        foreach (var label in domain.Split('.')) { var bytes = Encoding.ASCII.GetBytes(label); query.WriteByte((byte)bytes.Length); query.Write(bytes); }
        query.Write(new byte[] { 0, 0, 1, 0, 1 });
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(8));
        if (settings.DnsProvider == "yandex")
        {
            var watch = Stopwatch.StartNew();
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(proxyPort.HasValue ? "127.0.0.1" : "77.88.8.8", proxyPort ?? 53, budget.Token);
            await using var connection = tcp.GetStream();
            if (proxyPort.HasValue)
            {
                await connection.WriteAsync(Encoding.ASCII.GetBytes("CONNECT 77.88.8.8:53 HTTP/1.1\r\nHost: 77.88.8.8:53\r\n\r\n"), budget.Token);
                var header = new List<byte>(); var one = new byte[1];
                while (header.Count < 16384)
                {
                    await connection.ReadExactlyAsync(one, budget.Token); header.Add(one[0]);
                    if (header.Count >= 4 && header.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
                }
                var status = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n")[0].Split(' ');
                if (header.Count >= 16384 || status.Length < 2 || status[1] != "200") throw new IOException("VPN-прокси не открыл TCP-соединение с выбранным DNS.");
            }
            var wire = query.ToArray(); await connection.WriteAsync(new byte[] { (byte)(wire.Length >> 8), (byte)wire.Length }, budget.Token); await connection.WriteAsync(wire, budget.Token);
            var size = new byte[2]; await connection.ReadExactlyAsync(size, budget.Token); var reply = new byte[BinaryPrimitives.ReadUInt16BigEndian(size)];
            await connection.ReadExactlyAsync(reply, budget.Token); var records = ParseResponse(reply, id);
            return $"{domain}\nDNS: Yandex · TCP 77.88.8.8:53\nМаршрут проверки: {(proxyPort.HasValue ? "через подключённый VPN" : "напрямую")}\nЗадержка: {watch.ElapsedMilliseconds} мс\nОтвет A: {string.Join(", ", records)}";
        }
        using var handler = new HttpClientHandler { UseProxy = proxyPort.HasValue, Proxy = proxyPort.HasValue ? new WebProxy($"http://127.0.0.1:{proxyPort}") : null, AllowAutoRedirect = false };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(settings)) { Content = new ByteArrayContent(query.ToArray()) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
        var timer = Stopwatch.StartNew();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType != "application/dns-message") throw new InvalidDataException("Провайдер вернул ответ другого формата.");
        await using var stream = await response.Content.ReadAsStreamAsync(budget.Token);
        using var payload = new MemoryStream(); var buffer = new byte[4096]; int read;
        while ((read = await stream.ReadAsync(buffer, budget.Token)) > 0)
        { if (payload.Length + read > 65535) throw new InvalidDataException("Слишком большой DNS-ответ."); payload.Write(buffer, 0, read); }
        var answers = ParseResponse(payload.ToArray(), id);
        return $"{domain}\nDNS: {settings.DnsProvider}\nТранспорт проверки: DoH, {(proxyPort.HasValue ? "через подключённый VPN" : "напрямую")}\nЗадержка: {timer.ElapsedMilliseconds} мс\nОтвет A: {string.Join(", ", answers)}\nПроверяется DoH выбранного провайдера. Встроенный DNS ядра может использовать другой транспорт.";
    }
    public static string[] ParseResponse(byte[] data, ushort id)
    {
        if (data.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(data) != id || (data[2] & 0x80) == 0 || (data[2] & 2) != 0)
            throw new InvalidDataException("Повреждённый или неполный DNS-ответ.");
        if ((data[3] & 15) != 0) throw new InvalidDataException($"DNS вернул код ошибки {data[3] & 15}.");
        var offset = 12;
        void Need(int size) { if (size < 0 || offset + size > data.Length) throw new InvalidDataException("DNS-ответ обрезан."); }
        void SkipName()
        {
            for (var n = 0; n < 128; n++)
            {
                Need(1); var length = data[offset++]; if (length == 0) return;
                if ((length & 0xC0) == 0xC0) { Need(1); var pointer = ((length & 63) << 8) | data[offset++]; if (pointer >= data.Length) throw new InvalidDataException("Некорректное сжатие DNS."); return; }
                if (length > 63) throw new InvalidDataException("Некорректное имя DNS."); Need(length); offset += length;
            }
            throw new InvalidDataException("Слишком длинное имя DNS.");
        }
        for (var q = 0; q < BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4)); q++) { SkipName(); Need(4); offset += 4; }
        var answers = new List<string>();
        for (var a = 0; a < BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(6)); a++)
        {
            SkipName(); Need(10); var type = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset)); var klass = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2));
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 8)); offset += 10; Need(length);
            if (klass == 1 && type == 1 && length == 4) answers.Add(new IPAddress(data.AsSpan(offset, 4)).ToString()); offset += length;
        }
        if (answers.Count == 0) throw new InvalidDataException("Провайдер не вернул IPv4-записей.");
        return answers.Distinct().ToArray();
    }
}
