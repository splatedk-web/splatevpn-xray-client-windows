using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public class SubscriptionResult
{
    public List<ServerNode> Nodes { get; set; } = new();
    public long Upload { get; set; } = 0;
    public long Download { get; set; } = 0;
    public long Total { get; set; } = 0;
    public long Expire { get; set; } = 0; // Unix timestamp
}

public static class SubscriptionService
{
    public static async Task<SubscriptionResult> FetchSubscriptionAsync(string url, Action<string>? log = null, int? proxyPort = null)
    {
        var cleanUrl = url.Trim();

        if (!Uri.TryCreate(cleanUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Некорректный адрес ссылки подписки (ожидается http:// или https://).");
        }

        log?.Invoke($"[SUBSCRIPTION] Запрос подписки: {uri.Host}...");

        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(8)
        };
        if (proxyPort.HasValue)
        {
            handler.Proxy = new System.Net.WebProxy($"http://127.0.0.1:{proxyPort.Value}");
            handler.UseProxy = true;
            log?.Invoke("[SUBSCRIPTION] Обновление через активный VPN-прокси.");
        }
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        SubscriptionResult? subResult = null;
        Exception? firstAttemptException = null;

        // Попытка 1: со стандартным User-Agent (v2rayN / SplateClient)
        try
        {
            subResult = await FetchInternalAsync(httpClient, cleanUrl, userAgent: "v2rayN/6.45 (Windows NT 10.0; Win64; x64) SplateClient/2.9.1", log);
        }
        catch (Exception ex) when (ex is TaskCanceledException || ex is HttpRequestException { StatusCode: null })
        {
            throw new HttpRequestException($"Не удалось подключиться к серверу подписки {uri.Authority}. Проверьте доступность сервера или обновите подписку через работающий VPN.", ex);
        }
        catch (Exception ex)
        {
            firstAttemptException = ex;
            log?.Invoke($"[SUBSCRIPTION] Первичный запрос завершился ошибкой: {ex.Message}. Пробуем альтернативный User-Agent...");
        }

        // Попытка 2: если попытка 1 вернула 0 узлов или завершилась сетевой ошибкой HTTP, пробуем User-Agent ClashMeta
        if (subResult == null || subResult.Nodes.Count == 0)
        {
            try
            {
                log?.Invoke("[SUBSCRIPTION] Повторная попытка с User-Agent: ClashMeta/1.18.0...");
                var clashResult = await FetchInternalAsync(httpClient, cleanUrl, userAgent: "ClashMeta/1.18.0", log);
                if (clashResult.Nodes.Count > 0)
                {
                    return clashResult;
                }
                if (subResult == null)
                {
                    subResult = clashResult;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[SUBSCRIPTION] Повторный запрос (ClashMeta) завершился ошибкой: {ex.Message}");
                if (subResult == null && firstAttemptException != null)
                {
                    throw firstAttemptException;
                }
            }
        }

        if (subResult == null)
        {
            if (firstAttemptException != null)
                throw firstAttemptException;
            throw new InvalidOperationException("Не удалось загрузить данные подписки.");
        }

        return subResult;
    }

    private static async Task<SubscriptionResult> FetchInternalAsync(HttpClient httpClient, string cleanUrl, string userAgent, Action<string>? log)
    {
        var subResult = new SubscriptionResult();

        using var request = new HttpRequestMessage(HttpMethod.Get, cleanUrl);
        request.Headers.Clear();
        request.Headers.Add("User-Agent", userAgent);
        request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,application/x-yaml,text/yaml,text/plain,*/*;q=0.8");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Сервер подписки ответил ошибкой: {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
        }

        // Проверяем метаданные подписки (Userinfo), если сервер их предоставляет
        if (response.Headers.TryGetValues("subscription-userinfo", out var userInfoValues))
        {
            foreach (var ui in userInfoValues)
            {
                log?.Invoke($"[SUBSCRIPTION INFO] {ui}");
                
                var parts = ui.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var kv = part.Split('=');
                    if (kv.Length == 2)
                    {
                        var key = kv[0].Trim().ToLowerInvariant();
                        if (long.TryParse(kv[1].Trim(), out var val))
                        {
                            switch (key)
                            {
                                case "upload": subResult.Upload = val; break;
                                case "download": subResult.Download = val; break;
                                case "total": subResult.Total = val; break;
                                case "expire": subResult.Expire = val; break;
                            }
                        }
                    }
                }
            }
        }

        var rawContent = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            throw new InvalidOperationException("Сервер подписки вернул пустой ответ.");
        }

        log?.Invoke($"[SUBSCRIPTION] Получен ответ ({rawContent.Length} символов). Декодирование узлов...");

        // 1. Попытка декодировать из Base64 (стандартного или URL-safe)
        var decoded = TryDecodeBase64(rawContent);

        // 2. Парсим узлы из декодированного или исходного текста
        var result = LinkParser.ParseMultiple(decoded);

        // 3. Если ничего не найдено, пробуем распарсить исходный текст без декодирования
        if (result.Count == 0 && decoded != rawContent)
        {
            result = LinkParser.ParseMultiple(rawContent);
        }

        log?.Invoke($"[SUBSCRIPTION] Успешно распознано узлов: {result.Count}");
        subResult.Nodes = result;
        return subResult;
    }

    public static string TryDecodeBase64(string input)
    {
        var cleaned = input.Trim().Replace("\r", "").Replace("\n", "").Replace(" ", "");
        
        // Если уже содержит явные ссылки протоколов, декодирование всей строки не требуется
        if (cleaned.Contains("vless://") || cleaned.Contains("vmess://") || 
            cleaned.Contains("trojan://") || cleaned.Contains("ss://"))
        {
            return input;
        }

        try
        {
            // Поддержка URL-safe Base64
            cleaned = cleaned.Replace('-', '+').Replace('_', '/');

            // Исправление выравнивания (padding)
            var pad = (4 - cleaned.Length % 4) % 4;
            if (pad > 0)
            {
                cleaned = cleaned.PadRight(cleaned.Length + pad, '=');
            }

            var bytes = Convert.FromBase64String(cleaned);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // В случае ошибки возвращаем исходную строку
            return input;
        }
    }
}
