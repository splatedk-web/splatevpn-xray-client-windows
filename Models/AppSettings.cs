namespace SplateClientDesktop.Models;

using System.Collections.Generic;

public class AppSettings
{
    public string RoutingMode { get; set; } = "smart_ru"; // smart_ru, global
    public string DnsProvider { get; set; } = "cloudflare"; // cloudflare, google, quad9, adguard, yandex, xbox
    public bool EnableSniffing { get; set; } = true;
    public bool RouteOnlySniffing { get; set; } = true;
    public bool BlockQuic { get; set; } = true;
    public bool BlockIpv6 { get; set; } = true;
    public bool IsFakeDns { get; set; } = false;
    public bool EnableTlsFragment { get; set; } = false;
    public string XhttpModeOverride { get; set; } = "auto";
    public string PingMode { get; set; } = "tcp"; // tcp, http
    public string SubAutoUpdateInterval { get; set; } = "24h"; // 6h, 12h, 24h, manual
    public string LastSubUpdateTime { get; set; } = string.Empty;
    public int SocksPort { get; set; } = 10808;
    public int HttpPort { get; set; } = 10809;
    public int BufferSizeKb { get; set; } = 4096;
    public string TcpCongestion { get; set; } = "bbr";
    public string? SubscriptionUrl { get; set; }
    public bool AutoStart { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public SubscriptionTier SubscriptionTier { get; set; } = SubscriptionTier.Free;
    public List<RoutingProfile> RoutingProfiles { get; set; } = new();
    public string ActiveRoutingProfile { get; set; } = string.Empty;
    public bool AutoUpdateSubscription { get; set; } = false;
    public bool EnableAiBypass { get; set; } = true;
    public long SubscriptionDataLimit { get; set; } = 0;
    public long SubscriptionDataUsed { get; set; } = 0;
    public string SubscriptionExpiry { get; set; } = string.Empty;
    public string CustomProxyRules { get; set; } = string.Empty;
    public string CustomDirectRules { get; set; } = string.Empty;
    
    // --- Advanced Features ---
    public bool EnableTunMode { get; set; } = false;
    public bool EnableKillSwitch { get; set; } = false;
    public bool EnableDnsLeakProtection { get; set; } = false;
    public bool AutoFallbackFastestServer { get; set; } = false;
    public string SplitTunnelingMode { get; set; } = "proxy_selected"; // proxy_selected, bypass_selected
    public string PerAppProxyList { get; set; } = string.Empty;
    public string PerAppDirectList { get; set; } = string.Empty;
    public bool EnableGlobalHotkey { get; set; } = true;
    public string SelectedSubscriptionGroup { get; set; } = "Все";
}