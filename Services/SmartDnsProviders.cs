using System.Text.Json;
using System.Text.Json.Serialization;

namespace SplateClientDesktop.Services;

public record SmartDnsProvider(string Key, string Label, string Host, string[] BootstrapIpv4)
{
    public string DohUrl => $"https://{Host}/dns-query";
}
public static class SmartDnsProviders
{
    public static readonly string[] AiDomains = JsonSerializer.Deserialize<string[]>(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "filters", "ai-domains.json")))!;
    public static readonly SmartDnsProvider[] All = [
        new("dns_ai", "DNS-AI", "dns.dns-ai.ru", ["194.87.196.13", "186.246.49.127"]),
        new("geohide", "GeoHide RU", "geohide.ru", ["159.194.200.33", "193.233.112.67", "193.233.112.88", "193.233.112.68"]),
        new("geohide_eu", "GeoHide EU", "eu.geohide.ru", ["5.175.161.3", "5.175.161.40", "45.13.237.213"]),
        new("malw", "MALW", "dns.malw.link", ["164.5.224.81", "179.254.166.171"]),
        new("malw_gateway", "MALW · CF", "5u35p8m9i7.cloudflare-gateway.com", ["162.159.36.20", "162.159.36.5"]),
        new("zapret_dns", "Zapret DNS", "dns.zapret.moe", ["83.217.211.149"]),
        new("comss_smart", "Comss.one", "dns.comss.one", ["195.133.25.16", "83.220.169.155", "212.109.195.93"])
    ];
    public static SmartDnsProvider? Find(string key) => All.FirstOrDefault(p => p.Key == key.ToLowerInvariant());
    public static SmartDnsProvider ForAi(string key) => Find(key) ?? All[0];
    public static string ProbeConfig(string key, bool fragment, int port, string? uplink = null)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var p = ForAi(key);
        object Sockopt() => new { domainStrategy = "ForceIPv4", @interface = uplink };
        return JsonSerializer.Serialize(new
        {
            log = new { loglevel = "warning" },
            dns = new
            {
                queryStrategy = "UseIPv4",
                disableFallbackIfMatch = true,
                hosts = new Dictionary<string, string[]> { [p.Host] = p.BootstrapIpv4 },
                servers = new object[] {
                    new { address = p.DohUrl, skipFallback = true, domains = AiDomains },
                    // Chromium also needs scripts, fonts and sign-in resources.
                    // These use ordinary DNS/direct, without AI TLS fragmentation.
                    new { address = "https://1.1.1.1/dns-query" } }
            },
            inbounds = new[] { new { listen = "127.0.0.1", port, protocol = "socks", settings = new { auth = "noauth", udp = false } } },
            outbounds = new object[] {
                new { tag = "ai-probe", protocol = "freedom", settings = new { fragment = fragment ? new { packets = "tlshello", length = "10-20", interval = "1-2" } : null }, streamSettings = new { sockopt = Sockopt() } },
                new { tag = "dns-probe", protocol = "freedom", streamSettings = new { sockopt = Sockopt() } } },
            routing = new { rules = new object[] {
                new { type = "field", outboundTag = "dns-probe", domain = new[] { "full:" + p.Host } },
                new { type = "field", outboundTag = "ai-probe", domain = AiDomains },
                new { type = "field", outboundTag = "dns-probe", network = "tcp,udp" } } }
        }, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
    }
}
