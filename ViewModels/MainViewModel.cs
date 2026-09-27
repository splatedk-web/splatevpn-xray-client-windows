using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;
using SplateClientDesktop.Views;

namespace SplateClientDesktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServerStorage _storage;
    private readonly IXrayProcessManager _xrayManager;
    private readonly XrayConfigGenerator _configGen;
    private readonly IConfigExportImportService _configService;
    private readonly ITrafficMonitorService _trafficMonitor;
    private readonly TunDeviceManager _tunDeviceManager = new();
    private readonly QrCodeScannerService _qrScanner = new();
    private DispatcherTimer? _sessionTimer;
    private DateTime _sessionStartTime;

    // --- Connection state ---
    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "ОТКЛЮЧЕНО";

    [ObservableProperty]
    private string _connectionDetail = "Нажмите для создания защищённого туннеля";

    // --- Egress IP & Geo Details ---
    [ObservableProperty]
    private string _egressIp = "Не определен";

    [ObservableProperty]
    private string _countryName = "Отключено";

    [ObservableProperty]
    private string _countryFlag = "🌐";

    [ObservableProperty]
    private string _egressLocation = "";

    [ObservableProperty]
    private bool _isDetectingIp;

    [ObservableProperty]
    private long _connectionRtt = -1;

    partial void OnConnectionRttChanged(long value) => OnPropertyChanged(nameof(ConnectionRttDisplay));

    public string ConnectionRttDisplay => ConnectionRtt >= 0 ? $"{ConnectionRtt} ms" : (IsConnected ? "..." : "—");

    // --- Server Search & Display ---
    [ObservableProperty]
    private string _serverSearchQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ServerNode> _displayServers = new();

    // --- Servers ---
    [ObservableProperty]
    private ObservableCollection<ServerNode> _servers = new();

    partial void OnServersChanged(ObservableCollection<ServerNode> value)
    {
        ApplyServerFilters();
    }

    private bool _isSwitchingServer = false;
    private bool _isUpdatingSubscription = false;

    [ObservableProperty]
    private ServerNode? _selectedServer;

    partial void OnSelectedServerChanged(ServerNode? oldValue, ServerNode? newValue)
    {
        foreach (var s in Servers)
        {
            s.IsSelected = (s == newValue);
            s.IsCurrentlyActive = IsConnected && (s == newValue);
        }
        if (newValue != null)
        {
            _storage.SaveServers(Servers.ToList());
        }

        if (IsConnected && oldValue != null && newValue != null && oldValue != newValue && !_isSwitchingServer && !_isPinging && !_isUpdatingSubscription)
        {
            _ = SwitchServerAsync(newValue);
        }
    }

    public async Task SwitchServerAsync(ServerNode newNode)
    {
        if (_isSwitchingServer) return;
        _isSwitchingServer = true;
        try
        {
            AppendLog($"[CLIENT] Быстрое переключение на «{newNode.DisplayName}»...");
            Disconnect();
            await Task.Delay(250);
            await ConnectAsync();
        }
        finally
        {
            _isSwitchingServer = false;
        }
    }

    // --- Logs ---
    [ObservableProperty]
    private string _logs = string.Empty;

    // --- Traffic speed ---
    [ObservableProperty]
    private string _downloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _uploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _sessionDownloaded = "0 B";

    [ObservableProperty]
    private string _sessionUploaded = "0 B";

    // --- Traffic Speed Graph Metrics (0.0 to 1.0 relative ratios) ---
    [ObservableProperty]
    private double _downloadSpeedRatio = 0.0;

    [ObservableProperty]
    private double _uploadSpeedRatio = 0.0;

    [ObservableProperty]
    private ObservableCollection<double> _downloadHistory = new(new double[20]);

    [ObservableProperty]
    private ObservableCollection<double> _uploadHistory = new(new double[20]);

    [ObservableProperty]
    private System.Windows.Media.PointCollection _liveDownloadPoints = new();

    [ObservableProperty]
    private System.Windows.Media.PointCollection _liveDownloadAreaPoints = new();

    [ObservableProperty]
    private System.Windows.Media.PointCollection _liveUploadPoints = new();

    public bool IsSmartRouting => SelectedRoutingMode == "smart_ru";
    public bool IsGlobalRouting => SelectedRoutingMode == "global";

    // --- Session timer ---
    [ObservableProperty]
    private string _sessionDuration = "00:00:00";

    // --- Subscription ---
    [ObservableProperty]
    private ObservableCollection<SubscriptionItem> _subscriptions = new();

    [ObservableProperty]
    private SubscriptionItem? _selectedSubscription;

    [ObservableProperty]
    private string _subscriptionUrl = string.Empty;

    [ObservableProperty]
    private bool _isLoadingSubscription;

    [ObservableProperty]
    private string _subscriptionStatus = string.Empty;

    [ObservableProperty]
    private bool _isSubscriptionUrlVisible;

    [ObservableProperty]
    private bool _autoUpdateSubscription;

    [ObservableProperty]
    private string _subscriptionLimits = string.Empty;

    // --- App Updates ---
    [ObservableProperty]
    private bool _isAppUpdateAvailable;

    [ObservableProperty]
    private string _appUpdateUrl = string.Empty;

    [ObservableProperty]
    private string _appUpdateMessage = string.Empty;

    [ObservableProperty]
    private double _subscriptionUsagePercentage;

    public event Action? RequestNavigateToServers;

    [RelayCommand]
    private void NavigateToServers()
    {
        RequestNavigateToServers?.Invoke();
    }

    // --- Manual server add ---
    [ObservableProperty]
    private string _manualServerLink = string.Empty;

    // --- Ping ---
    [ObservableProperty]
    private bool _isPinging;

    // --- Settings page bindings ---
    [ObservableProperty]
    private string _selectedRoutingMode = "smart_ru";

    partial void OnSelectedRoutingModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSmartRouting));
        OnPropertyChanged(nameof(IsGlobalRouting));
        Settings.RoutingMode = value;
        _storage.SaveSettings(Settings);
    }

    [RelayCommand]
    private void SetRoutingMode(string mode)
    {
        SelectedRoutingMode = mode;
    }

    partial void OnEnableTunModeChanged(bool value)
    {
        Settings.EnableTunMode = value;
        _storage.SaveSettings(Settings);
    }

    partial void OnEnableAiBypassChanged(bool value)
    {
        Settings.EnableAiBypass = value;
        _storage.SaveSettings(Settings);
    }

    partial void OnEnableKillSwitchChanged(bool value)
    {
        Settings.EnableKillSwitch = value;
        _storage.SaveSettings(Settings);
    }

    partial void OnAutoFallbackFastestServerChanged(bool value)
    {
        Settings.AutoFallbackFastestServer = value;
        _storage.SaveSettings(Settings);
    }

    partial void OnServerSearchQueryChanged(string value) => ApplyServerFilters();
    partial void OnSelectedSubscriptionGroupChanged(string value) => ApplyServerFilters();

    public void ApplyServerFilters()
    {
        var query = ServerSearchQuery?.Trim().ToLowerInvariant() ?? "";
        var group = SelectedSubscriptionGroup ?? "Все";

        var filtered = Servers.Where(s =>
        {
            if (group != "Все" && !string.Equals(s.SubscriptionGroup, group, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(query))
            {
                var matchName = s.DisplayName.ToLowerInvariant().Contains(query);
                var matchAddr = s.Address.ToLowerInvariant().Contains(query);
                var matchProto = s.Protocol.ToLowerInvariant().Contains(query);
                if (!matchName && !matchAddr && !matchProto) return false;
            }
            return true;
        }).ToList();

        DisplayServers = new ObservableCollection<ServerNode>(filtered);
    }

    [ObservableProperty]
    private string _selectedDnsProvider = "cloudflare";

    partial void OnSelectedDnsProviderChanged(string value)
    {
        Settings.DnsProvider = value;
        _storage.SaveSettings(Settings);
    }

    [ObservableProperty]
    private bool _isFakeDns;

    partial void OnIsFakeDnsChanged(bool value)
    {
        Settings.IsFakeDns = value;
        _storage.SaveSettings(Settings);
    }

    [ObservableProperty]
    private bool _enableTlsFragment;

    partial void OnEnableTlsFragmentChanged(bool value)
    {
        Settings.EnableTlsFragment = value;
        _storage.SaveSettings(Settings);
    }

    [ObservableProperty]
    private string _xhttpModeOverride = "auto";

    partial void OnXhttpModeOverrideChanged(string value)
    {
        Settings.XhttpModeOverride = value;
        _storage.SaveSettings(Settings);
    }

    [ObservableProperty]
    private string _pingMode = "tcp";

    partial void OnPingModeChanged(string value)
    {
        Settings.PingMode = value;
        _storage.SaveSettings(Settings);
    }

    [ObservableProperty]
    private string _subAutoUpdateInterval = "24h";

    partial void OnSubAutoUpdateIntervalChanged(string value)
    {
        Settings.SubAutoUpdateInterval = value;
        _storage.SaveSettings(Settings);
    }

    [ObservableProperty]
    private string _splitTunnelingMode = "proxy_selected";

    partial void OnSplitTunnelingModeChanged(string value)
    {
        Settings.SplitTunnelingMode = value;
        _storage.SaveSettings(Settings);
    }

    [ObservableProperty]
    private bool _routeOnlySniffing = true;

    partial void OnRouteOnlySniffingChanged(bool value)
    {
        Settings.RouteOnlySniffing = value;
        _storage.SaveSettings(Settings);
    }

    [ObservableProperty]
    private bool _enableSniffing = true;

    partial void OnEnableSniffingChanged(bool value)
    {
        Settings.EnableSniffing = value;
        _storage.SaveSettings(Settings);
    }

    [ObservableProperty]
    private bool _blockQuic = true;

    partial void OnBlockQuicChanged(bool value)
    {
        Settings.BlockQuic = value;
        _storage.SaveSettings(Settings);
    }

    [ObservableProperty]
    private bool _blockIpv6 = true;

    partial void OnBlockIpv6Changed(bool value)
    {
        Settings.BlockIpv6 = value;
        _storage.SaveSettings(Settings);
    }

    [ObservableProperty]
    private bool _autoStart;

    partial void OnAutoStartChanged(bool value)
    {
        Settings.AutoStart = value;
        _storage.SaveSettings(Settings);
        WindowsIntegrationService.SetAutoStart(value);
    }

    [ObservableProperty]
    private bool _minimizeToTray = true;

    partial void OnMinimizeToTrayChanged(bool value)
    {
        Settings.MinimizeToTray = value;
        _storage.SaveSettings(Settings);
    }

    [ObservableProperty]
    private bool _enableAiBypass = true;

    [ObservableProperty]
    private string _customProxyRules = string.Empty;

    [ObservableProperty]
    private string _customDirectRules = string.Empty;

    [ObservableProperty]
    private bool _useHttpPing = false;

    // --- Advanced Features Properties ---
    [ObservableProperty]
    private bool _enableTunMode = false;

    [ObservableProperty]
    private bool _enableKillSwitch = false;

    [ObservableProperty]
    private bool _enableDnsLeakProtection = false;

    [ObservableProperty]
    private bool _autoFallbackFastestServer = false;

    [ObservableProperty]
    private string _perAppProxyList = string.Empty;

    [ObservableProperty]
    private string _perAppDirectList = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _subscriptionGroups = new() { "Все" };

    [ObservableProperty]
    private string _selectedSubscriptionGroup = "Все";

    [ObservableProperty]
    private bool _isUpdatingGeoAssets = false;

    // --- Navigation ---
    [ObservableProperty]
    private int _selectedTabIndex;

    public AppSettings Settings { get; private set; } = new();

    // Event to notify the window about tray tooltip updates
    public event Action<string>? TrayTooltipChanged;

    // Event to notify the window/tray to show a notification popup
    public event Action<string, string>? NotificationRequested;

    public MainViewModel(
        IServerStorage storage,
        IXrayProcessManager xrayManager,
        IConfigExportImportService configService,
        ITrafficMonitorService trafficMonitor)
    {
        _storage = storage;
        _xrayManager = xrayManager;
        _configGen = new XrayConfigGenerator();
        _configService = configService;
        _trafficMonitor = trafficMonitor;

        _xrayManager.OnLogMessage += AppendLog;
        _xrayManager.OnProcessExited += OnProcessExited;

        _trafficMonitor.SpeedUpdated += (download, upload) =>
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                DownloadSpeed = download;
                UploadSpeed = upload;
                TrayTooltipChanged?.Invoke(IsConnected
                    ? $"SplateVPN · ↓{download} ↑{upload}"
                    : "SplateVPN · Отключено");
            });
        };
        _trafficMonitor.BytesPerSecUpdated += (downBytes, upBytes) =>
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                // Max scale 10 MB/s for relative bar/gauge display
                const double maxSpeed = 10.0 * 1024 * 1024;
                DownloadSpeedRatio = Math.Clamp(downBytes / maxSpeed, 0.05, 1.0);
                UploadSpeedRatio = Math.Clamp(upBytes / maxSpeed, 0.05, 1.0);

                if (DownloadHistory.Count >= 24) DownloadHistory.RemoveAt(0);
                DownloadHistory.Add(DownloadSpeedRatio);

                if (UploadHistory.Count >= 24) UploadHistory.RemoveAt(0);
                UploadHistory.Add(UploadSpeedRatio);

                // Generate smooth live waveform points with normalized 0..100 coordinates
                var dPts = new System.Windows.Media.PointCollection();
                var dArea = new System.Windows.Media.PointCollection();
                var uPts = new System.Windows.Media.PointCollection();
                double width = 100.0;
                double height = 40.0;
                double step = width / Math.Max(1, DownloadHistory.Count - 1);

                dArea.Add(new System.Windows.Point(0, height));
                for (int i = 0; i < DownloadHistory.Count; i++)
                {
                    double x = i * step;
                    double yDown = Math.Clamp(height - (DownloadHistory[i] * (height - 6)), 2.0, height);
                    dPts.Add(new System.Windows.Point(x, yDown));
                    dArea.Add(new System.Windows.Point(x, yDown));

                    double yUp = Math.Clamp(height - (UploadHistory[i] * (height - 6)), 2.0, height);
                    uPts.Add(new System.Windows.Point(x, yUp));
                }
                dArea.Add(new System.Windows.Point(width, height));

                LiveDownloadPoints = dPts;
                LiveDownloadAreaPoints = dArea;
                LiveUploadPoints = uPts;
            });
        };
        _trafficMonitor.SessionBytesUpdated += (downloaded, uploaded) =>
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                SessionDownloaded = FormatBytes(downloaded);
                SessionUploaded = FormatBytes(uploaded);
            });
        };

        LoadData();
    }

    private void LoadData()
    {
        Settings = _storage.LoadSettings();
        var nodes = _storage.LoadServers();
        Servers = new ObservableCollection<ServerNode>(nodes);
        SelectedServer = Servers.FirstOrDefault(s => s.IsSelected) ?? Servers.FirstOrDefault();

        // Sync settings to UI properties
        SelectedRoutingMode = Settings.RoutingMode;
        SelectedDnsProvider = Settings.DnsProvider;
        IsFakeDns = Settings.IsFakeDns;
        EnableTlsFragment = Settings.EnableTlsFragment;
        XhttpModeOverride = Settings.XhttpModeOverride ?? "auto";
        PingMode = Settings.PingMode ?? "tcp";
        SubAutoUpdateInterval = Settings.SubAutoUpdateInterval ?? "24h";
        SplitTunnelingMode = Settings.SplitTunnelingMode ?? "proxy_selected";
        RouteOnlySniffing = Settings.RouteOnlySniffing;
        EnableSniffing = Settings.EnableSniffing;
        BlockQuic = Settings.BlockQuic;
        BlockIpv6 = Settings.BlockIpv6;
        AutoStart = Settings.AutoStart;
        MinimizeToTray = Settings.MinimizeToTray;
        EnableAiBypass = Settings.EnableAiBypass;
        SubscriptionUrl = Settings.SubscriptionUrl ?? string.Empty;
        AutoUpdateSubscription = Settings.AutoUpdateSubscription;
        CustomProxyRules = Settings.CustomProxyRules ?? string.Empty;
        CustomDirectRules = Settings.CustomDirectRules ?? string.Empty;
        EnableTunMode = Settings.EnableTunMode;
        EnableKillSwitch = Settings.EnableKillSwitch;
        EnableDnsLeakProtection = Settings.EnableDnsLeakProtection;
        AutoFallbackFastestServer = Settings.AutoFallbackFastestServer;
        PerAppProxyList = Settings.PerAppProxyList ?? string.Empty;
        PerAppDirectList = Settings.PerAppDirectList ?? string.Empty;
        SelectedSubscriptionGroup = Settings.SelectedSubscriptionGroup ?? "Все";
        
        var subs = _storage.LoadSubscriptions();
        Subscriptions = new ObservableCollection<SubscriptionItem>(subs);
        foreach (var sub in Subscriptions)
        {
            sub.NodeCount = Servers.Count(s => 
                (!string.IsNullOrEmpty(s.SubscriptionId) && s.SubscriptionId == sub.Id) ||
                string.Equals(s.SubscriptionGroup, sub.Name, StringComparison.OrdinalIgnoreCase));
        }
        UpdateSubscriptionGroups();
        UpdateSubscriptionLimitsUI();
        ApplyServerFilters();

        // Check for updates
        _ = CheckForAppUpdatesAsync();
        
        // Auto update subscriptions
        if (AutoUpdateSubscription && Subscriptions.Count > 0)
        {
            _ = UpdateAllSubscriptionsQuietAsync();
        }
    }

    private async Task CheckForAppUpdatesAsync()
    {
        var info = await UpdateCheckerService.CheckForUpdatesAsync("2.7.0");
        if (info.IsUpdateAvailable)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsAppUpdateAvailable = true;
                AppUpdateUrl = info.ReleaseUrl;
                AppUpdateMessage = $"Доступна новая версия: {info.LatestVersion}";
            });
        }
    }

    private void UpdateSubscriptionLimitsUI()
    {
        if (Settings.SubscriptionDataLimit > 0)
        {
            var limit = FormatBytes(Settings.SubscriptionDataLimit);
            var used = FormatBytes(Settings.SubscriptionDataUsed);
            var text = $"Трафик: {used} / {limit}";

            SubscriptionUsagePercentage = Math.Clamp((double)Settings.SubscriptionDataUsed / Settings.SubscriptionDataLimit * 100.0, 0, 100.0);
            
            if (Settings.SubscriptionExpiry != "0" && long.TryParse(Settings.SubscriptionExpiry, out var exp))
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(exp).ToLocalTime();
                text += $" · Действует до: {dt:dd.MM.yyyy}";
            }
            SubscriptionLimits = text;
        }
        else
        {
            SubscriptionLimits = string.Empty;
            SubscriptionUsagePercentage = 0;
        }
    }

    // ============================================================
    // Connection
    // ============================================================

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnected)
            Disconnect();
        else
            await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        if (SelectedServer == null)
        {
            MessageBox.Show("Список серверов пуст.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusText = "ПОДКЛЮЧЕНИЕ...";
        ConnectionDetail = "Инициализация защищённого канала...";

        var configJson = _configGen.GenerateConfig(
            node: SelectedServer,
            mode: Settings.RoutingMode,
            enableSniffing: Settings.EnableSniffing,
            routeOnlySniffing: Settings.RouteOnlySniffing,
            blockQuic: Settings.BlockQuic,
            blockIpv6: Settings.BlockIpv6,
            dnsProvider: Settings.DnsProvider,
            isFakeDns: Settings.IsFakeDns,
            enableTlsFragment: Settings.EnableTlsFragment,
            xhttpModeOverride: Settings.XhttpModeOverride,
            socksPort: Settings.SocksPort,
            httpPort: Settings.HttpPort,
            bufferSizeKb: Settings.BufferSizeKb,
            tcpCongestion: Settings.TcpCongestion,
            enableAiBypass: Settings.EnableAiBypass,
            customProxyRules: Settings.CustomProxyRules,
            customDirectRules: Settings.CustomDirectRules,
            enableTun: Settings.EnableTunMode,
            perAppProxyList: Settings.PerAppProxyList,
            perAppDirectList: Settings.PerAppDirectList
        );

        var started = await _xrayManager.StartAsync(configJson, Settings.HttpPort, Settings.SocksPort);
        if (started)
        {
            if (Settings.EnableTunMode)
            {
                _ = _tunDeviceManager.StartTunAsync(Settings.SocksPort, SelectedServer?.Address ?? "", AppendLog);
            }
            else
            {
                SystemProxyManager.EnableProxy(Settings.HttpPort);
            }
            
            // Apply DNS Leak Protection if requested
            if (Settings.EnableDnsLeakProtection)
            {
                SecurityIntegrationService.SetDnsLeakProtection(true);
            }

            // Apply Kill Switch if enabled
            if (Settings.EnableKillSwitch)
            {
                var corePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "core", "xray.exe");
                SecurityIntegrationService.EnableKillSwitch(corePath, Settings.HttpPort, Settings.SocksPort);
            }

            _trafficMonitor.Start();
            StartSessionTimer();
            IsConnected = true;
            foreach (var s in Servers)
            {
                s.IsCurrentlyActive = (s == SelectedServer);
            }
            StatusText = "ЗАЩИЩЕНО";
            ConnectionDetail = Settings.EnableTunMode 
                ? $"Туннель активен (TUN Wintun) · Прокси :{Settings.HttpPort}" 
                : $"Туннель активен · Прокси :{Settings.HttpPort}";
            TrayTooltipChanged?.Invoke("SplateVPN · Подключено");
            NotificationRequested?.Invoke("SplateVPN Подключено", $"Защищенное соединение через {SelectedServer?.DisplayName ?? "сервер"} активно.");
            AppendLog("[CLIENT] Anti-censorship tunnel is SECURE!");

            _ = DetectEgressAndHealthAsync();
        }
        else
        {
            StatusText = "ОТКЛЮЧЕНО";
            ConnectionDetail = "Ошибка запуска. Проверьте логи.";
            NotificationRequested?.Invoke("SplateVPN Ошибка", "Не удалось запустить ядро Xray. Проверьте логи.");
            MessageBox.Show("Не удалось запустить ядро Xray.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Disconnect()
    {
        StopSessionTimer();
        _trafficMonitor.Stop();
        
        // Disable Kill Switch & Restore DNS
        SecurityIntegrationService.DisableKillSwitch();
        SecurityIntegrationService.SetDnsLeakProtection(false);

        _tunDeviceManager.StopTun(AppendLog);
        SystemProxyManager.DisableProxy();
        _xrayManager.Stop();
        IsConnected = false;
        foreach (var s in Servers)
        {
            s.IsCurrentlyActive = false;
        }
        StatusText = "ОТКЛЮЧЕНО";
        ConnectionDetail = "Нажмите для создания защищённого туннеля";
        DownloadSpeed = "0 B/s";
        UploadSpeed = "0 B/s";
        DownloadSpeedRatio = 0.0;
        UploadSpeedRatio = 0.0;
        SessionDuration = "00:00:00";
        EgressIp = "Не определен";
        CountryName = "Отключено";
        CountryFlag = "🌐";
        EgressLocation = "";
        ConnectionRtt = -1;
        TrayTooltipChanged?.Invoke("SplateVPN · Отключено");
        NotificationRequested?.Invoke("SplateVPN Отключено", "VPN туннель деактивирован.");
        AppendLog("[CLIENT] Disconnected.");
    }

    private void OnProcessExited(int exitCode)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (IsConnected)
            {
                AppendLog($"[WARNING] Xray exited (code {exitCode}).");
                Disconnect();
            }
        });
    }

    // ============================================================
    // Session Timer
    // ============================================================

    private void StartSessionTimer()
    {
        _sessionStartTime = DateTime.Now;
        SessionDuration = "00:00:00";
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.Now - _sessionStartTime;
            SessionDuration = elapsed.ToString(@"hh\:mm\:ss");
        };
        _sessionTimer.Start();
    }

    private void StopSessionTimer()
    {
        _sessionTimer?.Stop();
        _sessionTimer = null;
    }

    // ============================================================
    // Subscriptions Management
    // ============================================================

    [RelayCommand]
    private void AddSubscription()
    {
        var addWnd = new AddSubscriptionWindow();
        addWnd.Owner = Application.Current.MainWindow;
        if (addWnd.ShowDialog() == true && addWnd.ResultSubscription != null)
        {
            var newSub = addWnd.ResultSubscription;
            Subscriptions.Add(newSub);
            _storage.SaveSubscriptions(Subscriptions.ToList());
            UpdateSubscriptionGroups();
            _ = UpdateSubscriptionAsync(newSub);
        }
    }

    [RelayCommand]
    private async Task UpdateSubscriptionAsync(SubscriptionItem? sub)
    {
        if (sub == null || string.IsNullOrWhiteSpace(sub.Url)) return;
        sub.IsUpdating = true;
        _isUpdatingSubscription = true;
        AppendLog($"[SUBSCRIPTION] Обновление подписки «{sub.Name}»...");

        try
        {
            var subResult = await SubscriptionService.FetchSubscriptionAsync(sub.Url, AppendLog);
            var newNodes = subResult.Nodes;

            if (newNodes.Count == 0)
            {
                AppendLog($"[SUBSCRIPTION WARNING] В подписке «{sub.Name}» не обнаружено серверов.");
                MessageBox.Show($"В подписке «{sub.Name}» не найдено серверов. Проверьте правильность ссылки.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // CRITICAL FIX: Replace existing old nodes of this subscription so that changed transports (e.g. TCP -> XHTTP) are applied!
            var currentActive = SelectedServer;
            var oldNodes = Servers.Where(s => 
                (!string.IsNullOrEmpty(s.SubscriptionId) && s.SubscriptionId == sub.Id) ||
                string.Equals(s.SubscriptionGroup, sub.Name, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            foreach (var old in oldNodes)
            {
                Servers.Remove(old);
            }

            foreach (var node in newNodes)
            {
                node.SubscriptionId = sub.Id;
                node.SubscriptionGroup = sub.Name;
                Servers.Add(node);
            }

            sub.DataLimit = subResult.Total;
            sub.DataUsed = subResult.Download + subResult.Upload;
            sub.ExpireTime = subResult.Expire;
            sub.LastUpdated = DateTime.Now;
            sub.NodeCount = newNodes.Count;

            _storage.SaveSubscriptions(Subscriptions.ToList());
            _storage.SaveServers(Servers.ToList());
            UpdateSubscriptionGroups();
            ApplyServerFilters();

            // Re-select active server if it was in this subscription
            if (currentActive != null)
            {
                var replacement = Servers.FirstOrDefault(s => s.DisplayName == currentActive.DisplayName)
                               ?? Servers.FirstOrDefault(s => s.Address == currentActive.Address && s.Port == currentActive.Port);
                if (replacement != null)
                {
                    SelectedServer = replacement;
                    if (IsConnected)
                    {
                        AppendLog($"[CLIENT] Конфиг активного узла обновлен («{replacement.DisplayName}»). Перезапуск канала...");
                        _ = SwitchServerAsync(replacement);
                    }
                }
            }
            if (SelectedServer == null)
            {
                SelectedServer = Servers.FirstOrDefault();
            }

            AppendLog($"[SUBSCRIPTION] «{sub.Name}»: успешно обновлено, узлов: {newNodes.Count}");
            NotificationRequested?.Invoke("Подписка обновлена", $"«{sub.Name}»: загружено {newNodes.Count} узлов.");
        }
        catch (Exception ex)
        {
            AppendLog($"[SUBSCRIPTION ERROR] {ex.Message}");
            MessageBox.Show($"Ошибка обновления подписки «{sub.Name}»:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            sub.IsUpdating = false;
            _isUpdatingSubscription = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAllSubscriptionsAsync()
    {
        if (Subscriptions.Count == 0)
        {
            MessageBox.Show("Список подписок пуст. Нажмите «+ Добавить подписку».", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var sub in Subscriptions.ToList())
        {
            await UpdateSubscriptionAsync(sub);
        }
    }

    public async Task UpdateAllSubscriptionsQuietAsync()
    {
        _isUpdatingSubscription = true;
        try
        {
            foreach (var sub in Subscriptions.Where(s => s.AutoUpdate).ToList())
            {
                try
                {
                    var subResult = await SubscriptionService.FetchSubscriptionAsync(sub.Url, AppendLog);
                    var newNodes = subResult.Nodes;
                    if (newNodes.Count > 0)
                    {
                        var oldNodes = Servers.Where(s => 
                            (!string.IsNullOrEmpty(s.SubscriptionId) && s.SubscriptionId == sub.Id) ||
                            string.Equals(s.SubscriptionGroup, sub.Name, StringComparison.OrdinalIgnoreCase)
                        ).ToList();

                        foreach (var old in oldNodes) Servers.Remove(old);
                        foreach (var node in newNodes)
                        {
                            node.SubscriptionId = sub.Id;
                            node.SubscriptionGroup = sub.Name;
                            Servers.Add(node);
                        }

                        sub.DataLimit = subResult.Total;
                        sub.DataUsed = subResult.Download + subResult.Upload;
                        sub.ExpireTime = subResult.Expire;
                        sub.LastUpdated = DateTime.Now;
                        sub.NodeCount = newNodes.Count;
                    }
                }
                catch { }
            }
            _storage.SaveSubscriptions(Subscriptions.ToList());
            _storage.SaveServers(Servers.ToList());
            UpdateSubscriptionGroups();
            ApplyServerFilters();
        }
        finally
        {
            _isUpdatingSubscription = false;
        }
    }

    [RelayCommand]
    private void DeleteSubscription(SubscriptionItem? sub)
    {
        if (sub == null) return;
        var res = MessageBox.Show($"Удалить подписку «{sub.Name}» и все входящие в неё серверы ({sub.NodeCount} шт.)?", 
            "Удаление подписки", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            var nodesToRemove = Servers.Where(s => 
                (!string.IsNullOrEmpty(s.SubscriptionId) && s.SubscriptionId == sub.Id) ||
                string.Equals(s.SubscriptionGroup, sub.Name, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            foreach (var node in nodesToRemove)
            {
                Servers.Remove(node);
            }

            Subscriptions.Remove(sub);
            _storage.SaveSubscriptions(Subscriptions.ToList());
            _storage.SaveServers(Servers.ToList());
            UpdateSubscriptionGroups();
            ApplyServerFilters();

            if (SelectedServer != null && nodesToRemove.Contains(SelectedServer))
            {
                SelectedServer = Servers.FirstOrDefault();
            }

            AppendLog($"[SUBSCRIPTION] Удалена подписка «{sub.Name}» и {nodesToRemove.Count} узлов.");
        }
    }

    // ============================================================
    // Universal Link Import & Clipboard
    // ============================================================

    [RelayCommand]
    private void AddFromClipboard()
    {
        try
        {
            var text = Clipboard.GetText()?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Буфер обмена пуст. Скопируйте ссылку на сервер (vless://, trojan://, vmess://) или ссылку на подписку (https://...).", 
                    "Буфер обмена", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ProcessAnyLink(text, fromClipboard: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка буфера обмена: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void AddManualServer()
    {
        if (string.IsNullOrWhiteSpace(ManualServerLink))
        {
            MessageBox.Show("Вставьте ссылку на сервер (vless://, trojan://, vmess://, ss://) или подписку (https://...).",
                "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var link = ManualServerLink.Trim();
        ManualServerLink = string.Empty;
        ProcessAnyLink(link, fromClipboard: false);
    }

    public void ProcessAnyLink(string input, bool fromClipboard = false)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        input = input.Trim();

        // 1. Subscription URL
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var existing = Subscriptions.FirstOrDefault(s => s.Url.Equals(input, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                var res = MessageBox.Show($"Эта подписка уже добавлена («{existing.Name}»).\nОбновить её прямо сейчас?", "Подписка найдена", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    _ = UpdateSubscriptionAsync(existing);
                }
                return;
            }

            var addWnd = new AddSubscriptionWindow(input);
            addWnd.Owner = Application.Current.MainWindow;
            if (addWnd.ShowDialog() == true && addWnd.ResultSubscription != null)
            {
                var newSub = addWnd.ResultSubscription;
                Subscriptions.Add(newSub);
                _storage.SaveSubscriptions(Subscriptions.ToList());
                UpdateSubscriptionGroups();
                _ = UpdateSubscriptionAsync(newSub);
            }
            return;
        }

        // 2. Multi-line server links (e.g. user pasted 5 servers)
        var lines = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(l => l.Trim())
                         .Where(l => !string.IsNullOrEmpty(l))
                         .ToList();

        if (lines.Count > 1)
        {
            int addedCount = 0;
            foreach (var line in lines)
            {
                var n = LinkParser.Parse(line);
                if (n != null && !Servers.Any(s => s.Address == n.Address && s.Port == n.Port))
                {
                    Servers.Add(n);
                    addedCount++;
                }
            }
            if (addedCount > 0)
            {
                _storage.SaveServers(Servers.ToList());
                ApplyServerFilters();
                NotificationRequested?.Invoke("Серверы добавлены", $"Успешно добавлено {addedCount} узлов.");
                AppendLog($"[SERVERS] Пакетно добавлено {addedCount} серверов.");
                MessageBox.Show($"Успешно добавлено {addedCount} серверов!", "Импорт завершен", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Не удалось найти новые серверы в предоставленном тексте или они уже есть в списке.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        // 3. Single server node
        var singleNode = LinkParser.Parse(input);
        if (singleNode != null)
        {
            if (Servers.Any(s => s.Address == singleNode.Address && s.Port == singleNode.Port))
            {
                MessageBox.Show("Этот сервер уже добавлен в список.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Servers.Add(singleNode);
            _storage.SaveServers(Servers.ToList());
            ApplyServerFilters();
            SelectedServer = singleNode;
            NotificationRequested?.Invoke("Сервер добавлен", $"Добавлен: {singleNode.DisplayName}");
            AppendLog($"[SERVER] Добавлен узел: {singleNode.DisplayName}");
            MessageBox.Show($"Сервер «{singleNode.DisplayName}» ({singleNode.ProtocolUpper}) успешно добавлен!", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Не удалось распознать ссылку. Поддерживаются форматы:\n• vless://\n• trojan://\n• vmess://\n• ss://\n• https:// (подписки)", 
                "Неверный формат", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task SelectAndConnectServerAsync(ServerNode? node)
    {
        if (node == null) return;
        if (!IsConnected)
        {
            SelectedServer = node;
            await ConnectAsync();
        }
        else
        {
            if (SelectedServer != node)
            {
                SelectedServer = node; // OnSelectedServerChanged triggers SwitchServerAsync
            }
            else
            {
                AppendLog($"[CLIENT] Переподключение к «{node.DisplayName}»...");
                await SwitchServerAsync(node);
            }
        }
    }

    // ============================================================
    // Ping All Servers
    // ============================================================

    [RelayCommand]
    private void PingAllServers()
    {
        _ = PingAllServersInternalAsync();
    }

    [RelayCommand]
    private void OpenUrl(string url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private async Task PingAllServersInternalAsync()
    {
        if (IsPinging || Servers.Count == 0) return;
        IsPinging = true;
        AppendLog($"[PING] Пинг {Servers.Count} серверов (Режим: {(UseHttpPing && IsConnected ? "HTTP Proxy Test" : "TCP Handshake")})...");

        var tasks = Servers.Select(async server =>
        {
            long latency;
            // If connected and user selected HTTP Ping, test real proxy tunnel latency for the active server
            if (UseHttpPing && IsConnected && SelectedServer == server)
            {
                latency = await PingService.MeasureHttpProxyLatencyAsync(Settings.HttpPort, 3500);
            }
            else
            {
                latency = await PingService.MeasureTcpLatencyAsync(server.Address, server.Port, 3000);
            }
            server.Ping = latency > 0 ? latency : 0;
        });

        await Task.WhenAll(tasks);

        // Force UI refresh — notify the collection changed
        Application.Current.Dispatcher.Invoke(() =>
        {
            var temp = Servers.ToList();
            // Sort by ping (reachable first, then by latency)
            temp.Sort((a, b) =>
            {
                if (a.Ping > 0 && b.Ping > 0) return a.Ping.CompareTo(b.Ping);
                if (a.Ping > 0) return -1;
                if (b.Ping > 0) return 1;
                return 0;
            });

            var selected = SelectedServer;
            Servers = new ObservableCollection<ServerNode>(temp);
            SelectedServer = selected != null && Servers.Contains(selected)
                ? selected
                : Servers.FirstOrDefault(s => s.Ping > 0) ?? Servers.FirstOrDefault();

            _storage.SaveServers(Servers.ToList());

            // Auto-fallback: if connected and current server has bad ping (>800 or 0), switch to best available
            if (AutoFallbackFastestServer && IsConnected && SelectedServer != null)
            {
                var best = Servers.FirstOrDefault(s => s.Ping > 0);
                if (best != null && best != SelectedServer && (SelectedServer.Ping == 0 || SelectedServer.Ping > 500))
                {
                    AppendLog($"[AUTO-FALLBACK] Автопереключение на лучший узел: {best.DisplayName} ({best.Ping} ms)");
                    SelectedServer = best;
                    _ = ConnectAsync();
                }
            }
        });

        AppendLog("[PING] Завершено.");
        IsPinging = false;
    }

    // ============================================================
    // Servers
    // ============================================================

    [RelayCommand]
    private async Task PingSingleServerAsync(ServerNode? node)
    {
        if (node == null) return;
        node.IsTestingPing = true;
        try
        {
            long latency;
            if (UseHttpPing && IsConnected && SelectedServer == node)
            {
                latency = await PingService.MeasureHttpProxyLatencyAsync(Settings.HttpPort, 3500);
            }
            else
            {
                latency = await PingService.MeasureTcpLatencyAsync(node.Address, node.Port, 3000);
            }
            node.Ping = latency > 0 ? latency : 0;
            _storage.SaveServers(Servers.ToList());
        }
        catch
        {
            node.Ping = 0;
        }
        finally
        {
            node.IsTestingPing = false;
        }
    }

    [RelayCommand]
    private void EditServer(ServerNode? node)
    {
        if (node == null) return;
        var editWindow = new EditServerWindow(node);
        editWindow.Owner = Application.Current.MainWindow;
        if (editWindow.ShowDialog() == true && editWindow.ResultNode != null)
        {
            var res = editWindow.ResultNode;
            node.Name = res.Name;
            node.Protocol = res.Protocol;
            node.Address = res.Address;
            node.Port = res.Port;
            node.Uuid = res.Uuid;
            node.Password = res.Password;
            node.Security = res.Security;
            node.Sni = res.Sni;
            node.Network = res.Network;
            node.Path = res.Path;
            node.Host = res.Host;
            node.Flow = res.Flow;
            node.Fingerprint = res.Fingerprint;
            node.PublicKey = res.PublicKey;
            node.ShortId = res.ShortId;
            node.SpiderX = res.SpiderX;
            node.XhttpMode = res.XhttpMode;
            node.XhttpExtra = res.XhttpExtra;
            node.SubscriptionGroup = res.SubscriptionGroup;

            _storage.SaveServers(Servers.ToList());
            ApplyServerFilters();
            AppendLog($"[SERVER] Отредактирован: {node.DisplayName}");
        }
    }

    [RelayCommand]
    private void CreateServer()
    {
        var editWindow = new EditServerWindow(null);
        editWindow.Owner = Application.Current.MainWindow;
        if (editWindow.ShowDialog() == true && editWindow.ResultNode != null)
        {
            var node = editWindow.ResultNode;
            Servers.Add(node);
            _storage.SaveServers(Servers.ToList());
            ApplyServerFilters();
            SelectedServer = node;
            AppendLog($"[SERVER] Создан вручную: {node.DisplayName}");
        }
    }

    [RelayCommand]
    private void CopyServerLink(ServerNode? node)
    {
        if (node == null) return;
        var link = LinkParser.GenerateLink(node);
        if (!string.IsNullOrWhiteSpace(link))
        {
            try
            {
                Clipboard.SetText(link);
                NotificationRequested?.Invoke("Ссылка скопирована", $"Ссылка для «{node.DisplayName}» скопирована в буфер.");
                AppendLog($"[CLIPBOARD] Ссылка скопирована: {node.DisplayName}");
            }
            catch (Exception ex)
            {
                AppendLog($"[CLIPBOARD ERROR] {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task ConnectServerAsync(ServerNode? node)
    {
        if (node == null) return;
        if (IsConnected && SelectedServer == node)
        {
            Disconnect();
            return;
        }

        if (IsConnected)
        {
            Disconnect();
            await Task.Delay(300);
        }

        SelectedServer = node;
        await ConnectAsync();
    }

    [RelayCommand]
    private void SelectServer(ServerNode? node)
    {
        if (node != null)
        {
            SelectedServer = node;
        }
    }

    [RelayCommand]
    public async Task RefreshIpDetailsAsync()
    {
        await DetectEgressAndHealthAsync();
    }

    public async Task DetectEgressAndHealthAsync()
    {
        if (IsDetectingIp) return;
        IsDetectingIp = true;
        try
        {
            int socksPort = IsConnected ? Settings.SocksPort : 0;
            var ipTask = IpLocationService.FetchEgressIpAsync(socksPort);
            var connTask = IpLocationService.CheckConnectivityAsync(socksPort);

            await Task.WhenAll(ipTask, connTask);

            var ipDetails = await ipTask;
            var (connSuccess, rtt) = await connTask;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (ipDetails != null && ipDetails.IsSuccess)
                {
                    EgressIp = ipDetails.Ip;
                    CountryName = ipDetails.Country;
                    CountryFlag = ipDetails.FlagEmoji;
                    EgressLocation = string.IsNullOrWhiteSpace(ipDetails.City) 
                        ? ipDetails.Country 
                        : $"{ipDetails.City}, {ipDetails.Country}";
                }
                else
                {
                    EgressIp = IsConnected ? "Скрыт (VPN)" : "Прямой";
                }
                ConnectionRtt = connSuccess ? rtt : -1;
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[GEO IP ERROR] {ex.Message}");
        }
        finally
        {
            IsDetectingIp = false;
        }
    }

    [RelayCommand]
    private void DeleteServer(ServerNode? node)
    {
        if (node != null && Servers.Contains(node))
        {
            Servers.Remove(node);
            _storage.SaveServers(Servers.ToList());
            ApplyServerFilters();
            if (SelectedServer == node)
                SelectedServer = Servers.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void ClearAllServers()
    {
        Servers.Clear();
        SelectedServer = null;
        _storage.SaveServers(Servers.ToList());
        ApplyServerFilters();
    }

    // ============================================================
    // Config Export / Import
    // ============================================================

    [RelayCommand]
    private async Task ExportConfigAsync()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "SplateVPN config (*.spcfg)|*.spcfg|All files (*.*)|*.*",
            FileName = "SplateVPN_Config.spcfg"
        };
        if (dlg.ShowDialog() == true)
        {
            var passphrase = "splate_pass";
            await _configService.ExportAsync(dlg.FileName, passphrase);
            MessageBox.Show("Конфигурация экспортирована!", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private async Task ImportConfigAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "SplateVPN config (*.spcfg)|*.spcfg|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            var passphrase = "splate_pass";
            await _configService.ImportAsync(dlg.FileName, passphrase);
            LoadData();
            MessageBox.Show("Конфигурация импортирована!", "Импорт", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ============================================================
    // Settings
    // ============================================================

    [RelayCommand]
    private void SaveSettings()
    {
        Settings.RoutingMode = SelectedRoutingMode;
        Settings.DnsProvider = SelectedDnsProvider;
        Settings.EnableSniffing = EnableSniffing;
        Settings.BlockQuic = BlockQuic;
        Settings.BlockIpv6 = BlockIpv6;
        Settings.AutoStart = AutoStart;
        Settings.MinimizeToTray = MinimizeToTray;
        Settings.EnableAiBypass = EnableAiBypass;
        Settings.AutoUpdateSubscription = AutoUpdateSubscription;
        Settings.CustomProxyRules = CustomProxyRules;
        Settings.CustomDirectRules = CustomDirectRules;
        Settings.EnableTunMode = EnableTunMode;
        Settings.EnableKillSwitch = EnableKillSwitch;
        Settings.EnableDnsLeakProtection = EnableDnsLeakProtection;
        Settings.AutoFallbackFastestServer = AutoFallbackFastestServer;
        Settings.PerAppProxyList = PerAppProxyList;
        Settings.PerAppDirectList = PerAppDirectList;
        Settings.SelectedSubscriptionGroup = SelectedSubscriptionGroup;
        _storage.SaveSettings(Settings);

        // Apply autostart setting
        WindowsIntegrationService.SetAutoStart(AutoStart);

        MessageBox.Show("Настройки сохранены!", "Настройки", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task UpdateGeoAssetsAsync()
    {
        if (IsUpdatingGeoAssets) return;
        IsUpdatingGeoAssets = true;
        try
        {
            var coreDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "core");
            var ok = await GeoAssetUpdaterService.UpdateGeoAssetsAsync(coreDir, AppendLog);
            if (ok)
            {
                NotificationRequested?.Invoke("SplateVPN Базы обновлены", "Геобазы geoip.dat и geosite.dat успешно обновлены.");
                MessageBox.Show("Базы гео-маршрутизации успешно обновлены!", "Обновление баз", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Не удалось обновить базы. Проверьте подключение к интернету или логи.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsUpdatingGeoAssets = false;
        }
    }

    public void UpdateSubscriptionGroups()
    {
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Все" };
        foreach (var sub in Subscriptions)
        {
            if (!string.IsNullOrWhiteSpace(sub.Name))
                groups.Add(sub.Name);
        }
        foreach (var s in Servers)
        {
            if (!string.IsNullOrWhiteSpace(s.SubscriptionGroup))
                groups.Add(s.SubscriptionGroup);
        }

        SubscriptionGroups = new ObservableCollection<string>(groups);
        if (!SubscriptionGroups.Contains(SelectedSubscriptionGroup))
        {
            SelectedSubscriptionGroup = "Все";
        }
    }

    // ============================================================
    // QR Code Scanning
    // ============================================================

    [RelayCommand]
    private async Task ScanQrFromScreenAsync()
    {
        AppendLog("[QR] Сканирование экрана на наличие QR-кода...");
        var result = await _qrScanner.ScanFromScreenAsync();
        ProcessQrResult(result);
    }

    [RelayCommand]
    private async Task ScanQrFromFileAsync()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите изображение с QR-кодом",
            Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Все файлы (*.*)|*.*"
        };
        if (ofd.ShowDialog() == true)
        {
            AppendLog($"[QR] Распознавание QR из файла: {ofd.FileName}...");
            var result = await _qrScanner.ScanFromFileAsync(ofd.FileName);
            ProcessQrResult(result);
        }
    }

    private void ProcessQrResult(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            MessageBox.Show("QR-код на экране не обнаружен или не содержит данных.", "QR Сканер", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        link = link.Trim();
        AppendLog($"[QR] Распознано: {link}");
        ProcessAnyLink(link, fromClipboard: false);
    }

    // ============================================================
    // Windows Service Mode
    // ============================================================

    [RelayCommand]
    private void InstallWindowsService()
    {
        var corePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "core", "xray.exe");
        var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "core", "config.json");
        var ok = WindowsServiceHelper.InstallService(corePath, configPath);
        if (ok)
        {
            MessageBox.Show("Фоновая служба Windows 'SplateVpnCore' успешно зарегистрирована!", "Служба Windows", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Не удалось установить службу. Запустите приложение от имени Администратора.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void UninstallWindowsService()
    {
        var ok = WindowsServiceHelper.UninstallService();
        if (ok)
        {
            MessageBox.Show("Фоновая служба Windows успешно удалена.", "Служба Windows", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Не удалось удалить службу.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ============================================================
    // Logs
    // ============================================================

    [RelayCommand]
    private void ClearLogs() => Logs = string.Empty;

    private void AppendLog(string message)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            Logs += $"[{timestamp}] {message}\n";
            if (Logs.Length > 20000)
                Logs = Logs.Substring(Logs.Length - 10000);
        });
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
