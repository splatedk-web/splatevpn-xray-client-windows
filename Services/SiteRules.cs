using System.Globalization;
using System.Text.Json.Nodes;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public static class SiteRules
{
    public static string Domain(string input)
    {
        input = input.Trim();
        if (input.Contains("://"))
        {
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                throw new ArgumentException("Введите домен или HTTP(S)-адрес сайта.");
            input = uri.Host;
        }
        input = new IdnMapping().GetAscii(input.TrimEnd('.')).ToLowerInvariant();
        if (input.Length > 253 || System.Net.IPAddress.TryParse(input, out _) || Uri.CheckHostName(input) != UriHostNameType.Dns ||
            !input.Contains('.') || input.Split('.').Any(s => s.Length is < 1 or > 63 || s.StartsWith('-') || s.EndsWith('-') || s.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new ArgumentException("Укажите корректный домен, например example.com.");
        return input;
    }
    public static bool Matches(string domain, string rule) => rule.StartsWith("full:") ? domain == rule[5..] :
        rule.StartsWith("domain:") ? MatchSuffix(domain, rule[7..]) : !rule.Contains(':') && MatchSuffix(domain, rule);
    private static bool MatchSuffix(string domain, string suffix) => domain == suffix || domain.EndsWith('.' + suffix, StringComparison.Ordinal);

    public static string Preview(AppSettings settings, string input)
    {
        var domain = Domain(input);
        var config = JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(null, mode: settings.RoutingMode,
            enableAiBypass: settings.EnableAiBypass, enableAiDns: settings.EnableAiDns, aiRoutingMode: settings.AiRoutingMode,
            customProxyRules: settings.CustomProxyRules, customDirectRules: settings.CustomDirectRules,
            customBlockRules: settings.CustomBlockRules, routingRegion: settings.RoutingRegion,
            temporaryRoutes: settings.TemporaryRoutes))!;
        var uncertain = false;
        foreach (var rule in config["routing"]!["rules"]!.AsArray())
        {
            if (rule!["inboundTag"] != null || rule["port"] != null || rule["network"] != null || rule["protocol"] != null) continue;
            if (rule["domain"] is JsonArray domains)
            {
                if (domains.Any(d => Matches(domain, d!.GetValue<string>())))
                    return $"{domain}\nПравило: {Label(rule["outboundTag"]!.GetValue<string>())}." +
                        (uncertain ? "\nПеред ним есть правила геобазы. Итог зависит от геобазы и IP сайта." : "\nСовпадение по домену, включая поддомены.");
                uncertain |= domains.Any(d => d!.GetValue<string>().StartsWith("geosite:"));
            }
            if (rule["ip"] != null) uncertain = true;
        }
        return $"{domain}\nПо умолчанию: через VPN. Геобаза, IP-правила и блокировщик могут изменить маршрут. Для фактического соединения проверьте журнал.";
    }
    private static string Label(string tag) => tag switch { "direct" => "напрямую", "block" => "блокировать", "ai-smartdns" => "SmartDNS", _ => "через VPN" };
}
