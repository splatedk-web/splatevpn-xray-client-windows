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
    private static readonly HttpClient _httpClient;

    static SubscriptionService()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "v2rayN/6.45 (Windows NT 10.0; Win64; x64) SplateClient/2.7");
        _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
    }

    public static async Task<SubscriptionResult> FetchSubscriptionAsync(string url, Action<string>? log = null)
    {
        var subResult = new SubscriptionResult();
        var cleanUrl = url.Trim();

        if (!Uri.TryCreate(cleanUrl, UriKind.Absolute, out var uri) || 
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Некорректный адрес ссылки подписки (ожидается http:// или https://).");
        }

        log?.Invoke($"[SUBSCRIPTION] Отправка запроса к источнику: {cleanUrl}...");

        using var request = new HttpRequestMessage(HttpMethod.Get, cleanUrl);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Сервер подписки ответил ошибкой: {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        // Проверяем метаданные подписки (Userinfo), если сервер их предоставляет
        // Пример: upload=0; download=12345678; total=53687091200; expire=1712345678
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
