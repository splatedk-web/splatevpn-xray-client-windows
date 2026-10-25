using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public static class SettingsCompatibility
{
    public static bool Normalize(AppSettings settings)
    {
        LanSharing.Validate(settings);
        var changed = false;
        if (settings.TemporaryRoutes == null) { settings.TemporaryRoutes = new(); changed = true; }
        if (settings.TemporaryRoutes.Count > 100 || settings.TemporaryRoutes.Any(r => r == null || r.Route is not ("proxy" or "direct" or "block") || SiteRules.Domain(r.Domain) != r.Domain))
            throw new ArgumentException("Повреждены временные правила сайтов.");
        changed |= settings.TemporaryRoutes.RemoveAll(r => r.ExpiresAt <= DateTimeOffset.UtcNow) > 0;
        if (!RoutingRegions.IsValid(settings.RoutingRegion)) { settings.RoutingRegion = "ru"; changed = true; }
        if (settings.UpstreamProxyEnabled)
        {
            var proxy = UpstreamProxy.Validate(settings.UpstreamProxyHost, settings.UpstreamProxyPort);
            if (proxy.Host != settings.UpstreamProxyHost) { settings.UpstreamProxyHost = proxy.Host; changed = true; }
            if (settings.RoutingMode != "global") { settings.RoutingMode = "global"; changed = true; }
        }
        settings.SubscriptionUpdateHistory ??= new();
        changed |= settings.SubscriptionUpdateHistory.RemoveAll(r => r == null) > 0;
        if (settings.SubscriptionUpdateHistory.Count > 100) { settings.SubscriptionUpdateHistory = settings.SubscriptionUpdateHistory.TakeLast(100).ToList(); changed = true; }
        if (settings.ConnectionProfiles == null) { settings.ConnectionProfiles = new(); changed = true; }
        if (settings.ConnectionProfiles.Count > 8 || settings.ConnectionProfiles.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id) || p.Name == null || p.Name.Trim().Length is < 1 or > 48 || p.Snapshot == null || p.Snapshot.Length > 256_000 || p.NetworkKey?.Length > 256 || p.NetworkLabel?.Length > 256) || settings.ConnectionProfiles.Select(p => p.Id).Distinct().Count() != settings.ConnectionProfiles.Count)
            throw new ArgumentException("Повреждён список профилей подключения.");
        if (settings.SettingsSchemaVersion < 2)
        {
            // Preserve the effective old route while removing the obsolete Xbox endpoint.
            settings.AiRoutingMode = settings.EnableAiXboxDns ? "dns_ai" :
                !settings.EnableAiBypass ? "off" : settings.EnableAiDns && settings.AiRoutingMode == "dns_ai" ? "dns_ai" : "vpn";
            settings.SettingsSchemaVersion = 2;
            changed = true;
        }
        if (settings.AiRoutingMode is not ("off" or "vpn" or "dns_ai" or "dns_vpn")) { settings.AiRoutingMode = "vpn"; changed = true; }
        if (settings.RoutingMode == "global" && settings.AiRoutingMode is not ("vpn" or "dns_vpn"))
        { settings.AiRoutingMode = "vpn"; changed = true; }
        var aiFlags = (settings.EnableAiBypass, settings.EnableAiDns, settings.EnableAiXboxDns);
        settings.EnableAiBypass = settings.AiRoutingMode != "off";
        settings.EnableAiDns = settings.AiRoutingMode is "dns_ai" or "dns_vpn";
        settings.EnableAiXboxDns = false;
        changed |= aiFlags != (settings.EnableAiBypass, settings.EnableAiDns, settings.EnableAiXboxDns);
        if (settings.DnsProvider == "xbox") { settings.DnsProvider = "cloudflare"; changed = true; }
        if (SmartDnsProviders.Find(settings.AiDnsProvider) == null) { settings.AiDnsProvider = "dns_ai"; changed = true; }
        try { var doh = XrayConfigGenerator.ValidateDoh(settings.CustomDohUrl); if (settings.CustomDohUrl != doh) { settings.CustomDohUrl = doh; changed = true; } } catch (ArgumentException) { settings.CustomDohUrl = "https://dns.google/dns-query"; changed = true; }
        try { var allow = string.Join('\n', AdFilterService.Allowlist(settings.AdBlockAllowlist)); if (allow != settings.AdBlockAllowlist) { settings.AdBlockAllowlist = allow; changed = true; } }
        catch (ArgumentException) { settings.AdBlockAllowlist = ""; changed = true; }
        if (!double.IsFinite(settings.GlassTransparency) || settings.GlassTransparency is < 0 or > 1) { settings.GlassTransparency = 0; changed = true; }
        if (settings.BalancerMode is not ("balanced" or "failover" or "initial_only")) { settings.BalancerMode = "balanced"; changed = true; }
        var before = (settings.BalancerHealthSeconds, settings.BalancerFailureConfirmations, settings.BalancerOptimizationMinutes, settings.BalancerCooldownSeconds, settings.BalancerImprovementPercent);
        settings.BalancerHealthSeconds = Math.Clamp(settings.BalancerHealthSeconds, 2, 30);
        settings.BalancerFailureConfirmations = Math.Clamp(settings.BalancerFailureConfirmations, 2, 5);
        settings.BalancerOptimizationMinutes = Math.Clamp(settings.BalancerOptimizationMinutes, 5, 60);
        settings.BalancerCooldownSeconds = Math.Clamp(settings.BalancerCooldownSeconds, 60, 1800);
        settings.BalancerImprovementPercent = Math.Clamp(settings.BalancerImprovementPercent, 30, 80);
        changed |= before != (settings.BalancerHealthSeconds, settings.BalancerFailureConfirmations, settings.BalancerOptimizationMinutes, settings.BalancerCooldownSeconds, settings.BalancerImprovementPercent);
        if (settings.IsFakeDns && (!settings.EnableTunMode || !settings.EnableSniffing))
        {
            settings.IsFakeDns = false;
            changed = true;
        }
        if (settings.EnableKillSwitch || settings.EnableDnsLeakProtection)
        {
            settings.EnableKillSwitch = false;
            settings.EnableDnsLeakProtection = false;
            changed = true;
        }
        if (settings.SettingsSchemaVersion < 1) { settings.SettingsSchemaVersion = 1; changed = true; }
        return changed;
    }
}
