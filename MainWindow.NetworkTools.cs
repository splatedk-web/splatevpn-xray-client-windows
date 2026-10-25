using System.Net.NetworkInformation;
using System.Text.Json;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

namespace SplateClientDesktop;

public partial class MainWindow
{
    private System.Threading.CancellationTokenSource? _aiMonitorCts;
    private string? _reviewedDiagnostic;
    private string? _profileNetworkSignature;
    private DateTimeOffset _profileNetworkChangedAt;
    private bool _profileNetworkChecked;
    private void ExpireSiteRules()
    {
        if (!_viewModel.Settings.TemporaryRoutes.Any(r => r.ExpiresAt <= DateTimeOffset.UtcNow)) return;
        var clean = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_viewModel.Settings))!;
        clean.TemporaryRoutes.RemoveAll(r => r.ExpiresAt <= DateTimeOffset.UtcNow);
        _viewModel.CommitProfileSettings(clean); QueueNetworkApply(); SendSettingsToWeb();
    }
    private async Task ApplyBoundNetworkProfileAsync()
    {
        if (!_viewModel.Settings.ConnectionProfiles.Any(p => p.NetworkKey != null)) return;
        var network = PhysicalNetworkSignature();
        if (network != _profileNetworkSignature) { _profileNetworkSignature = network; _profileNetworkChangedAt = DateTimeOffset.UtcNow; _profileNetworkChecked = false; }
        if (_profileNetworkChecked || _viewModel.IsConnecting || DateTimeOffset.UtcNow - _profileNetworkChangedAt < TimeSpan.FromSeconds(3)) return;
        _profileNetworkChecked = true;
        try
        {
            var context = await NetworkProfileContext.CaptureAsync(_adFilterLifetime.Token);
            if (network != PhysicalNetworkSignature()) { _profileNetworkChecked = false; return; }
            var profile = _viewModel.Settings.ConnectionProfiles.FirstOrDefault(p => p.NetworkKey == context.Key);
            if (profile == null) return;
            var target = ConnectionProfiles.Target(_viewModel.Settings, profile.Snapshot);
            target.ConnectionProfileUndo = ConnectionProfiles.Capture(_viewModel.Settings);
            _aiMonitorCts?.Cancel(); _aiDnsCts?.Cancel();
            _viewModel.CommitProfileSettings(target); QueueNetworkApply(); SendSettingsToWeb();
            PostWebMessage("notification", new { title = "Сетевой профиль", message = "Применён профиль «" + profile.Name + "». Применение можно отменить в настройках.", kind = "info" });
        }
        catch (OperationCanceledException) when (_adFilterLifetime.IsCancellationRequested) { throw; }
        catch (Exception ex) { _viewModel.WriteDiagnosticLog("[PROFILE] Не удалось применить сетевой профиль: " + ex.Message); }
    }
    private static string PhysicalNetworkSignature() => string.Join(';', NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            !n.Name.Contains("Splate", StringComparison.OrdinalIgnoreCase) && !n.Name.Contains("tun", StringComparison.OrdinalIgnoreCase))
        .Select(n => n.Id + string.Join(',', n.GetIPProperties().UnicastAddresses.Select(a => a.Address.ToString()))).Order());
    private string? AiMonitorIdentity() => _viewModel.IsConnected && !_viewModel.IsConnecting && _aiDnsCts == null &&
        _viewModel.Settings.AiDnsAutoSwitch && _viewModel.ActiveAiMode == "dns_ai" && !_viewModel.HasPendingConnectionSettings
        ? _viewModel.ConnectionRevision + ":" + ConnectionSettings.Signature(_viewModel.Settings) + ":" + PhysicalNetworkSignature() : null;
    private async Task RunAiMonitorLoopAsync()
    {
        string? identity = null; var next = DateTimeOffset.MaxValue;
        try
        {
            while (!_adFilterLifetime.IsCancellationRequested)
            {
                await ApplyBoundNetworkProfileAsync();
                ExpireSiteRules();
                var current = AiMonitorIdentity();
                if (current != identity) { identity = current; next = DateTimeOffset.UtcNow.AddMinutes(5); }
                if (identity != null && DateTimeOffset.UtcNow >= next)
                {
                    using var cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(_adFilterLifetime.Token);
                    cts.CancelAfter(TimeSpan.FromMinutes(3)); _aiMonitorCts = cts;
                    try
                    {
                        var choice = new AiDnsChoice(_viewModel.Settings.AiDnsProvider, _viewModel.Settings.AiTlsFragment);
                        var selector = new AiDnsAutoSelector();
                        _viewModel.WriteDiagnosticLog("[SmartDNS] Фоновая проверка прямого SmartDNS.");
                        var check = AiDnsFailover.FindAsync(choice, (c, ct) => selector.ProbeAsync(c, ct), cts.Token);
                        while (!check.IsCompleted)
                        {
                            await Task.WhenAny(check, Task.Delay(1000, _adFilterLifetime.Token));
                            ExpireSiteRules();
                            if (AiMonitorIdentity() != identity) cts.Cancel();
                        }
                        var replacement = await check;
                        if (!cts.IsCancellationRequested && replacement != null && AiMonitorIdentity() == identity)
                        {
                            var target = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_viewModel.Settings))!;
                            target.AiDnsProvider = replacement.Choice.ProviderKey; target.AiTlsFragment = replacement.Choice.TlsFragment;
                            _viewModel.CommitProfileSettings(target);
                            _viewModel.WriteDiagnosticLog($"[SmartDNS] Подтверждённое переключение: {choice.ProviderKey} → {replacement.Choice.ProviderKey}.");
                            SendSettingsToWeb(); QueueNetworkApply();
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { _viewModel.WriteDiagnosticLog("[SmartDNS] Проверка не завершена: " + ex.GetType().Name); }
                    finally { _aiMonitorCts = null; identity = AiMonitorIdentity(); next = DateTimeOffset.UtcNow.AddMinutes(5); }
                }
                await Task.Delay(2000, _adFilterLifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
    }
    private void QueueNetworkApply()
    {
        if (!_viewModel.IsConnected && !_viewModel.IsConnecting) return;
        ++_settingsApplyVersion;
        if (_settingsApplyTask == null || _settingsApplyTask.IsCompleted)
            _settingsApplyTask = RunWebOperationAsync("applyAiRoute", ApplyConnectionSettingsAsync);
    }
    private void SendProfiles() => PostWebMessage("connectionProfiles", new {
        profiles = _viewModel.Settings.ConnectionProfiles.Select(p => new { p.Id, p.Name, p.NetworkLabel }),
        canUndo = _viewModel.Settings.ConnectionProfileUndo != null,
        recoveryNotice = _viewModel.SettingsRecoveryNotice });
    private bool HandleNetworkTool(string? action, JsonElement root)
    {
        if (action is not ("bindConnectionProfile" or "unbindConnectionProfile" or "rollbackGeoAssets" or "setPersonalFolder" or "subscriptionHistory" or "cancelSubscriptionUpdate" or "testDns" or "saveSiteRule" or "removeSiteRule" or "previewSiteRule" or "importUpstreamProxy" or "checkUpstreamProxy" or "profiles" or "saveConnectionProfile" or "deleteConnectionProfile" or "previewConnectionProfile" or "applyConnectionProfile" or "undoConnectionProfile" or "extendedDiagnostic" or "copyExtendedDiagnostic")) return false;
        var data = root.Clone();
        _ = RunWebOperationAsync("networkTools", async () =>
        {
            if (action is "bindConnectionProfile" or "unbindConnectionProfile")
            {
                var id = data.GetProperty("id").GetString();
                var context = action == "bindConnectionProfile" ? await NetworkProfileContext.CaptureAsync(_adFilterLifetime.Token) : null;
                var target = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_viewModel.Settings))!;
                var index = target.ConnectionProfiles.FindIndex(p => p.Id == id);
                if (index < 0) throw new ArgumentException("Профиль не найден.");
                if (context != null)
                    target.ConnectionProfiles = target.ConnectionProfiles.Select(p => p.NetworkKey == context.Key ? p with { NetworkKey = null, NetworkLabel = null } : p).ToList();
                target.ConnectionProfiles[index] = target.ConnectionProfiles[index] with { NetworkKey = context?.Key, NetworkLabel = context?.Label };
                _viewModel.CommitProfileSettings(target); _profileNetworkSignature = PhysicalNetworkSignature(); _profileNetworkChecked = true; SendProfiles(); return;
            }
            if (action == "rollbackGeoAssets")
            {
                var ok = await GeoAssetUpdaterService.RollbackAsync(System.IO.Path.Combine(AppContext.BaseDirectory, "core"), _viewModel.WriteDiagnosticLog, _adFilterLifetime.Token);
                PostWebMessage("notification", new { title = "Геобазы", message = ok ? "Предыдущая версия восстановлена. Применится при следующем подключении." : "Не удалось восстановить предыдущую версию. Подробности в журнале.", kind = ok ? "info" : "warning" }); return;
            }
            if (action == "cancelSubscriptionUpdate") { _viewModel.CancelSubscriptionUpdate(data.GetProperty("id").GetString() ?? ""); return; }
            if (action == "setPersonalFolder") { _viewModel.SetPersonalServersEnabled(data.GetProperty("enabled").GetBoolean()); SendSettingsToWeb(); SendServersToWeb(); return; }
            if (action == "subscriptionHistory") { var id = data.GetProperty("id").GetString(); PostWebMessage("subscriptionHistory", (_viewModel.Settings.SubscriptionUpdateHistory ?? new()).Where(r => r.SubscriptionId == id).Reverse().ToArray()); return; }
            if (action == "extendedDiagnostic")
            { _reviewedDiagnostic = await _viewModel.GetExtendedDiagnosticAsync(); PostWebMessage("extendedDiagnostic", new { message = _reviewedDiagnostic }); return; }
            if (action == "copyExtendedDiagnostic")
            { if (_reviewedDiagnostic != null) System.Windows.Clipboard.SetText(_reviewedDiagnostic); return; }
            var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_viewModel.Settings))!;
            if (action == "testDns")
            {
                string message;
                try { message = await DnsDiagnosticService.RunAsync(settings, data.GetProperty("domain").GetString() ?? "", _viewModel.IsConnected ? _viewModel.ActiveHttpPort : null, _adFilterLifetime.Token); }
                catch (Exception ex) when (ex is not OperationCanceledException || !_adFilterLifetime.IsCancellationRequested) { message = "Проверка DNS не завершена: " + ex.Message; }
                PostWebMessage("dnsDiagnostic", new { message }); return;
            }
            if (action == "previewSiteRule")
            { PostWebMessage("siteRulePreview", new { message = SiteRules.Preview(settings, data.GetProperty("domain").GetString() ?? "") }); return; }
            if (action is "saveSiteRule" or "removeSiteRule")
            {
                var domain = SiteRules.Domain(data.GetProperty("domain").GetString() ?? "");
                settings.TemporaryRoutes.RemoveAll(r => r.Domain == domain);
                if (action == "saveSiteRule")
                {
                    var route = data.GetProperty("route").GetString() ?? "";
                    if (route is not ("proxy" or "direct" or "block")) throw new ArgumentException("Неизвестный маршрут.");
                    var minutes = data.GetProperty("minutes").GetInt32();
                    if (minutes == 0)
                    {
                        var rule = "domain:" + domain;
                        string Remove(string text) => string.Join('\n', (text ?? "").Split('\n').Where(line => line.Trim() != rule && line.Trim() != domain));
                        settings.CustomProxyRules = Remove(settings.CustomProxyRules);
                        settings.CustomDirectRules = Remove(settings.CustomDirectRules);
                        settings.CustomBlockRules = Remove(settings.CustomBlockRules);
                        if (route == "proxy") settings.CustomProxyRules += "\n" + rule;
                        if (route == "direct") settings.CustomDirectRules += "\n" + rule;
                        if (route == "block") settings.CustomBlockRules += "\n" + rule;
                    }
                    else
                    {
                        if (minutes is < 1 or > 1440 || settings.TemporaryRoutes.Count >= 100) throw new ArgumentException("До 100 временных правил, срок — до 24 часов.");
                        settings.TemporaryRoutes.Add(new(domain, route, DateTimeOffset.UtcNow.AddMinutes(minutes)));
                    }
                }
                _viewModel.CommitProfileSettings(settings); QueueNetworkApply(); SendSettingsToWeb(); return;
            }
            if (action == "importUpstreamProxy")
            {
                var endpoint = SystemProxyManager.OriginalHttpProxy();
                settings.UpstreamProxyHost = endpoint.Host; settings.UpstreamProxyPort = endpoint.Port;
                // Import only fills the form; enabling remains an explicit user choice.
                _viewModel.CommitProfileSettings(settings); SendSettingsToWeb(); return;
            }
            if (action == "checkUpstreamProxy")
            {
                var endpoint = UpstreamProxy.Validate(settings.UpstreamProxyHost, settings.UpstreamProxyPort);
                var node = _viewModel.Servers.FirstOrDefault(n => n.Id == _viewModel.SelectedServer?.Id) ?? _viewModel.Servers.FirstOrDefault() ?? throw new ArgumentException("Добавьте VPN-сервер для проверки CONNECT.");
                UpstreamProxy.ValidateTransport(node);
                await UpstreamProxy.CheckConnectAsync(endpoint, node.Address, node.Port, _adFilterLifetime.Token);
                PostWebMessage("notification", new { title = "Внешний HTTP-прокси", message = "CONNECT к VPN-серверу разрешён. Это проверка прокси; работоспособность VPN проверяется после подключения.", kind = "info" }); return;
            }
            string Id() => data.GetProperty("id").GetString() ?? "";
            string Snapshot() => settings.ConnectionProfiles.FirstOrDefault(p => p.Id == Id())?.Snapshot ?? ConnectionProfiles.BuiltIn(settings, Id());
            if (action == "saveConnectionProfile") ConnectionProfiles.Save(settings, data.GetProperty("name").GetString() ?? "");
            if (action == "deleteConnectionProfile")
            {
                if (await _viewModel.ConfirmationRequested!("Удалить профиль", "Удалить сохранённый профиль подключения?"))
                {
                    // A background SmartDNS switch may finish while the confirmation is open.
                    settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_viewModel.Settings))!;
                    settings.ConnectionProfiles.RemoveAll(p => p.Id == Id());
                }
                else return;
            }
            if (action == "previewConnectionProfile")
            {
                var target = ConnectionProfiles.Target(settings, Snapshot());
                PostWebMessage("connectionProfilePreview", new { id = Id(), snapshot = ConnectionProfiles.Capture(target),
                    message = $"Маршрут: {target.RoutingMode}; нейросети: {target.AiRoutingMode}\nDNS: {target.DnsProvider}; SmartDNS: {target.AiDnsProvider}\nTUN: {target.EnableTunMode}; sniffing: {target.EnableSniffing}\nHTTP/SOCKS: {target.HttpPort}/{target.SocksPort}\nИзменятся сетевые настройки, правила и LAN. Активный VPN переподключится." }); return;
            }
            if (action is "applyConnectionProfile" or "undoConnectionProfile")
            {
                var snapshot = action == "undoConnectionProfile" ? settings.ConnectionProfileUndo ?? throw new ArgumentException("Нет изменений для отката.") : Snapshot();
                var target = ConnectionProfiles.Target(settings, snapshot);
                if (action == "applyConnectionProfile" && data.GetProperty("snapshot").GetString() != ConnectionProfiles.Capture(target)) throw new ArgumentException("Профиль изменился. Откройте предпросмотр снова.");
                target.ConnectionProfileUndo = action == "undoConnectionProfile" ? null : ConnectionProfiles.Capture(settings);
                _aiDnsCts?.Cancel(); _aiMonitorCts?.Cancel();
                _viewModel.CommitProfileSettings(target); QueueNetworkApply(); SendSettingsToWeb(); SendProfiles(); return;
            }
            if (action != "profiles") _viewModel.CommitProfileSettings(settings);
            SendProfiles();
        });
        return true;
    }
}
