using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace SplateClientDesktop.Services;

public static class GeoAssetUpdaterService
{
    private const string GeoipUrl = "https://raw.githubusercontent.com/Loyalsoldier/v2ray-rules-dat/release/geoip.dat";
    private const string GeositeUrl = "https://raw.githubusercontent.com/Loyalsoldier/v2ray-rules-dat/release/geosite.dat";
    private static readonly SemaphoreSlim Gate = new(1);

    public static async Task<bool> UpdateGeoAssetsAsync(string coreDirectory, Action<string>? log = null, HttpClient? client = null, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        var staging = Path.Combine(Path.GetTempPath(), "splate-geo-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (string.IsNullOrEmpty(coreDirectory) || !Directory.Exists(coreDirectory))
                throw new DirectoryNotFoundException("Папка ядра не найдена.");
            using var ownedClient = client == null ? new HttpClient { Timeout = TimeSpan.FromMinutes(2) } : null;
            var http = client ?? ownedClient!;
            Directory.CreateDirectory(staging);
            var files = new Dictionary<string, byte[]>();
            foreach (var (name, url) in new[] { ("geoip.dat", GeoipUrl), ("geosite.dat", GeositeUrl) })
            {
                log?.Invoke($"[GEO-UPDATE] Загрузка {name}...");
                var bytes = await http.GetByteArrayAsync(url, ct);
                if (bytes.Length <= 1024 * 1024)
                    throw new InvalidDataException($"Сервер вернул слишком маленький файл {name}.");
                var checksum = (await http.GetStringAsync(url + ".sha256sum", ct)).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (checksum?.Length != 64 || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(checksum, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Контрольная сумма {name} не совпадает.");
                await File.WriteAllBytesAsync(Path.Combine(staging, name), bytes, ct);
                files[Path.Combine(coreDirectory, name)] = bytes;
            }
            await ValidateAsync(coreDirectory, staging, ct);
            foreach (var name in new[] { "geoip.dat", "geosite.dat" })
                if (File.Exists(Path.Combine(coreDirectory, name))) files[Path.Combine(coreDirectory, name + ".previous")] = await File.ReadAllBytesAsync(Path.Combine(coreDirectory, name), ct);
            ct.ThrowIfCancellationRequested();
            AtomicFileSet.Write(files);
            log?.Invoke("[GEO-UPDATE] Обе базы проверены и успешно обновлены.");
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log?.Invoke("[GEO-UPDATE ERROR] " + ex.Message);
            return false;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            Gate.Release();
        }
    }

    private static async Task ValidateAsync(string coreDirectory, string staging, CancellationToken ct)
    {
        var config = Path.Combine(staging, "validate.json");
        await File.WriteAllTextAsync(config, """
            {"outbounds":[{"tag":"direct","protocol":"freedom"}],"routing":{"rules":[
            {"type":"field","ip":["geoip:private"],"outboundTag":"direct"},
            {"type":"field","domain":["geosite:cn","geosite:category-ir","geosite:category-ru"],"outboundTag":"direct"}]}}
            """);
        var start = new ProcessStartInfo(Path.Combine(Path.GetFullPath(coreDirectory), "xray.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = staging };
        foreach (var arg in new[] { "run", "-test", "-c", config }) start.ArgumentList.Add(arg);
        start.Environment["XRAY_LOCATION_ASSET"] = staging;
        using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить проверку геобаз.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
        var diagnostics = await output + await errors;
        if (process.ExitCode != 0) throw new InvalidDataException("Xray отклонил геобазы: " + diagnostics);
    }
    public static async Task<bool> RollbackAsync(string coreDirectory, Action<string>? log = null, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        var staging = Path.Combine(Path.GetTempPath(), "splate-geo-rollback-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging); var files = new Dictionary<string, byte[]>();
            foreach (var name in new[] { "geoip.dat", "geosite.dat" })
            {
                var target = Path.Combine(coreDirectory, name);
                var previous = await File.ReadAllBytesAsync(target + ".previous", ct);
                await File.WriteAllBytesAsync(Path.Combine(staging, name), previous, ct);
                files[target] = previous;
                files[target + ".previous"] = await File.ReadAllBytesAsync(target, ct);
            }
            await ValidateAsync(coreDirectory, staging, ct);
            ct.ThrowIfCancellationRequested(); AtomicFileSet.Write(files);
            log?.Invoke("[GEO] Предыдущие геобазы проверены и восстановлены."); return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { log?.Invoke("[GEO] Не удалось откатить геобазы: " + ex.Message); return false; }
        finally { try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } finally { Gate.Release(); } }
    }
}
