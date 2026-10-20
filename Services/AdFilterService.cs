using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SplateClientDesktop.Services;

public record AdFilterState(int Count = 0, DateTimeOffset? Updated = null, string Status = "Встроенная база готова к использованию.");
public static class AdFilterService
{
    public const string Source = "https://adguardteam.github.io/AdGuardSDNSFilter/Filters/filter.txt";
    public static string AssetDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SplateVPN", "XrayAssets");
    private static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SplateVPN", "Filters");
    private static readonly SemaphoreSlim Gate = new(1);
    public static AdFilterState State { get; private set; } = new();
    public static readonly string[] Additional = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "filters", "additional.json")))!;
    private static readonly HashSet<string> Protected = new("yandex.ru yandex.net yandex.com ya.ru vk.com vk.ru mail.ru my.com google.com googleapis.com gstatic.com youtube.com googlevideo.com ytimg.com yastatic.net imgsmail.ru cloudflare.com github.com tiktok.com yahoo.com yahooinc.com miui.com".Split(' '));
    private sealed record Metadata(string Key, string File, string Sha, int Count, DateTimeOffset? Updated);
    public static string EnsureAssets(string baseDirectory)
    {
        Directory.CreateDirectory(AssetDirectory);
        foreach (var file in new[] { "geoip.dat", "geosite.dat" })
        {
            var source = new FileInfo(Path.Combine(baseDirectory, "core", file));
            var target = new FileInfo(Path.Combine(AssetDirectory, file));
            if (source.Exists && (!target.Exists || source.Length != target.Length || source.LastWriteTimeUtc != target.LastWriteTimeUtc))
            { File.Copy(source.FullName, target.FullName + ".tmp", true); File.Move(target.FullName + ".tmp", target.FullName, true); File.SetLastWriteTimeUtc(target.FullName, source.LastWriteTimeUtc); }
        }
        return AssetDirectory;
    }
    public static string? NormalizeDomain(string input)
    {
        try
        {
            var host = new IdnMapping().GetAscii(input.Trim().TrimEnd('.')).ToLowerInvariant();
            return host.Length is >= 3 and <= 253 && host.Contains('.') && !IPAddress.TryParse(host, out _) &&
                host.Split('.').All(label => label.Length is >= 1 and <= 63 && Regex.IsMatch(label, "^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")) ? host : null;
        }
        catch (ArgumentException) { return null; }
    }
    public static string[] Allowlist(string value)
    {
        var entries = value.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length > 100) throw new ArgumentException("В исключениях допускается до 100 доменов.");
        return entries.Select(s => NormalizeDomain(s) ?? throw new ArgumentException("Некорректный домен исключения: " + s)).Distinct().Order().ToArray();
    }
    public static string[] Parse(string text, IEnumerable<string> additional, IEnumerable<string> allowed)
    {
        var blocked = new HashSet<string>(); var exceptions = allowed.Select(NormalizeDomain).OfType<string>().ToHashSet();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim(); var rule = Regex.Match(line, "^\\|\\|([a-zA-Z0-9_.-]+)\\^$");
            var except = Regex.Match(line, "^@@\\|\\|([a-zA-Z0-9_.-]+)\\^(?:\\$.*)?$");
            if (rule.Success && NormalizeDomain(rule.Groups[1].Value) is string block) blocked.Add(block);
            if (except.Success && NormalizeDomain(except.Groups[1].Value) is string allow) exceptions.Add(allow);
        }
        foreach (var value in additional) if (NormalizeDomain(value) is string domain) blocked.Add(domain);
        foreach (var domain in exceptions.Concat(Protected))
            for (var parent = domain; parent.Contains('.'); parent = parent[(parent.IndexOf('.') + 1)..]) blocked.Remove(parent);
        return blocked.Where(domain => !exceptions.Any(allow => domain == allow || domain.EndsWith("." + allow, StringComparison.Ordinal))).Order().ToArray();
    }
    public static byte[] Encode(IEnumerable<string> domains)
    {
        static void Varint(Stream output, int n) { while (n >= 128) { output.WriteByte((byte)((n & 127) | 128)); n >>= 7; } output.WriteByte((byte)n); }
        static void Field(Stream output, byte tag, byte[] bytes) { output.WriteByte(tag); Varint(output, bytes.Length); output.Write(bytes); }
        using var site = new MemoryStream(); Field(site, 10, "ADS"u8.ToArray());
        foreach (var domain in domains) { using var entry = new MemoryStream(); entry.WriteByte(8); entry.WriteByte(2); Field(entry, 18, Encoding.UTF8.GetBytes(domain)); Field(site, 18, entry.ToArray()); }
        using var result = new MemoryStream(); Field(result, 10, site.ToArray()); return result.ToArray();
    }
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static async Task Atomic(string path, byte[] data, CancellationToken token) { var temp = path + "." + Guid.NewGuid().ToString("N"); try { await File.WriteAllBytesAsync(temp, data, token); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); } }
    private static async Task<string> Bundled(string root, CancellationToken token) { await using var source = File.OpenRead(Path.Combine(root, "Assets", "filters", "adguard-dns-source.bin")); await using var gzip = new GZipStream(source, CompressionMode.Decompress); using var reader = new StreamReader(gzip); return await reader.ReadToEndAsync(token); }
    public static Task<string> PrepareAsync(string root, string allow, CancellationToken token = default) => Task.Run(() => PrepareCoreAsync(root, allow, token), token);
    private static async Task<string> PrepareCoreAsync(string root, string allow, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try
        {
            EnsureAssets(root); Directory.CreateDirectory(DataDirectory);
            var path = Path.Combine(DataDirectory, "source.txt");
            var text = File.Exists(path) ? await File.ReadAllTextAsync(path, token) : await Bundled(root, token);
            var key = Hash(Encoding.UTF8.GetBytes(text + "\n" + string.Join('\n', Allowlist(allow)) + "\n" + string.Join('\n', Additional)));
            var metaPath = Path.Combine(DataDirectory, "metadata.json"); Metadata? meta = null;
            try { if (File.Exists(metaPath)) meta = JsonSerializer.Deserialize<Metadata>(await File.ReadAllTextAsync(metaPath, token)); } catch (JsonException) { }
            var file = "splate-ads-" + key[..20].ToLowerInvariant() + ".dat";
            var output = Path.Combine(AssetDirectory, file);
            if (meta?.Key == key && meta.File == file && File.Exists(output) && Hash(await File.ReadAllBytesAsync(output, token)) == meta.Sha)
            { State = new(meta.Count, meta.Updated, "База готова."); return "ext:" + file + ":ads"; }
            var domains = Parse(text, Additional, Allowlist(allow));
            if (domains.Length is < 5000 or > 300000) { text = await Bundled(root, token); domains = Parse(text, Additional, Allowlist(allow)); }
            if (domains.Length is < 5000 or > 300000) throw new InvalidDataException("Некорректная база рекламных доменов.");
            var bytes = Encode(domains); await Atomic(output, bytes, token);
            var updated = File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path)) : (DateTimeOffset?)null;
            await Atomic(metaPath, JsonSerializer.SerializeToUtf8Bytes(new Metadata(key, file, Hash(bytes), domains.Length, updated)), token);
            State = new(domains.Length, updated, "База готова."); return "ext:" + file + ":ads";
        }
        finally { Gate.Release(); }
    }
    public static Task<bool> RefreshAsync(string root, string allow, int? proxyPort, CancellationToken token = default) => Task.Run(() => RefreshCoreAsync(root, allow, proxyPort, token), token);
    private static async Task<bool> RefreshCoreAsync(string root, string allow, int? proxyPort, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try
        {
            State = State with { Status = "Загрузка фильтров…" };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(40)); var ct = timeout.Token;
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = proxyPort.HasValue, Proxy = proxyPort.HasValue ? new WebProxy($"http://127.0.0.1:{proxyPort}") : null };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(40) };
            using var response = await client.GetAsync(Source, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream(); var buffer = new byte[65536];
            while (true) { var n = await stream.ReadAsync(buffer, ct); if (n == 0) break; if (output.Length + n > 12 * 1024 * 1024) throw new InvalidDataException("Файл фильтров слишком большой."); output.Write(buffer, 0, n); }
            var text = Encoding.UTF8.GetString(output.ToArray()); var domains = Parse(text, Additional, Allowlist(allow));
            if (!text.Contains("Title: AdGuard DNS filter") || domains.Length is < 5000 or > 300000 || (State.Count > 0 && domains.Length < State.Count / 2)) throw new InvalidDataException("Проверка базы фильтров не пройдена.");
            Directory.CreateDirectory(DataDirectory); await Atomic(Path.Combine(DataDirectory, "source.txt"), output.ToArray(), ct);
            State = new(domains.Length, DateTimeOffset.UtcNow, "Загружено. Применится при следующем подключении."); return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or OperationCanceledException) { State = State with { Status = "Обновление не удалось. Сохранена предыдущая база." }; return false; }
        finally { Gate.Release(); }
    }
    private static string[] Matchers(string? extended, string allow)
    {
        var exceptions = Allowlist(allow);
        if (extended != null) return exceptions.Length == 0 ? [extended, "geosite:category-ads-all"] : [extended];
        var rules = Additional.Where(d => !exceptions.Any(a => d == a || d.EndsWith("." + a) || a.EndsWith("." + d))).Select(d => "domain:" + d);
        return (exceptions.Length == 0 ? rules.Prepend("geosite:category-ads-all") : rules).ToArray();
    }
    public static void AddDnsHosts(JsonObject hosts, string? matcher, string allow) { foreach (var entry in Matchers(matcher, allow)) hosts[entry] = new JsonArray(new JsonNode?[] { JsonValue.Create("0.0.0.0"), JsonValue.Create("::") }); }
    public static JsonObject BlockRule(string? matcher, string allow) => new() { ["type"] = "field", ["outboundTag"] = "block", ["domain"] = new JsonArray(Matchers(matcher, allow).Select(s => JsonValue.Create(s)).ToArray()) };
}
