using System.Text.Json;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

// Only settings used by the core or the Windows network integration belong here.
// Appearance, subscriptions and balancing policy must not interrupt a session.
public static class ConnectionSettings
{
    public static string Signature(AppSettings settings)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        // The running core keeps a rule until reconfigured, even after its clock expires.
        // Keep the active snapshot distinct from the pruned saved settings.
        var temporaryRoutes = s.TemporaryRoutes?.ToArray() ?? Array.Empty<TemporaryRoute>();
        SettingsCompatibility.Normalize(s);
        return JsonSerializer.Serialize(new
    {
        TemporaryRoutes = temporaryRoutes, s.RoutingRegion, s.RoutingMode, s.AiRoutingMode, s.EnableAiBypass, s.EnableAiDns, s.EnableAiXboxDns,
        s.AiDnsProvider, s.AiTlsFragment, s.EnableSniffing, s.RouteOnlySniffing,
        s.CustomProxyRules, s.CustomDirectRules, s.CustomBlockRules, s.EnableAdBlock, s.AdBlockAllowlist,
        s.DnsProvider, s.CustomDohUrl, s.IsFakeDns, s.BlockQuic, s.BlockIpv6,
        s.EnableTunMode, s.EnableKillSwitch, s.EnableDnsLeakProtection,
        s.HttpPort, s.SocksPort, s.ShareLan,
        LanBindAddress = s.ShareLan ? s.LanBindAddress : "", LanAuthEnabled = s.ShareLan && s.LanAuthEnabled,
        LanUsername = s.ShareLan && s.LanAuthEnabled ? s.LanUsername : "", LanPassword = s.ShareLan && s.LanAuthEnabled ? s.LanPassword : "",
        s.BufferSizeKb, s.TcpCongestion,
        s.UpstreamProxyEnabled, UpstreamProxyHost = s.UpstreamProxyEnabled ? s.UpstreamProxyHost : "", UpstreamProxyPort = s.UpstreamProxyEnabled ? s.UpstreamProxyPort : 0,
        s.EnableTlsFragment, s.TlsFragmentLength, s.TlsFragmentInterval, s.TlsFragmentPackets,
        s.XhttpModeOverride, s.EnableMux, s.MuxConcurrency, s.PerAppProxyList, s.PerAppDirectList
        });
    }
}
