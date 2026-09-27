using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SplateClientDesktop.Services;

public static class GeoAssetUpdaterService
{
    private const string GeoipUrl = "https://raw.githubusercontent.com/Loyalsoldier/v2ray-rules-dat/release/geoip.dat";
    private const string GeositeUrl = "https://raw.githubusercontent.com/Loyalsoldier/v2ray-rules-dat/release/geosite.dat";

    public static async Task<bool> UpdateGeoAssetsAsync(string coreDirectory, Action<string>? log = null)
    {
        try
        {
            if (string.IsNullOrEmpty(coreDirectory) || !Directory.Exists(coreDirectory))
            {
                log?.Invoke("[GEO-UPDATE] Ошибка: папка ядра не найдена.");
                return false;
            }

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            httpClient.DefaultRequestHeaders.Add("User-Agent", "SplateVPN-Client/2.7");

            // 1. Update geoip.dat
            log?.Invoke("[GEO-UPDATE] Загрузка актуальной базы geoip.dat...");
            var geoipPath = Path.Combine(coreDirectory, "geoip.dat");
            var geoipTemp = geoipPath + ".tmp";
            var geoipBytes = await httpClient.GetByteArrayAsync(GeoipUrl);
            if (geoipBytes.Length > 1024 * 1024) // Sanity check > 1MB
            {
                await File.WriteAllBytesAsync(geoipTemp, geoipBytes);
                File.Move(geoipTemp, geoipPath, true);
                log?.Invoke($"[GEO-UPDATE] geoip.dat успешно обновлен ({geoipBytes.Length / (1024.0 * 1024):F1} MB).");
            }

            // 2. Update geosite.dat
            log?.Invoke("[GEO-UPDATE] Загрузка актуальной базы geosite.dat...");
            var geositePath = Path.Combine(coreDirectory, "geosite.dat");
            var geositeTemp = geositePath + ".tmp";
            var geositeBytes = await httpClient.GetByteArrayAsync(GeositeUrl);
            if (geositeBytes.Length > 1024 * 1024) // Sanity check > 1MB
            {
                await File.WriteAllBytesAsync(geositeTemp, geositeBytes);
                File.Move(geositeTemp, geositePath, true);
                log?.Invoke($"[GEO-UPDATE] geosite.dat успешно обновлен ({geositeBytes.Length / (1024.0 * 1024):F1} MB).");
            }

            log?.Invoke("[GEO-UPDATE] Базы маршрутизации успешно обновлены до последней версии!");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[GEO-UPDATE ERROR] Ошибка обновления баз: {ex.Message}");
            return false;
        }
    }
}
