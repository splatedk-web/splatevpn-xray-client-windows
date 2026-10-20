using System.IO;
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
    private readonly IConnectionPlatform _platform;
    private readonly IConnectionProbes _probes;
    private SessionState? _activeConnection;
    private record SessionState(ServerNode Node, AppSettings Settings, string Config, int MetricsPort, NetworkUplink? Uplink);
    private int _connectionEpoch, _toggleRequest;
    private bool _connectionRequested, _initialSelection;
    private bool _speedTestRunning;
    private System.Threading.CancellationTokenSource? _optimizationCts;
    private Task? _optimizationTask;
    public bool IsSpeedTestRunning { get => _speedTestRunning; set { _speedTestRunning = value; if (value) _optimizationCts?.Cancel(); } }
    public event Action? ConnectionChanging;
    private readonly BalancerPolicy _balancerPolicy;
    private long _currentDownloadBps;
    private AppSettings? _activeSettings;
    public int ActiveHttpPort => _activeSettings?.HttpPort ?? Settings.HttpPort;
    private int ActiveSocksPort => _activeSettings?.SocksPort ?? Settings.SocksPort;
    public bool ActiveTunMode => _platform.IsTunRunning;
    public bool ActiveAiXboxDns => _activeSettings?.EnableAiXboxDns == true;
    public bool ActiveAiDns => _activeSettings is { EnableAiDns: true, EnableAiBypass: true, AiRoutingMode: "dns_ai" };
    public string ActiveAiMode => _activeSettings?.AiRoutingMode ?? "off";
    public string ActiveAiDnsLabel => SmartDnsProviders.ForAi(_activeSettings?.AiDnsProvider ?? "dns_ai").Label;
    private readonly QrCodeScannerService _qrScanner = new();
    private DispatcherTimer? _sessionTimer;
    private System.Threading.Timer? _healthCheckTimer;
    private DispatcherTimer? _subscriptionUpdateTimer;
    private static readonly System.Threading.Channels.Channel<string> _logFileChannel =
        System.Threading.Channels.Channel.CreateUnbounded<string>(new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private DateTime _sessionStartTime;

    private readonly System.Threading.SemaphoreSlim _connectionLock = new(1, 1);
    private System.Threading.CancellationTokenSource? _connectCts;

    // --- Frozen Status Brushes ---
    private static readonly System.Windows.Media.SolidColorBrush _emeraldBrush = CreateFrozenBrush("#10E79D");
    private static readonly System.Windows.Media.SolidColorBrush _cyanBrush = CreateFrozenBrush("#00F0FF");
    private static readonly System.Windows.Media.SolidColorBrush _slateBrush = CreateFrozenBrush("#94A3B8");
    private static readonly System.Windows.Media.SolidColorBrush _badgeBgConnected = CreateFrozenBrush("#1810E79D");
    private static readonly System.Windows.Media.SolidColorBrush _badgeBgConnecting = CreateFrozenBrush("#1800F0FF");
    private static readonly System.Windows.Media.SolidColorBrush _badgeBgDisconnected = CreateFrozenBrush("#121E2B42");
    private static readonly System.Windows.Media.SolidColorBrush _badgeBorderConnected = CreateFrozenBrush("#4010E79D");
    private static readonly System.Windows.Media.SolidColorBrush _badgeBorderConnecting = CreateFrozenBrush("#4000F0FF");
    private static readonly System.Windows.Media.SolidColorBrush _badgeBorderDisconnected = CreateFrozenBrush("#253B5A");

    private static System.Windows.Media.SolidColorBrush CreateFrozenBrush(string hex)
    {
        var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public System.Windows.Media.SolidColorBrush StatusColorBrush =>
        IsConnected ? _emeraldBrush : (IsConnecting ? _cyanBrush : _slateBrush);

    public System.Windows.Media.SolidColorBrush StatusBadgeBackground =>
        IsConnected ? _badgeBgConnected : (IsConnecting ? _badgeBgConnecting : _badgeBgDisconnected);

    public System.Windows.Media.SolidColorBrush StatusBadgeBorder =>
        IsConnected ? _badgeBorderConnected : (IsConnecting ? _badgeBorderConnecting : _badgeBorderDisconnected);

    public string StatusBadgeText =>
        IsConnecting ? "● ПОДКЛЮЧЕНИЕ..." : (IsConnected ? $"● ЗАЩИЩЕННАЯ СЕССИЯ  {SessionDuration}" : "● ОЖИДАНИЕ ЗАЩИТЫ");

    // --- Connection state ---
    [ObservableProperty]
    private bool _isConnected;

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusColorBrush));
        OnPropertyChanged(nameof(StatusBadgeBackground));
        OnPropertyChanged(nameof(StatusBadgeBorder));
        OnPropertyChanged(nameof(StatusBadgeText));
    }

    [ObservableProperty]
    private bool _isConnecting;

    partial void OnIsConnectingChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusColorBrush));
        OnPropertyChanged(nameof(StatusBadgeBackground));
        OnPropertyChanged(nameof(StatusBadgeBorder));
        OnPropertyChanged(nameof(StatusBadgeText));
    }

    [ObservableProperty]
    private string _statusText = "ОТКЛЮЧЕНО";

    [ObservableProperty]
    private string _connectionDetail = "Нажмите для создания защищённого туннеля";

    // --- Node View Tabs (Matching Android NodeViewMode: 0 = ALL, 1 = PRESET, 2 = SUBSCRIPTIONS, 3 = PERSONAL) ---
    [ObservableProperty]
    private int _selectedNodeTab = 0;

    partial void OnSelectedNodeTabChanged(int value) => ApplyServerFilters();

    [RelayCommand]
    private void SetNodeTab(object? param)
    {
        if (param != null && int.TryParse(param.ToString(), out int tab))
        {
            SelectedNodeTab = tab;
        }
    }

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

    private int _selectionRequest;
    private readonly ServerQualityTracker _quality = new();
    private int _autoSelectionRunning;
    private System.Threading.CancellationTokenSource? _autoSelectionCts;

    public async Task SelectServerAsync(ServerNode node, bool forceReconnect = false)
    {
        if (!IsServerEnabled(node)) throw new InvalidOperationException("Подписка отключена. Включите её перед выбором сервера.");
        if (!Servers.Contains(node)) return;
        var request = ++_selectionRequest;
        _autoSelectionCts?.Cancel();
        if (!forceReconnect && SelectedServer == node && (!IsConnected || _activeConnection?.Node == node)) return;
        var reconnect = !_initialSelection && (IsConnected || IsConnecting || _isSwitchingServer || _connectionRequested);
        if (reconnect) _connectCts?.Cancel();
        await _connectionLock.WaitAsync();
        try
        {
            if (request != _selectionRequest || !Servers.Contains(node) || !IsServerEnabled(node)) return;
            _isSwitchingServer = true;
            SelectedServer = node;
            if (reconnect && _connectionRequested)
            {
                _connectCts?.Dispose();
                _connectCts = new System.Threading.CancellationTokenSource();
                await ConnectInternalAsync(_connectCts.Token, replacing: true);
            }
        }
        finally { _isSwitchingServer = false; _connectionLock.Release(); }
    }
    public Task SwitchServerAsync(ServerNode node) => SelectServerAsync(node, forceReconnect: true);

    [RelayCommand]
    private Task AutoSelectServerAsync() => AutoSelectVerifiedAsync();

    private async Task<(ServerNode? Node, long Ping)> ConfirmBestCandidateAsync(IEnumerable<KeyValuePair<ServerNode,long>> results, System.Threading.CancellationToken token)
    {
        foreach (var candidate in results.Where(x=>x.Value>0 && Servers.Contains(x.Key) && IsServerEnabled(x.Key)).OrderBy(x=>_quality.Score(x.Key.Id,x.Value)).Take(3))
        {
            long confirmed=0;
            await _probes.CandidatesAsync([candidate.Key],(_,delay)=>confirmed=delay,token);
            token.ThrowIfCancellationRequested();
            _quality.Record(candidate.Key.Id,confirmed);
            if(confirmed>0) return (candidate.Key,confirmed);
            AppendLog($"[PROBE] «{candidate.Key.DisplayName}» не прошёл повторную проверку.");
        }
        return (null,-1);
    }

    private async Task<bool> AutoSelectVerifiedAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _autoSelectionRunning, 1) != 0) return false;
        try
        {
            _autoSelectionCts = new System.Threading.CancellationTokenSource();
            var token = _autoSelectionCts.Token;
            if (Servers.Count == 0) { NotifyUser("Добавьте сервер или ссылку на подписку.", "Автовыбор"); return false; }
            var selectionAtStart = SelectedServer;
            var requestAtStart = _selectionRequest;
            var candidates = Servers.Where(s => IsServerEnabled(s) && !_quality.IsQuarantined(s.Id))
                .OrderBy(s => _quality.Score(s.Id, s.Ping, s == SelectedServer)).ToList();
            AppendLog("[AUTO-SELECT] Проверка VPN через каждый узел отдельно; TCP-пинг не подтверждает работоспособность.");
            foreach (var batch in candidates.Chunk(10))
            {
                var measured = new System.Collections.Concurrent.ConcurrentDictionary<ServerNode,long>();
                await _probes.CandidatesAsync(batch,(node,ping)=>{
                    measured[node]=ping;_quality.Record(node.Id,ping);
                    Application.Current.Dispatcher.InvokeAsync(()=>{node.Ping=ping;ServerPingUpdated?.Invoke(node);});
                },token);
                token.ThrowIfCancellationRequested();
                if(requestAtStart!=_selectionRequest || SelectedServer!=selectionAtStart) return false;
                var best=await ConfirmBestCandidateAsync(measured,token);
                if(requestAtStart!=_selectionRequest || SelectedServer!=selectionAtStart) return false;
                if(best.Node==null) continue;
                await SelectServerAsync(best.Node);
                AppendLog($"[AUTO-SELECT] Выбран «{best.Node.DisplayName}» ({best.Ping} ms), проверка подтверждена.");
                return true;
            }
            NotifyUser("Проверка не нашла подтверждённых VPN-серверов. Текущий выбор сохранён. Для ручного подключения отключите автовыбор.","Автовыбор");
            return false;
        }
        catch(OperationCanceledException){return false;}
        catch(Exception ex){AppendLog($"[AUTO-SELECT] {ex.Message}");return false;}
        finally{_autoSelectionCts?.Dispose();_autoSelectionCts=null;System.Threading.Interlocked.Exchange(ref _autoSelectionRunning,0);}
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

    partial void OnSessionDurationChanged(string value)
    {
        if (IsConnected)
        {
            OnPropertyChanged(nameof(StatusBadgeText));
        }
    }

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
    private bool _isCheckingAppUpdate;
    [ObservableProperty]
    private string _appUpdateStatus = "Проверка ещё не выполнялась.";
    [ObservableProperty]
    private string _appLatestVersion = string.Empty;
    private int _appUpdateCheckRunning;

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
    private System.Threading.CancellationTokenSource? _pingCancellation;
    public event Action<int, int, bool>? PingProgressUpdated;
    public void CancelPing() => _pingCancellation?.Cancel();

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
        if (value && IsConnected)
        {
            StartHealthCheckTimer();
        }
        else
        {
            StopHealthCheckTimer();
        }
    }

    partial void OnServerSearchQueryChanged(string value) => ApplyServerFilters();
    partial void OnSelectedSubscriptionGroupChanged(string value) => ApplyServerFilters();

    public void ApplyServerFilters()
    {
        var query = ServerSearchQuery?.Trim().ToLowerInvariant() ?? "";
        var group = SelectedSubscriptionGroup ?? "Все";

        var filtered = Servers.Where(s =>
        {
            // Tab filtering (Matching Android NodeViewMode: 0 = ALL, 1 = PRESET, 2 = SUBSCRIPTIONS, 3 = PERSONAL)
            if (SelectedNodeTab == 1) // PRESET / Предустановленные
            {
                bool isPreset = PresetSubscriptions.Catalog.Any(p => p.Id == s.SubscriptionId);
                if (!isPreset) return false;
            }
            else if (SelectedNodeTab == 2) // SUBSCRIPTIONS / Подписки
            {
                bool isSub = !string.IsNullOrEmpty(s.SubscriptionId) && s.SubscriptionId is not ("custom" or "builtin") && !PresetSubscriptions.Catalog.Any(p => p.Id == s.SubscriptionId);
                if (!isSub) return false;
            }
            else if (SelectedNodeTab == 3) // PERSONAL / Личные
            {
                bool isPersonal = string.IsNullOrEmpty(s.SubscriptionId) || s.SubscriptionId is "custom" or "builtin";
                if (!isPersonal) return false;
            }

            if (SelectedNodeTab == 0 && !IsServerEnabled(s)) return false;
            if (group != "Все" && s.SubscriptionId != group && !string.Equals(s.SubscriptionGroup, group, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(query))
            {
                var matchName = s.DisplayName.ToLowerInvariant().Contains(query);
                var matchAddr = s.Address.ToLowerInvariant().Contains(query);
                var matchProto = s.Protocol.ToLowerInvariant().Contains(query);
                if (!matchName && !matchAddr && !matchProto && !s.SubscriptionGroup.Contains(query, StringComparison.OrdinalIgnoreCase) && !s.Network.Contains(query, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }).ToList();

        // Sort: reachable nodes (ping > 0) with lowest ping first, then unreachable
        var sorted = Settings.ServerSort switch
        {
            "ping" => filtered.OrderBy(s => s.Ping > 0 ? 0 : 1).ThenBy(s => s.Ping > 0 ? s.Ping : long.MaxValue).ThenBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList(),
            "name" => filtered.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList(),
            "favorites" => filtered.OrderByDescending(s => s.IsFavorite).ThenBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList(),
            _ => filtered
        };

        DisplayServers = new ObservableCollection<ServerNode>(sorted);
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

    // Event to stream logs directly to WebView2 console
    public event Action<string>? LogMessageReceived;

    // Event to notify when a single server ping is updated
    public event Action<ServerNode>? ServerPingUpdated;

    // Event to notify when bulk server changes have completed
    public event Action? ServersBulkUpdated;

    public ITrafficMonitorService TrafficMonitor => _trafficMonitor;
    public IServerStorage Storage => _storage;

    static MainViewModel()
    {
        Task.Run(ProcessLogFileQueueAsync);
    }

    private readonly bool _enableBackgroundWork;
    public MainViewModel(
        IServerStorage storage,
        IXrayProcessManager xrayManager,
        IConfigExportImportService configService,
        ITrafficMonitorService trafficMonitor,
        bool enableBackgroundWork = true,
        IConnectionPlatform? platform = null,
        IConnectionProbes? probes = null,
        BalancerPolicy? balancerPolicy = null)
    {
        _enableBackgroundWork = enableBackgroundWork;
        _platform = platform ?? new WindowsConnectionPlatform();
        _probes = probes ?? new ConnectionProbes();
        _balancerPolicy = balancerPolicy ?? new BalancerPolicy();
        _storage = storage;
        _xrayManager = xrayManager;
        _configGen = new XrayConfigGenerator();
        _configService = configService;
        _trafficMonitor = trafficMonitor;

        _xrayManager.OnLogMessage += OnCoreLogMessage;
        _xrayManager.OnProcessExited += OnProcessExited;
        _platform.OnTunExited += OnProcessExited;

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
            _currentDownloadBps = downBytes;
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
        if (!string.IsNullOrWhiteSpace(Settings.PerAppProxyList) || !string.IsNullOrWhiteSpace(Settings.PerAppDirectList))
            AppendLog("[COMPAT] Старые списки приложений сохранены, но на Windows не применяются. Используются правила доменов/IP; подключение доступно.");
        var nodes = _storage.LoadServers();
        if (_storage is ServerStorage { LastLoadWarning: { } warning })
            NotifyUser(warning, "Каталог серверов", MessageBoxButton.OK, MessageBoxImage.Warning);
        Servers = new ObservableCollection<ServerNode>(nodes);
        SelectedServer = Servers.FirstOrDefault(s => s.IsSelected) ?? Servers.FirstOrDefault(IsServerEnabled);

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
        if (!Settings.PresetSubscriptionsInitialized)
        {
            try
            {
                PresetSubscriptions.Seed(Subscriptions, Servers, System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "presets"));
                _storage.SaveServers(Servers.ToList());
                _storage.SaveSubscriptions(Subscriptions.ToList());
                Settings.PresetSubscriptionsInitialized = true;
                _storage.SaveSettings(Settings);
            }
            catch (Exception ex) { AppendLog($"[PRESET ERROR] {ex.Message}"); }
        }
        SelectedServer = Servers.FirstOrDefault(s => s.IsSelected && IsServerEnabled(s)) ?? Servers.FirstOrDefault(IsServerEnabled);
        foreach (var sub in Subscriptions)
        {
            sub.NodeCount = Servers.Count(s =>
                PresetSubscriptions.Owns(sub, s));
        }
        UpdateSubscriptionGroups();
        UpdateSubscriptionLimitsUI();
        ApplyServerFilters();

        // Check for updates
        if (_enableBackgroundWork) _ = CheckForAppUpdatesAsync();

        // Auto update subscriptions
        if (_enableBackgroundWork && AutoUpdateSubscription && SubAutoUpdateInterval != "manual" && Subscriptions.Count > 0)
        {
            _ = UpdateAllSubscriptionsQuietAsync();
        }

        if (_enableBackgroundWork) StartSubscriptionUpdateTimer();
    }

    public async Task CheckForAppUpdatesAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _appUpdateCheckRunning, 1) != 0) return;
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() => { IsCheckingAppUpdate = true; AppUpdateStatus = "Проверка обновлений…"; });
            var info = await UpdateCheckerService.CheckForUpdatesAsync();
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!info.CheckSucceeded) { AppUpdateStatus = info.ErrorMessage; return; }
                AppLatestVersion = info.LatestVersion;
                AppUpdateUrl = info.ReleaseUrl;
                AppUpdateMessage = info.IsUpdateAvailable ? $"Доступна новая версия: {info.LatestVersion}" : "";
                IsAppUpdateAvailable = info.IsUpdateAvailable;
                AppUpdateStatus = info.IsUpdateAvailable ? AppUpdateMessage : $"Обновлений нет. Последний опубликованный релиз: {info.LatestVersion}.";
            });
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _appUpdateCheckRunning, 0);
            if (!Application.Current.Dispatcher.HasShutdownStarted)
                await Application.Current.Dispatcher.InvokeAsync(() => IsCheckingAppUpdate = false);
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

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnecting)
        {
            AppendLog("[CLIENT] Отмена процесса подключения пользователем...");
            _connectCts?.Cancel();
            Disconnect();
            return;
        }

        if (IsConnected)
        {
            Disconnect();
            return;
        }

        var toggleRequest = ++_toggleRequest;
        var initialEpoch = _connectionEpoch;
        _connectionRequested = true;
        if (AutoFallbackFastestServer)
        {
            _initialSelection = true; IsConnecting = true;
            try {
                if (!await AutoSelectVerifiedAsync())
                {
                    if (_connectionRequested && toggleRequest == _toggleRequest)
                    {
                        _connectionRequested = false;
                        StatusText = "НЕТ ПРОВЕРЕННЫХ УЗЛОВ";
                        ConnectionDetail = "Автовыбор не подтвердил VPN. Выберите другой сервер или отключите автовыбор для ручного подключения.";
                    }
                    return;
                }
            }
            finally { _initialSelection = false; if (initialEpoch == _connectionEpoch) IsConnecting = false; }
            if (!_connectionRequested || toggleRequest != _toggleRequest) return;
        }
        if (!await _connectionLock.WaitAsync(1000))
        {
            AppendLog("[CLIENT] Блокировка занята, повторная попытка...");
            return;
        }

        try
        {
            if (!_connectionRequested || toggleRequest != _toggleRequest) return;
            _connectCts?.Dispose();
            _connectCts = new System.Threading.CancellationTokenSource();
            await ConnectInternalAsync(_connectCts.Token);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task ConnectAsync()
    {
        if (IsConnected) return;

        var toggleRequest = ++_toggleRequest; _connectionRequested = true;
        if (!await _connectionLock.WaitAsync(1000)) return;
        try
        {
            if (!_connectionRequested || toggleRequest != _toggleRequest) return;
            _connectCts?.Dispose();
            _connectCts = new System.Threading.CancellationTokenSource();
            await ConnectInternalAsync(_connectCts.Token);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task<string> PrepareConfigAsync(ServerNode node, AppSettings settings, NetworkUplink? uplink, int metricsPort, System.Threading.CancellationToken ct)
    {
        string? adMatcher = null;
        if (settings.EnableAdBlock) {
            try { adMatcher = await AdFilterService.PrepareAsync(AppContext.BaseDirectory, settings.AdBlockAllowlist, ct); }
            catch (Exception error) when (error is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException) { AppendLog("[CLIENT] Расширенный рекламный фильтр недоступен. Используются встроенные правила: " + error.Message); }
        }
        string configJson;
        configJson = _configGen.GenerateConfig(
            node: node,
            mode: settings.RoutingMode,
            enableSniffing: settings.EnableSniffing,
            routeOnlySniffing: settings.RouteOnlySniffing,
            blockQuic: settings.BlockQuic,
            blockIpv6: settings.BlockIpv6,
            dnsProvider: settings.DnsProvider,
            isFakeDns: settings.IsFakeDns,
            enableTlsFragment: settings.EnableTlsFragment,
            xhttpModeOverride: settings.XhttpModeOverride,
            socksPort: settings.SocksPort,
            httpPort: settings.HttpPort,
            bufferSizeKb: settings.BufferSizeKb,
            tcpCongestion: settings.TcpCongestion,
            enableAiBypass: settings.EnableAiBypass,
            customProxyRules: settings.CustomProxyRules,
            customDirectRules: settings.CustomDirectRules,
            enableTun: settings.EnableTunMode,
            perAppProxyList: settings.PerAppProxyList,
            perAppDirectList: settings.PerAppDirectList,
            uplinkInterface: uplink?.Name,
            metricsPort: metricsPort,
            enableAiXboxDns: settings.EnableAiXboxDns,
            enableAiDns: settings.EnableAiDns,
            aiRoutingMode: settings.AiRoutingMode,
            customDohUrl: settings.CustomDohUrl,
            customBlockRules: settings.CustomBlockRules,
            enableAdBlock: settings.EnableAdBlock,
            enableMux: settings.EnableMux,
            muxConcurrency: settings.MuxConcurrency,
            tlsFragmentLength: settings.TlsFragmentLength,
            tlsFragmentInterval: settings.TlsFragmentInterval,
            tlsFragmentPackets: settings.TlsFragmentPackets,
            shareLan: settings.ShareLan,
            aiDnsProvider: settings.AiDnsProvider,
            aiTlsFragment: settings.AiTlsFragment,
            adBlockMatcher: adMatcher,
            adBlockAllowlist: settings.AdBlockAllowlist
        );
        if ((uplink != null || XrayConfigGenerator.ServerAddressAlias(node) != null) && !System.Net.IPAddress.TryParse(node.Address, out _))
        {
            // A failing active TUN must not prevent resolving the replacement server.
            var addresses = await _probes.ResolveServerAsync(node.Address, ct);
            var v4 = addresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray();
            if (v4.Length == 0) throw new InvalidOperationException("Адрес VPN-сервера не имеет IPv4 записи.");
            var config = System.Text.Json.Nodes.JsonNode.Parse(configJson)!;
            var mapped = new System.Text.Json.Nodes.JsonArray();
            foreach (var ip in v4) mapped.Add(ip);
            config["dns"]!["hosts"]![node.Address] = mapped;
            var alias = XrayConfigGenerator.ServerAddressAlias(node);
            if (alias != null) config["dns"]!["hosts"]![alias] = mapped.DeepClone();
            configJson = config.ToJsonString();
        }
        return configJson;
    }

    private async Task ConnectInternalAsync(System.Threading.CancellationToken ct = default, bool replacing = false)
    {
        var previous = replacing ? _activeConnection : null;
        var epoch = ++_connectionEpoch;
        var selectionAtStart = _selectionRequest;
        var node = SelectedServer;
        var coreChanged = false;
        var attemptedSocksPort = previous?.Settings.SocksPort ?? Settings.SocksPort;
        IsConnecting = true;
        ConnectionChanging?.Invoke();
        StatusText = previous != null ? "ПЕРЕКЛЮЧЕНИЕ..." : "ПОДКЛЮЧЕНИЕ...";
        ConnectionDetail = "Подготовка соединения...";
        try
        {
            ct.ThrowIfCancellationRequested();
            if (node == null || !Servers.Contains(node) || !IsServerEnabled(node)) throw new InvalidOperationException("Выберите доступный сервер.");
            var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(Settings))!;
            SettingsCompatibility.Normalize(settings);
            attemptedSocksPort = settings.SocksPort;
            NetworkUplink? uplink = null;
            if (settings.EnableTunMode)
            {
                if (!_platform.IsAdministrator) throw new InvalidOperationException("Для TUN запустите приложение от администратора.");
                uplink = await _platform.GetUplinkAsync(ct);
                AppendLog($"[UPLINK] Физический интерфейс: {uplink.Name}; индекс {uplink.Index}; шлюз {uplink.Gateway}.");
            }
            using var metricsListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            metricsListener.Start();
            var metricsPort = ((System.Net.IPEndPoint)metricsListener.LocalEndpoint).Port;
            metricsListener.Stop();
            var config = await PrepareConfigAsync(node, settings, uplink, metricsPort, ct);
            ct.ThrowIfCancellationRequested();
            ConnectionDetail = "Запуск ядра...";
            if (!await _xrayManager.StartAsync(config, settings.HttpPort, settings.SocksPort, ct)) throw new IOException("Не удалось запустить ядро. Предыдущее соединение будет восстановлено, если оно было активно.");
            coreChanged = true;
            ct.ThrowIfCancellationRequested();
            // Readiness is local SOCKS availability. External HTTP diagnostics never gate connection startup.
            if (settings.EnableTunMode)
            {
                if (!_platform.IsTunRunning || previous?.Settings.SocksPort != settings.SocksPort)
                    if (!await _platform.StartTunAsync(settings.SocksPort, uplink!, settings.BlockIpv6, AppendLog, ct)) throw new IOException("Не удалось создать TUN.");
                _platform.DisableProxy();
            }
            else
            {
                _platform.EnableProxy(settings.HttpPort, settings.SocksPort);
                if (_platform.IsTunRunning) _platform.StopTun(AppendLog);
            }
            ct.ThrowIfCancellationRequested();
            if (!_xrayManager.IsRunning || (settings.EnableTunMode && !_platform.IsTunRunning)) throw new IOException("Сетевое ядро остановилось во время запуска.");
            _activeConnection = new(node, settings, config, metricsPort, uplink);
            _activeSettings = settings;
            if (previous == null) { _trafficMonitor.Start(metricsPort); StartSessionTimer(); }
            else _trafficMonitor.Resume(metricsPort);
            IsConnected = true;
            IsConnecting = false;
            foreach (var server in Servers) server.IsCurrentlyActive = server == node;
            StatusText = "ПОДКЛЮЧЕНО";
            EgressIp = "Проверяется…"; EgressLocation = "Выход выбранного VPN";
            ConnectionDetail = settings.EnableTunMode ? "TUN активен · Все приложения" : $"Системный прокси :{settings.HttpPort}";
            _balancerPolicy.Start(Settings);
            if (previous != null) _balancerPolicy.Switched();
            if (_enableBackgroundWork) StartHealthCheckTimer();
            TrayTooltipChanged?.Invoke("SplateVPN · Подключено");
            NotificationRequested?.Invoke(previous == null ? "VPN подключён" : "Сервер переключён", node.DisplayName);
            AppendLog($"[CONNECT] Готово: {node.DisplayName}; сеанс {epoch}.");
            if (_enableBackgroundWork) _ = DetectEgressAndHealthAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[CONNECT] {ex.Message}");
            if (previous != null && _connectionRequested && epoch == _connectionEpoch)
            {
                try
                {
                    var restoreUplink = previous.Settings.EnableTunMode ? await _platform.GetUplinkAsync(System.Threading.CancellationToken.None) : null;
                    var restoreConfig = previous.Config;
                    var uplinkChanged = restoreUplink != previous.Uplink;
                    if (uplinkChanged) restoreConfig = await PrepareConfigAsync(previous.Node, previous.Settings, restoreUplink, previous.MetricsPort, System.Threading.CancellationToken.None);
                    var restoredCore = coreChanged || !_xrayManager.IsRunning || uplinkChanged;
                    if (restoredCore)
                        if (!await _xrayManager.StartAsync(restoreConfig, previous.Settings.HttpPort, previous.Settings.SocksPort)) throw new IOException("Прежнее ядро не восстановлено.");
                    if (epoch != _connectionEpoch || !_connectionRequested) { DisconnectInternal(); return; }
                    if (previous.Settings.EnableTunMode)
                    {
                        if (!_platform.IsTunRunning || attemptedSocksPort != previous.Settings.SocksPort)
                            if (!await _platform.StartTunAsync(previous.Settings.SocksPort, restoreUplink!, previous.Settings.BlockIpv6, AppendLog, System.Threading.CancellationToken.None)) throw new IOException("Не удалось восстановить TUN.");
                        _platform.DisableProxy();
                    }
                    else { _platform.EnableProxy(previous.Settings.HttpPort, previous.Settings.SocksPort); if (_platform.IsTunRunning) _platform.StopTun(AppendLog); }
                    if (epoch != _connectionEpoch || !_connectionRequested) { DisconnectInternal(); return; }
                    _activeConnection = previous with { Config = restoreConfig, Uplink = restoreUplink }; _activeSettings = previous.Settings;
                    SelectedServer = previous.Node;
                    if (restoredCore) _trafficMonitor.Resume(previous.MetricsPort);
                    IsConnected = true; StatusText = "ПОДКЛЮЧЕНО";
                    ConnectionDetail = "Прежний сервер сохранён. Новый сервер не запустился.";
                    if (ex is not OperationCanceledException) _quality.Record(node?.Id ?? "", 0);
                    if (_enableBackgroundWork) StartHealthCheckTimer();
                    NotificationRequested?.Invoke("Смена сервера не завершена", "Соединение восстановлено на прежнем сервере.");
                    return;
                }
                catch (Exception rollback) { AppendLog($"[RECOVERY] {rollback.Message}"); }
            }
            if (epoch == _connectionEpoch)
            {
                // A newer selection is queued behind this attempt. Its cancellation
                // stops the old core but must preserve the user's connection request.
                if (ex is not OperationCanceledException || selectionAtStart == _selectionRequest)
                    _connectionRequested = false;
                DisconnectInternal();
                if (ex is not OperationCanceledException) { StatusText = "ОШИБКА"; ConnectionDetail = ex.Message; }
            }
        }
        finally { if (epoch == _connectionEpoch) IsConnecting = false; }
    }

    public void Disconnect()
    {
        _connectionRequested = false;
        ++_toggleRequest;
        ++_connectionEpoch; ++_selectionRequest;
        _autoSelectionCts?.Cancel(); _connectCts?.Cancel();
        DisconnectInternal();
    }

    private void DisconnectInternal()
    {
        StopSessionTimer(); StopHealthCheckTimer(); _trafficMonitor.Stop();
        _platform.StopTun(AppendLog); _platform.DisableProxy(); _xrayManager.Stop();
        _activeSettings = null; _activeConnection = null;
        IsConnected = false; IsConnecting = false;
        foreach (var server in Servers) server.IsCurrentlyActive = false;
        StatusText = "ОТКЛЮЧЕНО"; ConnectionDetail = "Нажмите для подключения";
        DownloadSpeed = UploadSpeed = "0 B/s"; DownloadSpeedRatio = UploadSpeedRatio = 0;
        SessionDuration = "00:00:00"; EgressIp = "Не определён"; CountryName = "Отключено";
        CountryFlag = "🌐"; EgressLocation = ""; ConnectionRtt = -1;
        TrayTooltipChanged?.Invoke("SplateVPN · Отключено");
        AppendLog("[CLIENT] Соединение остановлено.");
    }

    private int _recoveryRunning;
    private void OnProcessExited(int exitCode)
    {
        var epoch = _connectionEpoch;
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (epoch != _connectionEpoch || !_connectionRequested || _isSwitchingServer || IsConnecting || !IsConnected) return;
            if (_xrayManager.IsRunning && (_activeSettings?.EnableTunMode != true || _platform.IsTunRunning)) return;
            if (System.Threading.Interlocked.Exchange(ref _recoveryRunning, 1) != 0) return;
            await _connectionLock.WaitAsync();
            try
            {
                if (epoch != _connectionEpoch || !_connectionRequested) return;
                _isSwitchingServer = true;
                AppendLog($"[RECOVERY] Ядро завершилось ({exitCode}), восстанавливаем соединение.");
                _connectCts?.Dispose(); _connectCts = new System.Threading.CancellationTokenSource();
                await ConnectInternalAsync(_connectCts.Token, replacing: true);
            }
            finally { _isSwitchingServer = false; _connectionLock.Release(); System.Threading.Interlocked.Exchange(ref _recoveryRunning, 0); }
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

    private TimeSpan GetSubscriptionUpdateInterval()
    {
        return SubAutoUpdateInterval switch
        {
            "6h" => TimeSpan.FromHours(6),
            "12h" => TimeSpan.FromHours(12),
            "24h" => TimeSpan.FromHours(24),
            "48h" => TimeSpan.FromHours(48),
            _ => TimeSpan.FromHours(24)
        };
    }

    private void StartSubscriptionUpdateTimer()
    {
        _subscriptionUpdateTimer?.Stop();
        _subscriptionUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _subscriptionUpdateTimer.Tick += (_, _) =>
        {
            if (AutoUpdateSubscription && SubAutoUpdateInterval != "manual" && Subscriptions.Count > 0 && !_isUpdatingSubscription)
            {
                var interval = GetSubscriptionUpdateInterval();
                var needsUpdate = Subscriptions.Any(s => s.Enabled && s.AutoUpdate &&
                    (s.LastUpdated == null || (DateTime.Now - s.LastUpdated.Value) >= interval));

                if (needsUpdate)
                {
                    AppendLog("[SUBSCRIPTION] Периодическое плановое автообновление подписок по таймеру...");
                    _ = UpdateAllSubscriptionsQuietAsync();
                }
            }
        };
        _subscriptionUpdateTimer.Start();
    }

    public void RestartBalancer()
    {
        _balancerPolicy.Start(Settings);
        if (_enableBackgroundWork) StartHealthCheckTimer();
    }
    private void StartHealthCheckTimer()
    {
        StopHealthCheckTimer();
        if (!_enableBackgroundWork || !AutoFallbackFastestServer || !IsConnected || Settings.BalancerMode == "initial_only") return;
        _healthCheckTimer = new System.Threading.Timer(async _ => await RunHealthCheckAsync(), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(Settings.BalancerHealthSeconds));
    }
    private void StopHealthCheckTimer() { _healthCheckTimer?.Dispose(); _healthCheckTimer = null; _optimizationCts?.Cancel(); }
    private record BalancerSnapshot(bool Enabled, ServerNode? Node, int Epoch, int Request, int Port, System.Threading.CancellationToken Token);
    private int _healthCheckRunning;
    private async Task RunHealthCheckAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _healthCheckRunning, 1) != 0) return;
        try
        {
            var snapshot = await Application.Current.Dispatcher.InvokeAsync(() => new BalancerSnapshot(
                Enabled: AutoFallbackFastestServer && Settings.BalancerMode != "initial_only" && IsConnected && !_isSwitchingServer && !IsConnecting && !IsPinging,
                Node: _activeConnection?.Node, Epoch: _connectionEpoch, Request: _selectionRequest,
                Port: ActiveHttpPort, Token: _connectCts?.Token ?? System.Threading.CancellationToken.None
            ));
            if (!snapshot.Enabled || snapshot.Node == null) return;
            var ping = await _probes.CurrentAsync(snapshot.Port, 3000, snapshot.Token);
            if (snapshot.Token.IsCancellationRequested || snapshot.Epoch != _connectionEpoch) return;
            var decision = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (snapshot.Epoch != _connectionEpoch || snapshot.Request != _selectionRequest) return BalancerPolicy.Decision.Keep;
                if (ping > 0) { _quality.Record(snapshot.Node.Id, ping); snapshot.Node.Ping = ping; ServerPingUpdated?.Invoke(snapshot.Node); }
                return _balancerPolicy.Observe(ping, Settings, IsSpeedTestRunning, _currentDownloadBps, _probes.NetworkAvailable);
            });
            if (decision == BalancerPolicy.Decision.ConfirmFailure)
            {
                await Task.Delay(200, snapshot.Token);
                var confirmation = await _probes.CurrentAsync(snapshot.Port, 5000, snapshot.Token);
                if (snapshot.Epoch != _connectionEpoch || snapshot.Token.IsCancellationRequested) return;
                if (confirmation > 0) { _balancerPolicy.ConfirmedSuccess(); _quality.Record(snapshot.Node.Id, confirmation); return; }
                decision = BalancerPolicy.Decision.FindReserve;
            }
            if (decision is not (BalancerPolicy.Decision.FindReserve or BalancerPolicy.Decision.Optimize)) return;
            if (decision == BalancerPolicy.Decision.Optimize)
            {
                if (_optimizationTask is { IsCompleted: false }) return;
                _optimizationCts?.Dispose();
                var cancellation = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(snapshot.Token);
                _optimizationCts = cancellation;
                _optimizationTask = OptimizeAsync(snapshot, ping, cancellation);
            }
            else
            {
                _optimizationCts?.Cancel();
                await FindAndSwitchAsync(snapshot, ping, true, snapshot.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppendLog($"[BALANCER] Проверка не завершена: {ex.Message}. Соединение сохраняется."); }
        finally { System.Threading.Interlocked.Exchange(ref _healthCheckRunning, 0); }
    }

    private async Task OptimizeAsync(BalancerSnapshot snapshot, long ping, System.Threading.CancellationTokenSource cancellation)
    {
        try { await FindAndSwitchAsync(snapshot, ping, false, cancellation.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppendLog($"[BALANCER] Оптимизация не завершена: {ex.Message}"); }
        finally { if (ReferenceEquals(_optimizationCts, cancellation)) _optimizationCts = null; cancellation.Dispose(); }
    }
    private async Task FindAndSwitchAsync(BalancerSnapshot snapshot, long ping, bool dead, System.Threading.CancellationToken token)
    {
        var activeNode = snapshot.Node;
        if (activeNode == null) return;
        var candidates = await Application.Current.Dispatcher.InvokeAsync(() => Servers
                .Where(n => n.Id != activeNode.Id && IsServerEnabled(n) && !_quality.IsQuarantined(n.Id))
                .OrderBy(n => _quality.Score(n.Id, n.Ping)).ToList());
        if (!dead) candidates = candidates.Take(5).ToList();
        ServerNode? best = null; long bestScore=long.MaxValue,bestPing=-1;
        foreach(var batch in candidates.Chunk(10))
        {
            var measured=new System.Collections.Concurrent.ConcurrentDictionary<ServerNode,long>();
            await _probes.CandidatesAsync(batch,(node,delay)=>{measured[node]=delay;_quality.Record(node.Id,delay);},token);
            token.ThrowIfCancellationRequested();
            if(snapshot.Epoch!=_connectionEpoch || snapshot.Request!=_selectionRequest) return;
            var confirmed=await ConfirmBestCandidateAsync(measured,token);
            if(confirmed.Node==null)continue;
            best=confirmed.Node;bestPing=confirmed.Ping;bestScore=_quality.Score(best.Id,bestPing);break;
        }
        if (best == null) { AppendLog("[BALANCER] Проверенного резерва нет; VPN остаётся включённым."); return; }
        await (await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (snapshot.Epoch != _connectionEpoch || snapshot.Request != _selectionRequest || !IsConnected || !AutoFallbackFastestServer || Settings.BalancerMode == "initial_only" || !Servers.Contains(best) || !IsServerEnabled(best)) return;
            if (!dead && (IsSpeedTestRunning || _currentDownloadBps >= 100000 || Settings.BalancerMode != "balanced" || !BalancerPolicy.IsImprovement(ping, bestPing, Settings))) return;
            if (!dead && bestScore >= _quality.Score(activeNode.Id, ping, active: true)) return;
            if (!_balancerPolicy.CanSwitch(Settings, dead)) return;
            if (dead) _quality.Record(activeNode.Id, 0);
            AppendLog($"[BALANCER] Проверенный резерв: {best.DisplayName} ({bestPing} ms).");
            await SelectServerAsync(best);
        }));
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
        await RefreshSubscriptionAsync(sub, notify: true);
    }

    private readonly System.Threading.SemaphoreSlim _subscriptionLock = new(1, 1);
    public event Action? SubscriptionsUpdated;
    public bool IsServerEnabled(ServerNode node) => string.IsNullOrEmpty(node.SubscriptionId) || Subscriptions.FirstOrDefault(s => s.Id == node.SubscriptionId)?.Enabled != false;

    public void SetSubscriptionOptions(SubscriptionItem sub, bool? enabled, bool? autoUpdate)
    {
        if (!Subscriptions.Contains(sub)) return;
        var previousEnabled = sub.Enabled; var previousAuto = sub.AutoUpdate;
        if (enabled.HasValue) sub.Enabled = enabled.Value;
        if (autoUpdate.HasValue) sub.AutoUpdate = autoUpdate.Value;
        try { _storage.SaveSubscriptions(Subscriptions.ToList()); }
        catch { sub.Enabled = previousEnabled; sub.AutoUpdate = previousAuto; throw; }
        if (!sub.Enabled && SelectedServer?.SubscriptionId == sub.Id)
        {
            if (IsConnected || IsConnecting) Disconnect();
            SelectedServer = Servers.FirstOrDefault(IsServerEnabled);
        }
        SubscriptionsUpdated?.Invoke();
        ServersBulkUpdated?.Invoke();
    }

    public async Task RestorePresetSubscriptionsAsync()
    {
        await _subscriptionLock.WaitAsync();
        try
        {
            var stagedSubscriptions = Subscriptions.ToList(); var stagedServers = Servers.ToList();
            var count = PresetSubscriptions.Seed(stagedSubscriptions, stagedServers, System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "presets"));
            _storage.SaveCatalog(stagedServers, stagedSubscriptions);
            foreach (var added in stagedSubscriptions.Where(s => !Subscriptions.Contains(s))) Subscriptions.Add(added);
            foreach (var added in stagedServers.Where(s => !Servers.Contains(s))) Servers.Add(added);
            Settings.PresetSubscriptionsInitialized = true;
            _storage.SaveSettings(Settings);
            if (SelectedServer == null) SelectedServer = Servers.FirstOrDefault(IsServerEnabled);
            UpdateSubscriptionGroups();
            ApplyServerFilters();
            SubscriptionsUpdated?.Invoke();
            ServersBulkUpdated?.Invoke();
            NotificationRequested?.Invoke("Встроенные подписки", $"Добавлено узлов: {count}. Существующие подписки сохранены.");
        }
        finally { _subscriptionLock.Release(); }
    }

    private async Task RefreshSubscriptionAsync(SubscriptionItem? sub, bool notify)
    {
        if (sub == null || string.IsNullOrWhiteSpace(sub.Url)) return;
        await _subscriptionLock.WaitAsync();
        if (!Subscriptions.Contains(sub)) { _subscriptionLock.Release(); return; }
        sub.IsUpdating = true;
        _isUpdatingSubscription = true;
        SubscriptionsUpdated?.Invoke();
        AppendLog($"[SUBSCRIPTION] Обновление подписки «{sub.Name}»...");

        try
        {
            SubscriptionResult subResult;
            try
            {
                subResult = await SubscriptionService.FetchSubscriptionAsync(sub.Url, AppendLog, IsConnected ? ActiveHttpPort : null);
                if (subResult.Nodes.Count == 0) throw new InvalidOperationException("Источник не содержит серверов.");
            }
            catch when (!string.IsNullOrWhiteSpace(sub.FallbackUrl))
            {
                AppendLog("[SUBSCRIPTION] Загружаем резервный источник.");
                subResult = await SubscriptionService.FetchSubscriptionAsync(sub.FallbackUrl, AppendLog, IsConnected ? ActiveHttpPort : null);
            }
            var newNodes = subResult.Nodes;
            if (!Subscriptions.Contains(sub)) return; // Removed while the download was in flight.

            if (newNodes.Count == 0)
            {
                AppendLog($"[SUBSCRIPTION WARNING] В подписке «{sub.Name}» не обнаружено серверов.");
                if (notify) NotifyUser($"В подписке «{sub.Name}» не найдено серверов. Проверьте правильность ссылки.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // CRITICAL FIX: Replace existing old nodes of this subscription so that changed transports (e.g. TCP -> XHTTP) are applied!
            var currentActive = SelectedServer;
            var oldNodes = Servers.Where(s =>
                PresetSubscriptions.Owns(sub, s)
            ).ToList();

            newNodes = PresetSubscriptions.Merge(oldNodes, newNodes);

            foreach (var node in newNodes) { node.SubscriptionId = sub.Id; node.SubscriptionGroup = sub.Name; }
            var updatedSub = System.Text.Json.JsonSerializer.Deserialize<SubscriptionItem>(System.Text.Json.JsonSerializer.Serialize(sub))!;
            updatedSub.DataLimit = subResult.Total; updatedSub.DataUsed = subResult.Download + subResult.Upload;
            updatedSub.ExpireTime = subResult.Expire; updatedSub.LastUpdated = DateTime.Now; updatedSub.NodeCount = newNodes.Count;
            _storage.SaveCatalog(Servers.Except(oldNodes).Concat(newNodes).ToList(), Subscriptions.Select(s => s == sub ? updatedSub : s).ToList());

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

            UpdateSubscriptionGroups();
            ApplyServerFilters();
            ServersBulkUpdated?.Invoke();

            // Only replace the selected node if this subscription actually owned it.
            if (currentActive != null && oldNodes.Contains(currentActive) && SelectedServer == currentActive)
            {
                var replacement = newNodes.FirstOrDefault(s => s.Address == currentActive.Address && s.Port == currentActive.Port && s.Protocol == currentActive.Protocol)
                    ?? newNodes.FirstOrDefault(s => s.DisplayName == currentActive.DisplayName)
                    ?? newNodes.FirstOrDefault();
                if (replacement != null) await SelectServerAsync(replacement);
            }
            if (SelectedServer == null) SelectedServer = Servers.FirstOrDefault(IsServerEnabled);

            AppendLog($"[SUBSCRIPTION] «{sub.Name}»: успешно обновлено, узлов: {newNodes.Count}");
            if (notify) NotificationRequested?.Invoke("Подписка обновлена", $"«{sub.Name}»: загружено {newNodes.Count} узлов.");
        }
        catch (Exception ex)
        {
            AppendLog($"[SUBSCRIPTION ERROR] {ex.Message}");
            if (notify) NotifyUser($"Ошибка обновления подписки «{sub.Name}»:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            sub.IsUpdating = false;
            _isUpdatingSubscription = false;
            _subscriptionLock.Release();
            SubscriptionsUpdated?.Invoke();
        }
    }

    [RelayCommand]
    private async Task UpdateAllSubscriptionsAsync()
    {
        if (Subscriptions.Count == 0)
        {
            NotifyUser("Список подписок пуст. Нажмите «+ Добавить подписку».", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var sub in Subscriptions.ToList())
        {
            await UpdateSubscriptionAsync(sub);
        }
    }

    public async Task UpdateAllSubscriptionsQuietAsync()
    {
        foreach (var sub in Subscriptions.Where(s => s.Enabled && s.AutoUpdate).ToList())
            await RefreshSubscriptionAsync(sub, notify: false);
    }

    [RelayCommand]
    private async Task DeleteSubscriptionAsync(SubscriptionItem? sub)
    {
        if (sub == null) return;
        var res = await ConfirmUserAsync($"Удалить подписку «{sub.Name}» и все входящие в неё серверы ({sub.NodeCount} шт.)?",
            "Удаление подписки");
        if (res)
        {
            var nodesToRemove = Servers.Where(s =>
                PresetSubscriptions.Owns(sub, s)
            ).ToList();

            _storage.SaveCatalog(Servers.Except(nodesToRemove).ToList(), Subscriptions.Where(s => s != sub).ToList());

            if ((IsConnected || IsConnecting) && SelectedServer != null && nodesToRemove.Contains(SelectedServer)) Disconnect();
            foreach (var node in nodesToRemove)
            {
                Servers.Remove(node);
            }

            Subscriptions.Remove(sub);
            UpdateSubscriptionGroups();
            ApplyServerFilters();

            if (SelectedServer != null && nodesToRemove.Contains(SelectedServer))
            {
                SelectedServer = Servers.FirstOrDefault(IsServerEnabled);
            }

            AppendLog($"[SUBSCRIPTION] Удалена подписка «{sub.Name}» и {nodesToRemove.Count} узлов.");
            SubscriptionsUpdated?.Invoke();
            ServersBulkUpdated?.Invoke();
        }
    }

    // ============================================================
    // Universal Link Import & Clipboard
    // ============================================================

    [RelayCommand]
    private async Task AddFromClipboardAsync()
    {
        try
        {
            var text = Clipboard.GetText()?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                NotifyUser("Буфер обмена пуст. Скопируйте ссылку на сервер (vless://, trojan://, vmess://) или ссылку на подписку (https://...).",
                    "Буфер обмена", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await ProcessAnyLinkAsync(text, fromClipboard: true);
        }
        catch (Exception ex)
        {
            NotifyUser($"Ошибка буфера обмена: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task AddManualServerAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualServerLink))
        {
            NotifyUser("Вставьте ссылку на сервер (vless://, trojan://, vmess://, ss://) или подписку (https://...).",
                "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var link = ManualServerLink.Trim();
        ManualServerLink = string.Empty;
        await ProcessAnyLinkAsync(link, fromClipboard: false);
    }

    public async Task ProcessAnyLinkAsync(string input, bool fromClipboard = false, string? subscriptionName = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        input = LinkParser.UnwrapImportLink(input);

        // 1. Subscription URL
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var existing = Subscriptions.FirstOrDefault(s => s.Url.Equals(input, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                var res = await ConfirmUserAsync($"Эта подписка уже добавлена («{existing.Name}»).\nОбновить её прямо сейчас?", "Подписка найдена");
                if (res)
                {
                    _ = UpdateSubscriptionAsync(existing);
                }
                return;
            }

            if (!Uri.TryCreate(input, UriKind.Absolute, out var subscriptionUri))
            {
                NotifyUser("Проверьте адрес подписки.", "Неверная ссылка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var newSub = new SubscriptionItem
            {
                Name = string.IsNullOrWhiteSpace(subscriptionName) ? subscriptionUri.Host : subscriptionName.Trim(),
                Url = input,
                AutoUpdate = true
            };
            Subscriptions.Add(newSub);
            _storage.SaveSubscriptions(Subscriptions.ToList());
            UpdateSubscriptionGroups();
            await UpdateSubscriptionAsync(newSub);
            return;
        }

        var parsed = LinkParser.ParseMultiple(input);
        var added = new List<ServerNode>();
        foreach (var node in parsed)
        {
            if (Servers.Any(s => LinkParser.SameEndpoint(s, node)) || added.Any(s => LinkParser.SameEndpoint(s, node))) continue;
            node.IsCustom = true;
            node.SubscriptionId = "";
            node.SubscriptionGroup = "Личные";
            added.Add(node);
        }
        if (added.Count == 0)
        {
            var message = parsed.Count > 0 ? "Эти серверы уже есть в списке." :
                "Не удалось распознать конфигурацию. Поддерживаются VLESS, Trojan, VMess, Shadowsocks, Base64, Clash, JSON и HTTP(S) подписки. Для зашифрованных Happ/INCY нужна обычная HTTPS-ссылка.";
            NotifyUser(message, "Импорт", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // Persist before mutating the observable list; a failed write cannot create phantom nodes.
        _storage.SaveServers(Servers.Concat(added).ToList());
        foreach (var node in added) Servers.Add(node);
        ApplyServerFilters();
        if (SelectedServer == null || (added.Count == 1 && !IsConnected && !IsConnecting)) await SelectServerAsync(added[0]);
        ServersBulkUpdated?.Invoke();
        NotificationRequested?.Invoke("Импорт завершён", $"Добавлено серверов: {added.Count}.");
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

    internal async Task PingAllServersInternalAsync()
    {
        if (IsPinging || Servers.Count == 0) return;
        IsPinging = true;
        var nodes = Servers.Where(IsServerEnabled).ToList();
        var results = new System.Collections.Concurrent.ConcurrentDictionary<ServerNode, long>();
        var http = PingMode == "http";
        using var cancellation = new System.Threading.CancellationTokenSource();
        _pingCancellation = cancellation;
        var token = cancellation.Token;
        var completed = 0;
        void Report(ServerNode node, long delay)
        {
            token.ThrowIfCancellationRequested();
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (token.IsCancellationRequested || !Servers.Contains(node)) return;
                results[node] = delay;
                node.Ping = delay > 0 ? delay : -1;
                if (http) _quality.Record(node.Id, delay);
                ServerPingUpdated?.Invoke(node);
                PingProgressUpdated?.Invoke(++completed, nodes.Count, true);
            });
        }
        try
        {
            PingProgressUpdated?.Invoke(0, nodes.Count, true);
            AppendLog($"[PING] {(http ? "HTTPS" : "TCP")}: {nodes.Count} узлов.");
            if (http) await _probes.CandidatesAsync(nodes, Report, token);
            else
            {
                var route = await ProbeRoute.CaptureAsync(token);
                using var limit = new System.Threading.SemaphoreSlim(8);
                await Task.WhenAll(nodes.Select(async node => { await limit.WaitAsync(token); try { var delay = await PingService.MeasureTcpOnRouteAsync(node.Address, node.Port, 3000, route, token); route.Validate(); Report(node, delay); } finally { limit.Release(); } }));
                route.Validate();
            }
            AppendLog($"[PING] Готово: {completed}/{nodes.Count}, ответили {results.Count(p => p.Value > 0)}.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { AppendLog($"[PING] Остановлено: {completed}/{nodes.Count}."); }
        catch (Exception ex) { AppendLog($"[PING] {ex.Message}"); NotificationRequested?.Invoke("Проверка не завершена", ex.Message); }
        finally
        {
            _pingCancellation = null;
            IsPinging = false;
            PingProgressUpdated?.Invoke(completed, nodes.Count, false);
            try { await _storage.SaveServersAsync(Servers.ToList()); }
            finally { ApplyServerFilters(); ServersBulkUpdated?.Invoke(); }
        }
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
            var corePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "core", "xray.exe");
            int? connectedPort = (IsConnected && SelectedServer == node) ? ActiveHttpPort : null;

            long latency = PingMode == "tcp" ? await PingService.MeasureTcpLatencyAsync(node.Address, node.Port, 3000) : await PingService.MeasureRealPingSingleAsync(node, corePath, connectedPort, 3500);
            node.Ping = latency > 0 ? latency : -1;
            await _storage.SaveServersAsync(Servers.ToList());
            Application.Current?.Dispatcher?.InvokeAsync(() => ServerPingUpdated?.Invoke(node));
        }
        catch
        {
            node.Ping = 0;
            Application.Current?.Dispatcher?.InvokeAsync(() => ServerPingUpdated?.Invoke(node));
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
            node.Encryption = res.Encryption;
            node.Alpn = res.Alpn;
            node.Mldsa65Verify = res.Mldsa65Verify;
            node.VerifyPeerCertByName = res.VerifyPeerCertByName;
            node.PinnedPeerCertSha256 = res.PinnedPeerCertSha256;
            node.XmuxMaxConnections = res.XmuxMaxConnections;
            node.XmuxMaxRequestTimes = res.XmuxMaxRequestTimes;
            node.XmuxMaxReusableSecs = res.XmuxMaxReusableSecs;
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
            node.Mode = res.Mode;
            node.Padding = res.Padding;
            node.RawLink = ""; // Edited fields are authoritative; do not re-import the old link on startup.
            node.SubscriptionGroup = res.SubscriptionGroup;

            _storage.SaveServers(Servers.ToList());
            ApplyServerFilters();
            ServersBulkUpdated?.Invoke();
            OnPropertyChanged(nameof(SelectedServer));
            AppendLog($"[SERVER] Отредактирован: {node.DisplayName}");
            NotifyUser("Изменения сохранены. Для активного сервера переподключитесь.", "Сервер обновлён");
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
            node.IsCustom = true;
            node.SubscriptionGroup = "Личные";
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

    private int _ipDetectionEpoch;
    public async Task DetectEgressAndHealthAsync()
    {
        var epoch = _connectionEpoch;
        if (IsDetectingIp && _ipDetectionEpoch == epoch) return;
        _ipDetectionEpoch = epoch;
        IsDetectingIp = true;
        try
        {
            int socksPort = IsConnected ? ActiveSocksPort : 0;
            var ipTask = IpLocationService.FetchEgressIpAsync(socksPort);
            var connTask = IpLocationService.CheckConnectivityAsync(socksPort);

            await Task.WhenAll(ipTask, connTask);

            var ipDetails = await ipTask;
            var (connSuccess, rtt) = await connTask;

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                if (epoch != _connectionEpoch) return;
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
                    EgressIp = "Не удалось определить";
                    EgressLocation = IsConnected ? "Выход VPN не подтверждён · повторите проверку" : "Нет данных";
                }
                ConnectionRtt = connSuccess ? rtt : -1;
                if (IsConnected && !IsConnecting && StatusText != "ОШИБКА МАРШРУТА")
                {
                    StatusText = "ПОДКЛЮЧЕНО";
                    if (ipDetails?.IsSuccess != true && !connSuccess) ConnectionDetail = "Ядро запущено, но сайты не ответили. VPN остаётся включённым.";
                }
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[GEO IP ERROR] {ex.Message}");
        }
        finally
        {
            if (_ipDetectionEpoch == epoch) IsDetectingIp = false;
        }
    }

    [RelayCommand]
    private void DeleteServer(ServerNode? node)
    {
        if (node != null && Servers.Contains(node))
        {
            if (SelectedServer == node && (IsConnected || IsConnecting)) Disconnect();
            Servers.Remove(node);
            _storage.SaveServers(Servers.ToList());
            ApplyServerFilters();
            if (SelectedServer == node)
                SelectedServer = Servers.FirstOrDefault(IsServerEnabled);
        }
    }

    [RelayCommand]
    private void ClearAllServers()
    {
        Disconnect();
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
            var password = new BackupPasswordWindow(exporting: true) { Owner = Application.Current?.MainWindow };
            if (password.ShowDialog() != true) return;
            var passphrase = password.Passphrase;
            await _configService.ExportAsync(dlg.FileName, passphrase);
            NotifyUser("Конфигурация экспортирована!", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var passphrase = "";
            if (!ConfigExportImportService.IsLegacyBackup(dlg.FileName))
            {
                var password = new BackupPasswordWindow(exporting: false) { Owner = Application.Current?.MainWindow };
                if (password.ShowDialog() != true) return;
                passphrase = password.Passphrase;
            }
            await _configService.ImportAsync(dlg.FileName, passphrase);
            Disconnect();
            LoadData();
            NotifyUser("Конфигурация импортирована!", "Импорт", MessageBoxButton.OK, MessageBoxImage.Information);
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
                NotifyUser("Базы гео-маршрутизации успешно обновлены!", "Обновление баз", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                NotifyUser("Не удалось обновить базы. Проверьте подключение к интернету или логи.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        if (!SubscriptionGroups.Contains(SelectedSubscriptionGroup) && !Subscriptions.Any(s => s.Id == SelectedSubscriptionGroup))
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
        await ProcessQrResultAsync(result);
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
            await ProcessQrResultAsync(result);
        }
    }

    private async Task ProcessQrResultAsync(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            NotifyUser("QR-код на экране не обнаружен или не содержит данных.", "QR Сканер", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        link = link.Trim();
        AppendLog("[QR] Ссылка распознана.");
        await ProcessAnyLinkAsync(link, fromClipboard: false);
    }

    // ============================================================
    // Windows Service Mode
    // ============================================================

    [RelayCommand]
    private void InstallWindowsService()
    {
        var coreDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "core");
        System.IO.Directory.CreateDirectory(coreDir);
        var corePath = System.IO.Path.Combine(coreDir, "xray.exe");
        var configPath = System.IO.Path.Combine(coreDir, "config.json");

        if (SelectedServer == null)
        {
            NotifyUser("Для установки фоновой службы Windows выберите сервер из списка.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
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
                socksPort: ActiveSocksPort,
                httpPort: ActiveHttpPort,
                bufferSizeKb: Settings.BufferSizeKb,
                tcpCongestion: Settings.TcpCongestion,
                enableAiBypass: Settings.EnableAiBypass,
                customProxyRules: Settings.CustomProxyRules,
                customDirectRules: Settings.CustomDirectRules,
                enableTun: Settings.EnableTunMode,
                perAppProxyList: Settings.PerAppProxyList,
                perAppDirectList: Settings.PerAppDirectList,
                enableAiXboxDns: Settings.EnableAiXboxDns
            );
            System.IO.File.WriteAllText(configPath, configJson);
        }
        catch (Exception ex)
        {
            NotifyUser($"Не удалось сгенерировать конфигурационный файл службы: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var ok = WindowsServiceHelper.InstallService(corePath, configPath);
        if (ok)
        {
            NotifyUser("Фоновая служба Windows 'SplateVPNService' успешно зарегистрирована!", "Служба Windows", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            NotifyUser("Не удалось установить службу. Запустите приложение от имени Администратора.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void UninstallWindowsService()
    {
        var ok = WindowsServiceHelper.UninstallService();
        if (ok)
        {
            NotifyUser("Фоновая служба Windows успешно удалена.", "Служба Windows", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            NotifyUser("Не удалось удалить службу.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ============================================================
    // Logs
    // ============================================================

    private readonly StringBuilder _logBuffer = new();
    private readonly object _logLock = new();
    private bool _isLogFlushScheduled = false;

    [RelayCommand]
    private void ClearLogs()
    {
        lock (_logLock)
        {
            _logBuffer.Clear();
            Logs = string.Empty;
        }
    }

    private static async Task ProcessLogFileQueueAsync()
    {
        var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "splate_debug.log");
        var batch = new List<string>(64);
        var reader = _logFileChannel.Reader;

        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                batch.Clear();
                while (reader.TryRead(out var line))
                {
                    batch.Add(line);
                    if (batch.Count >= 100) break;
                }

                if (batch.Count > 0)
                {
                    try
                    {
                        await System.IO.File.AppendAllLinesAsync(logPath, batch).ConfigureAwait(false);
                    }
                    catch { }
                }

                await Task.Delay(80).ConfigureAwait(false);
            }
        }
        catch { }
    }

    public async Task<string> CheckAiConnectivityAsync()
    {
        if (!IsConnected) return "Сначала подключитесь к VPN.";
        var epoch = _connectionEpoch;
        var mode = ActiveTunMode ? "TUN" : "Системный прокси (приложения могут игнорировать его)";
        var aiRoute = ActiveAiDns ? $"SmartDNS · {SmartDnsProviders.ForAi(_activeSettings!.AiDnsProvider).Label}, напрямую" : _activeSettings?.EnableAiBypass == true ? "выбранный VPN-сервер" : "общие правила маршрутизации";
        var ipTask = IpLocationService.FetchEgressIpAsync(ActiveSocksPort, 5000);
        var token = _connectCts?.Token ?? System.Threading.CancellationToken.None;
        var renderedTask = GeminiPageVerifier.CheckAsync(ActiveSocksPort, token);
        var targetsTask = AiServiceProbe.CheckAsync(ActiveSocksPort, token);
        await Task.WhenAll(targetsTask, renderedTask);
        var targets = await targetsTask;
        var rendered = await renderedTask;
        if (rendered.Outcome != AiDnsOutcome.Failed || targets[0].Outcome == AiDnsOutcome.Accessible) targets[0] = rendered;
        var ip = await ipTask;
        if (epoch != _connectionEpoch || !IsConnected) return "Соединение изменилось во время проверки. Повторите проверку.";
        var lines = targets.Select(t => $"{t.Service}: {t.Outcome switch { AiDnsOutcome.Accessible => "публичная страница доступна", AiDnsOutcome.Limited => "ограничение / вход / CAPTCHA", AiDnsOutcome.GeoBlocked => "региональный запрет", _ => "нет подтверждения доступа" }} — {t.Detail}");
        var result = $"Режим: {mode}.\nВыход выбранного VPN: {(ip.IsSuccess ? $"{ip.Ip} ({ip.Country})" : "не определён — это не подтверждение защиты")}.\nМаршрут нейросетей: {aiRoute}.\n" + string.Join("\n", lines) + "\nПроверяются публичные страницы, без входа в аккаунт. SmartDNS не меняет общий IP; Smart RU оставляет российские ресурсы напрямую. При региональном запрете выберите VPN-сервер в другой стране либо другой SmartDNS.";
        AppendLog("[DIAG] " + result);
        return result;
    }

    public event Action<string, string, string>? UiNotificationRequested;
    public Func<string, string, Task<bool>>? ConfirmationRequested { get; set; }

    private void NotifyUser(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information)
    {
        var kind = image == MessageBoxImage.Error ? "error" : image == MessageBoxImage.Warning ? "warning" : "info";
        AppendLog($"[CLIENT] {title}: {message}");
        UiNotificationRequested?.Invoke(title, message, kind);
    }

    private Task<bool> ConfirmUserAsync(string message, string title) =>
        ConfirmationRequested?.Invoke(title, message) ?? Task.FromResult(false);

    private long _bindingFailureEpoch = -1;
    private DateTime _lastBindingRecovery;
    private void OnCoreLogMessage(string message)
    {
        AppendLog(message);
        if (!message.Contains("failed to find the interface", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("failed to bind to interface", StringComparison.OrdinalIgnoreCase)) return;
        var epoch = _connectionEpoch;
        _bindingFailureEpoch = epoch;
        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            if (epoch != _connectionEpoch || !IsConnected || !ActiveTunMode) return;
            StatusText = "ОШИБКА МАРШРУТА";
            ConnectionDetail = "Изменился сетевой адаптер. Восстанавливаем выход из TUN…";
            if (IsConnecting || _isSwitchingServer || DateTime.UtcNow - _lastBindingRecovery < TimeSpan.FromSeconds(30)) return;
            _lastBindingRecovery = DateTime.UtcNow;
            try { if (_activeConnection?.Node is { } node) await SwitchServerAsync(node); }
            catch (Exception e) { AppendLog("[UPLINK RECOVERY] " + e.Message); }
            if (_bindingFailureEpoch == _connectionEpoch && IsConnected)
            {
                StatusText = "ОШИБКА МАРШРУТА";
                ConnectionDetail = "Не удалось привязать выход к Wi-Fi / Ethernet. Переподключите VPN после проверки адаптера.";
            }
        });
    }

    private void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var fullLine = $"[{timestamp}] {message}";

        _logFileChannel.Writer.TryWrite(fullLine);

        lock (_logLock)
        {
            _logBuffer.Append(fullLine).Append('\n');
            if (_logBuffer.Length > 40000)
            {
                _logBuffer.Remove(0, 20000);
            }
        }

        LogMessageReceived?.Invoke(fullLine);

        if (!_isLogFlushScheduled)
        {
            _isLogFlushScheduled = true;
            Application.Current?.Dispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                lock (_logLock)
                {
                    _isLogFlushScheduled = false;
                    Logs = _logBuffer.ToString();
                }
            });
        }
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
