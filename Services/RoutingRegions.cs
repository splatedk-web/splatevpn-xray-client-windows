using System.Text.Json.Nodes;

namespace SplateClientDesktop.Services;

public static class RoutingRegions
{
    public static bool IsValid(string region) => region is "ru" or "cn" or "ir" or "global";
    public static string Dns(string region) => region switch { "cn" => "223.5.5.5", "ir" => "178.22.122.100", _ => "77.88.8.8" };
    public static JsonArray Domains(string region) => region switch
    {
        "ru" => new JsonArray { "geosite:category-ru", "geosite:tld-ru", "geosite:yandex", "geosite:vk", "geosite:mailru",
            "domain:gosuslugi.ru", "domain:sberbank.ru", "domain:tinkoff.ru", "domain:vtb.ru", "domain:alfabank.ru",
            "domain:raiffeisen.ru", "domain:gazprombank.ru", "domain:mos.ru", "domain:nalog.ru", "domain:ru", "domain:su", "domain:xn--p1ai" },
        "cn" => new JsonArray { "geosite:cn", "domain:cn" },
        "ir" => new JsonArray { "geosite:category-ir", "domain:ir" },
        _ => new JsonArray()
    };
}
