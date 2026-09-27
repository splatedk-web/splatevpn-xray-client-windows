using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace SplateClientDesktop.Services;

public class AppUpdateInfo
{
    public bool IsUpdateAvailable { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseUrl { get; set; } = string.Empty;
}

public static class UpdateCheckerService
{
    private static readonly HttpClient _httpClient;

    static UpdateCheckerService()
    {
        _httpClient = new HttpClient();
        // GitHub API requires a User-Agent
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SplateClientDesktop/UpdateChecker");
    }

    public static async Task<AppUpdateInfo> CheckForUpdatesAsync(string currentVersion)
    {
        var result = new AppUpdateInfo { IsUpdateAvailable = false };
        try
        {
            var url = "https://api.github.com/repos/splatedk-web/splatevpn-xray-client-windows/releases/latest";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var node = JsonNode.Parse(content);
                var tagName = node?["tag_name"]?.ToString();
                var htmlUrl = node?["html_url"]?.ToString();

                if (!string.IsNullOrEmpty(tagName) && !string.IsNullOrEmpty(htmlUrl))
                {
                    // Clean tag name (e.g., "v2.5" or "2.5.0" -> "2.5.0")
                    var latestVer = tagName.TrimStart('v', 'V');
                    var currVer = currentVersion.TrimStart('v', 'V');

                    if (Version.TryParse(latestVer, out var latest) && Version.TryParse(currVer, out var curr))
                    {
                        if (latest > curr)
                        {
                            result.IsUpdateAvailable = true;
                            result.LatestVersion = tagName;
                            result.ReleaseUrl = htmlUrl;
                        }
                    }
                    else
                    {
                        // Fallback simple string comparison if version parsing fails
                        if (string.Compare(latestVer, currVer, StringComparison.OrdinalIgnoreCase) > 0)
                        {
                            result.IsUpdateAvailable = true;
                            result.LatestVersion = tagName;
                            result.ReleaseUrl = htmlUrl;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore errors during update check
        }
        return result;
    }
}
