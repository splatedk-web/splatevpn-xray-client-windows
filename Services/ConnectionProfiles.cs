using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public static class ConnectionProfiles
{
    private static readonly HashSet<string> Keys = new(("LanBindAddress LanAuthEnabled LanUsername LanPassword RoutingRegion UpstreamProxyEnabled UpstreamProxyHost UpstreamProxyPort RoutingMode AiRoutingMode AiDnsProvider AiTlsFragment AiDnsAutoSwitch EnableAiBypass EnableAiDns EnableAiXboxDns EnableSniffing RouteOnlySniffing CustomProxyRules CustomDirectRules CustomBlockRules EnableAdBlock AdBlockAllowlist DnsProvider CustomDohUrl IsFakeDns BlockQuic BlockIpv6 EnableTunMode HttpPort SocksPort ShareLan BufferSizeKb TcpCongestion EnableTlsFragment TlsFragmentLength TlsFragmentInterval TlsFragmentPackets XhttpModeOverride EnableMux MuxConcurrency").Split(' '));
    private static readonly PropertyInfo[] Fields = typeof(AppSettings).GetProperties().Where(p => Keys.Contains(p.Name)).ToArray();
    public static string Capture(AppSettings settings)
    {
        var all = JsonSerializer.SerializeToNode(settings)!.AsObject();
        return new JsonObject(all.Where(p => Keys.Contains(p.Key)).Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone()))).ToJsonString();
    }
    public static AppSettings Target(AppSettings current, string snapshot)
    {
        if (snapshot.Length > 256_000) throw new ArgumentException("Профиль слишком большой.");
        var json = JsonNode.Parse(snapshot)?.AsObject() ?? throw new ArgumentException("Пустой профиль.");
        if (Keys.Except(new[] { "LanBindAddress", "LanAuthEnabled", "LanUsername", "LanPassword", "RoutingRegion", "UpstreamProxyEnabled", "UpstreamProxyHost", "UpstreamProxyPort" }).Any(k => !json.ContainsKey(k)) || json.Any(p => !Keys.Contains(p.Key) || p.Value == null)) throw new ArgumentException("Некорректные поля профиля.");
        var target = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(current))!;
        var values = json.Deserialize<AppSettings>()!;
        foreach (var field in Fields) field.SetValue(target, field.GetValue(values));
        Validate(target);
        SettingsCompatibility.Normalize(target);
        return target;
    }
    private static void Validate(AppSettings s)
    {
        if (s.HttpPort is < 1 or > 65535 || s.SocksPort is < 1 or > 65535 || s.HttpPort == s.SocksPort ||
            s.RoutingMode is not ("global" or "smart_ru") || s.AiRoutingMode is not ("off" or "vpn" or "dns_ai" or "dns_vpn") ||
            SmartDnsProviders.Find(s.AiDnsProvider) == null || s.MuxConcurrency is < 1 or > 128 ||
            !XrayConfigGenerator.ValidRange(s.TlsFragmentLength, 1, 65535) || !XrayConfigGenerator.ValidRange(s.TlsFragmentInterval, 0, 10000) ||
            !(s.TlsFragmentPackets == "tlshello" || XrayConfigGenerator.ValidRange(s.TlsFragmentPackets, 1, 100)))
            throw new ArgumentException("Некорректные сетевые настройки профиля.");
        XrayConfigGenerator.ValidateDoh(s.CustomDohUrl);
        AdFilterService.Allowlist(s.AdBlockAllowlist);
    }
    public static void CopyNetwork(AppSettings source, AppSettings target)
    { foreach (var field in Fields) field.SetValue(target, field.GetValue(source)); }
    public static ConnectionProfile Save(AppSettings settings, string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 48 || settings.ConnectionProfiles.Count >= 8) throw new ArgumentException("Имя: 1–48 символов. Максимум восемь профилей.");
        var snapshot = Capture(settings); Target(settings, snapshot);
        var profile = new ConnectionProfile(Guid.NewGuid().ToString("N"), name, snapshot);
        settings.ConnectionProfiles.Add(profile); return profile;
    }
    public static string BuiltIn(AppSettings current, string id)
    {
        var s = Target(current, Capture(current));
        switch (id)
        {
            case "smart": s.RoutingMode = "smart_ru"; break;
            case "all": s.RoutingMode = "global"; s.AiRoutingMode = "vpn"; s.ShareLan = false; s.EnableTunMode = true; break;
            case "ai": s.RoutingMode = "smart_ru"; s.AiRoutingMode = "vpn"; break;
            default: throw new ArgumentException("Профиль не найден.");
        }
        SettingsCompatibility.Normalize(s); return Capture(s);
    }
}
