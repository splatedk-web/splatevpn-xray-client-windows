using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public static class SettingsCompatibility
{
    public static bool Normalize(AppSettings settings)
    {
        var changed = false;
        if (settings.SettingsSchemaVersion < 2)
        {
            // Preserve the effective old route while removing the obsolete Xbox endpoint.
            settings.AiRoutingMode = settings.EnableAiXboxDns ? "dns_ai" :
                !settings.EnableAiBypass ? "off" : settings.EnableAiDns && settings.AiRoutingMode == "dns_ai" ? "dns_ai" : "vpn";
            settings.SettingsSchemaVersion = 2;
            changed = true;
        }
        if (settings.AiRoutingMode is not ("off" or "vpn" or "dns_ai")) { settings.AiRoutingMode = "vpn"; changed = true; }
        var aiFlags = (settings.EnableAiBypass, settings.EnableAiDns, settings.EnableAiXboxDns, settings.EnableSniffing);
        settings.EnableAiBypass = settings.AiRoutingMode != "off";
        settings.EnableAiDns = settings.AiRoutingMode == "dns_ai";
        settings.EnableAiXboxDns = false;
        if (settings.EnableAiBypass) settings.EnableSniffing = true;
        changed |= aiFlags != (settings.EnableAiBypass, settings.EnableAiDns, settings.EnableAiXboxDns, settings.EnableSniffing);
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
