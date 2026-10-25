using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

namespace SplateClientDesktop.ViewModels;

public partial class MainViewModel
{
    public void WriteDiagnosticLog(string message) => AppendLog(message);
    public int ConnectionRevision => _connectionEpoch;
    public string? SettingsRecoveryNotice => (_storage as ServerStorage)?.SettingsRecoveryNotice;
    public void CommitProfileSettings(AppSettings target)
    {
        SettingsCompatibility.Normalize(target);
        // Persist once before publishing. A write failure leaves both UI and saved settings intact.
        _storage.SaveSettings(target);
        Settings = target;
        // Publish a complete snapshot without invoking setters that each persist intermediate state.
#pragma warning disable MVVMTK0034
        _selectedRoutingMode = target.RoutingMode; _selectedDnsProvider = target.DnsProvider;
        _isFakeDns = target.IsFakeDns; _enableTlsFragment = target.EnableTlsFragment;
        _xhttpModeOverride = target.XhttpModeOverride; _routeOnlySniffing = target.RouteOnlySniffing;
        _enableSniffing = target.EnableSniffing; _blockQuic = target.BlockQuic; _blockIpv6 = target.BlockIpv6;
        _enableAiBypass = target.EnableAiBypass; _customProxyRules = target.CustomProxyRules;
        _customDirectRules = target.CustomDirectRules; _enableTunMode = target.EnableTunMode;
#pragma warning restore MVVMTK0034
        foreach (var property in new[] { nameof(Settings), nameof(SelectedRoutingMode), nameof(SelectedDnsProvider), nameof(IsFakeDns),
            nameof(EnableTlsFragment), nameof(XhttpModeOverride), nameof(RouteOnlySniffing), nameof(EnableSniffing), nameof(BlockQuic),
            nameof(BlockIpv6), nameof(EnableAiBypass), nameof(CustomProxyRules), nameof(CustomDirectRules), nameof(EnableTunMode), nameof(IsSmartRouting), nameof(IsGlobalRouting) }) OnPropertyChanged(property);
    }
    public async Task<string> GetExtendedDiagnosticAsync()
    {
        var secrets = Servers.SelectMany(s => new[] { s.Address, s.DisplayName, s.RawLink, s.Uuid, s.Password, s.PublicKey, s.ShortId, s.Sni, s.Host, s.Path })
            .Concat(Subscriptions.Select(s => s.Url)).Append(Settings.LanPassword).Append(Settings.LanUsername).Append(Settings.UpstreamProxyHost).Append(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return DiagnosticRedactor.Redact(await GetConnectionDiagnosticAsync() +
            $"\nВнешний HTTP-прокси: {Settings.UpstreamProxyEnabled}\nSmartDNS: {ActiveAiMode}; провайдер: {ActiveAiDnsLabel}; автопереключение: {Settings.AiDnsAutoSwitch}" +
            $"\nБалансировщик: {Settings.BalancerMode}; интервал: {Settings.BalancerHealthSeconds} с; подтверждений: {Settings.BalancerFailureConfirmations}" +
            "\n\nПоследние строки журнала (секреты и адреса скрыты):\n" + string.Join('\n', Logs.Split('\n').TakeLast(80)), secrets);
    }
}
