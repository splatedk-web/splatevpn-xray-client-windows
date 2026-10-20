using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SplateClientDesktop.Services;

public class AppUpdateInfo
{
    public bool CheckSucceeded { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public string LatestVersion { get; set; } = "";
    public string ReleaseUrl { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
}

public static class UpdateCheckerService
{
    public const string LatestReleaseApi = "https://api.github.com/repos/splatedk-web/splatevpn-xray-client-windows/releases/latest";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    public static string CurrentVersion
    {
        get
        {
            var version = typeof(UpdateCheckerService).Assembly.GetName().Version!;
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build)).ToString();
        }
    }

    public static async Task<AppUpdateInfo> CheckForUpdatesAsync(string? currentVersion = null, HttpClient? client = null, CancellationToken token = default)
    {
        var result = new AppUpdateInfo();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.ParseAdd("SplateClientDesktop/UpdateChecker");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await (client ?? Client).SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
            {
                result.ErrorMessage = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
                    ? "GitHub ограничил число проверок. Повторите позже."
                    : $"Не удалось проверить обновления: GitHub ответил HTTP {(int)response.StatusCode}.";
                return result;
            }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var release = json.RootElement;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            var url = release.GetProperty("html_url").GetString() ?? "";
            if ((release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) ||
                (release.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean()))
                throw new FormatException("GitHub не вернул стабильный релиз.");
            if (!TryVersion(tag, out var latest) || !TryVersion(currentVersion ?? CurrentVersion, out var current))
                throw new FormatException("Не удалось распознать номер версии релиза.");
            if (!IsReleaseUrl(url)) throw new FormatException("GitHub вернул некорректную ссылку релиза.");
            result.CheckSucceeded = true;
            result.IsUpdateAvailable = latest > current;
            result.LatestVersion = tag;
            result.ReleaseUrl = url;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { result.ErrorMessage = "GitHub не ответил за отведённое время. Повторите проверку позже."; }
        catch (HttpRequestException) { result.ErrorMessage = "Не удалось связаться с GitHub. Проверьте подключение и повторите попытку."; }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        { result.ErrorMessage = "Не удалось прочитать сведения об обновлении. " + ex.Message; }
        return result;
    }

    public static bool IsReleaseUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/splatedk-web/splatevpn-xray-client-windows/releases/", StringComparison.Ordinal);

    private static bool TryVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (!Version.TryParse(text.Trim().TrimStart('v', 'V'), out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        return true;
    }
}
