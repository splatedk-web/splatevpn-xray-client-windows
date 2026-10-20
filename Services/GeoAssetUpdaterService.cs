using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace SplateClientDesktop.Services;

public static class GeoAssetUpdaterService
{
    private const string GeoipUrl = "https://raw.githubusercontent.com/Loyalsoldier/v2ray-rules-dat/release/geoip.dat";
    private const string GeositeUrl = "https://raw.githubusercontent.com/Loyalsoldier/v2ray-rules-dat/release/geosite.dat";
    private static readonly SemaphoreSlim Gate = new(1);

    public static async Task<bool> UpdateGeoAssetsAsync(string coreDirectory, Action<string>? log = null, HttpClient? client = null)
    {
        await Gate.WaitAsync();
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
                var bytes = await http.GetByteArrayAsync(url);
                if (bytes.Length <= 1024 * 1024)
                    throw new InvalidDataException($"Сервер вернул слишком маленький файл {name}.");
                await File.WriteAllBytesAsync(Path.Combine(staging, name), bytes);
                files[Path.Combine(coreDirectory, name)] = bytes;
            }
            // Xray parses both protobuf databases before the working files are replaced.
            var config = Path.Combine(staging, "validate.json");
            await File.WriteAllTextAsync(config, """
                {"outbounds":[{"tag":"direct","protocol":"freedom"}],"routing":{"rules":[
                {"type":"field","ip":["geoip:private"],"outboundTag":"direct"},
                {"type":"field","domain":["geosite:cn"],"outboundTag":"direct"}]}}
                """);
            var start = new ProcessStartInfo(Path.Combine(Path.GetFullPath(coreDirectory), "xray.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = staging };
            foreach (var arg in new[] { "run", "-test", "-c", config }) start.ArgumentList.Add(arg);
            start.Environment["XRAY_LOCATION_ASSET"] = staging;
            using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить проверку геобаз.");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
            var diagnostics = await output + await errors;
            if (process.ExitCode != 0) throw new InvalidDataException("Xray отклонил геобазы: " + diagnostics);
            AtomicFileSet.Write(files);
            log?.Invoke("[GEO-UPDATE] Обе базы проверены и успешно обновлены.");
            return true;
        }
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
}
