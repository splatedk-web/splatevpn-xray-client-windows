using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SplateClientDesktop.Models;

public class ServerNode : INotifyPropertyChanged
{
    private bool _isFavorite;
    public bool IsFavorite { get => _isFavorite; set => SetField(ref _isFavorite, value); }
    private string _id = Guid.NewGuid().ToString();
    private string _name = "";
    private string _protocol = "vless"; // vless, trojan, vmess, ss
    private string _address = "";
    private int _port = 443;
    private string _uuid = "";
    private string _flow = "";
    private string _encryption = "none";
    private string _network = "tcp"; // tcp, xhttp, ws, grpc
    private string _security = "reality"; // reality, tls, none
    private string _sni = "";
    private string _fingerprint = "chrome";
    private string _publicKey = "";
    private string _shortId = "";
    private string _spiderX = "";
    private string _path = "/";
    private string _host = "";
    private string _mode = "auto"; // for xhttp
    private string _padding = "100-1000";
    private long _ping = 0;
    private string _rawLink = "";
    private string _alpn = "";
    private string? _verifyPeerCertByName;
    private string? _pinnedPeerCertSha256;
    private string? _mldsa65Verify;
    private int? _xmuxMaxConnections;
    private string? _xmuxMaxRequestTimes;
    private string? _xmuxMaxReusableSecs;
    private bool _isSelected = false;
    private bool _isCurrentlyActive = false;
    private bool _isTestingPing = false;
    private bool _isCustom = false;
    private string _subscriptionGroup = "Основное";

    public bool IsCustom
    {
        get => _isCustom;
        set => SetField(ref _isCustom, value);
    }

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Protocol
    {
        get => _protocol;
        set
        {
            if (SetField(ref _protocol, value))
            {
                OnPropertyChanged(nameof(DisplaySubtitle));
                OnPropertyChanged(nameof(ProtocolUpper));
                OnPropertyChanged(nameof(ProtocolBadgeBackground));
                OnPropertyChanged(nameof(ProtocolBadgeForeground));
            }
        }
    }

    public string Address
    {
        get => _address;
        set
        {
            if (SetField(ref _address, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(DisplaySubtitle));
            }
        }
    }

    public int Port
    {
        get => _port;
        set
        {
            if (SetField(ref _port, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(DisplaySubtitle));
            }
        }
    }

    public string Uuid
    {
        get => _uuid;
        set => SetField(ref _uuid, value);
    }

    public string Flow
    {
        get => _flow;
        set => SetField(ref _flow, value);
    }

    public string Encryption
    {
        get => _encryption;
        set => SetField(ref _encryption, value);
    }

    public string Network
    {
        get => _network;
        set
        {
            if (SetField(ref _network, value))
            {
                OnPropertyChanged(nameof(DisplaySubtitle));
            }
        }
    }

    public string Security
    {
        get => _security;
        set
        {
            if (SetField(ref _security, value))
            {
                OnPropertyChanged(nameof(DisplaySubtitle));
                OnPropertyChanged(nameof(ProtocolBadgeBackground));
                OnPropertyChanged(nameof(ProtocolBadgeForeground));
            }
        }
    }

    public string Sni
    {
        get => _sni;
        set => SetField(ref _sni, value);
    }

    public string Fingerprint
    {
        get => _fingerprint;
        set => SetField(ref _fingerprint, value);
    }

    public string PublicKey
    {
        get => _publicKey;
        set => SetField(ref _publicKey, value);
    }

    public string ShortId
    {
        get => _shortId;
        set => SetField(ref _shortId, value);
    }

    public string SpiderX
    {
        get => _spiderX;
        set => SetField(ref _spiderX, value);
    }

    public string Path
    {
        get => _path;
        set => SetField(ref _path, value);
    }

    public string Host
    {
        get => _host;
        set => SetField(ref _host, value);
    }

    public string Mode
    {
        get => _mode;
        set => SetField(ref _mode, value);
    }

    public string XhttpMode
    {
        get => _mode;
        set => SetField(ref _mode, value);
    }

    public string Padding
    {
        get => _padding;
        set => SetField(ref _padding, value);
    }

    public string XhttpExtra
    {
        get => _padding;
        set => SetField(ref _padding, value);
    }

    public string Password
    {
        get => _uuid;
        set => SetField(ref _uuid, value);
    }

    public long Ping
    {
        get => _ping;
        set
        {
            if (SetField(ref _ping, value))
            {
                OnPropertyChanged(nameof(PingDisplay));
                OnPropertyChanged(nameof(PingColor));
            }
        }
    }

    public string RawLink
    {
        get => _rawLink;
        set => SetField(ref _rawLink, value);
    }

    public string Alpn
    {
        get => _alpn;
        set => SetField(ref _alpn, value);
    }

    public string? VerifyPeerCertByName
    {
        get => _verifyPeerCertByName;
        set => SetField(ref _verifyPeerCertByName, value);
    }

    public string? PinnedPeerCertSha256
    {
        get => _pinnedPeerCertSha256;
        set => SetField(ref _pinnedPeerCertSha256, value);
    }

    public string? Mldsa65Verify
    {
        get => _mldsa65Verify;
        set => SetField(ref _mldsa65Verify, value);
    }

    public int? XmuxMaxConnections
    {
        get => _xmuxMaxConnections;
        set => SetField(ref _xmuxMaxConnections, value);
    }

    public string? XmuxMaxRequestTimes
    {
        get => _xmuxMaxRequestTimes;
        set => SetField(ref _xmuxMaxRequestTimes, value);
    }

    public string? XmuxMaxReusableSecs
    {
        get => _xmuxMaxReusableSecs;
        set => SetField(ref _xmuxMaxReusableSecs, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCurrentlyActive
    {
        get => _isCurrentlyActive;
        set => SetField(ref _isCurrentlyActive, value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsTestingPing
    {
        get => _isTestingPing;
        set => SetField(ref _isTestingPing, value);
    }

    private string? _subscriptionId;

    public string? SubscriptionId
    {
        get => _subscriptionId;
        set => SetField(ref _subscriptionId, value);
    }

    public string SubscriptionGroup
    {
        get => _subscriptionGroup;
        set => SetField(ref _subscriptionGroup, value);
    }

    // UI Helpers
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : $"{Address}:{Port}";
    public string DisplaySubtitle => $"{Protocol.ToUpperInvariant()} · {Network.ToUpperInvariant()} · {(string.IsNullOrWhiteSpace(Security) || Security.Equals("none", StringComparison.OrdinalIgnoreCase) ? "DIRECT" : Security.ToUpperInvariant())}";
    public string ProtocolUpper => Protocol.ToUpperInvariant();
    public string PingDisplay => Ping > 0 ? $"{Ping} ms" : "—";
    public string PingColor => Ping <= 0 ? "#64748B" : Ping < 150 ? "#10E79D" : Ping < 300 ? "#FFB300" : "#FF3366";

    public string Flag
    {
        get
        {
            var text = DisplayName.ToUpperInvariant();
            if (text.Contains("RU") || text.Contains("RUSSIA") || text.Contains("РОССИЯ")) return "🇷🇺";
            if (text.Contains("NL") || text.Contains("NETHERLANDS") || text.Contains("НИДЕРЛАНДЫ") || text.Contains("AMSTERDAM")) return "🇳🇱";
            if (text.Contains("DE") || text.Contains("GERMANY") || text.Contains("ГЕРМАНИЯ") || text.Contains("FRANKFURT")) return "🇩🇪";
            if (text.Contains("US") || text.Contains("USA") || text.Contains("США") || text.Contains("AMERICA")) return "🇺🇸";
            if (text.Contains("FI") || text.Contains("FINLAND") || text.Contains("ФИНЛЯНДИЯ")) return "🇫🇮";
            if (text.Contains("SE") || text.Contains("SWEDEN") || text.Contains("ШВЕЦИЯ")) return "🇸🇪";
            if (text.Contains("TR") || text.Contains("TURKEY") || text.Contains("ТУРЦИЯ")) return "🇹🇷";
            if (text.Contains("KZ") || text.Contains("KAZAKHSTAN") || text.Contains("КАЗАХСТАН")) return "🇰🇿";
            if (text.Contains("FR") || text.Contains("FRANCE") || text.Contains("ФРАНЦИЯ")) return "🇫🇷";
            if (text.Contains("GB") || text.Contains("UK") || text.Contains("LONDON")) return "🇬🇧";
            if (text.Contains("JP") || text.Contains("JAPAN") || text.Contains("ЯПОНИЯ")) return "🇯🇵";
            if (text.Contains("SG") || text.Contains("SINGAPORE") || text.Contains("СИНГАПУР")) return "🇸🇬";
            if (text.Contains("PL") || text.Contains("POLAND") || text.Contains("ПОЛЬША")) return "🇵🇱";
            if (text.Contains("AE") || text.Contains("DUBAI") || text.Contains("ДУБАЙ")) return "🇦🇪";
            if (text.Contains("CH") || text.Contains("SWISS") || text.Contains("ШВЕЙЦАРИЯ")) return "🇨🇭";
            if (text.Contains("AT") || text.Contains("AUSTRIA") || text.Contains("АВСТРИЯ")) return "🇦🇹";
            return "🌐";
        }
    }

    public string ProtocolBadgeBackground => Protocol.ToLowerInvariant() switch
    {
        "vless" => "#22A78BFA",
        "vmess" => "#2200E5FF",
        "trojan" => "#2210E79D",
        "ss" or "shadowsocks" => "#22FFB300",
        "hysteria" or "hysteria2" or "hy2" => "#22FF3366",
        _ => "#2200F0FF"
    };

    public string ProtocolBadgeForeground => Protocol.ToLowerInvariant() switch
    {
        "vless" => "#A78BFA",
        "vmess" => "#00E5FF",
        "trojan" => "#10E79D",
        "ss" or "shadowsocks" => "#FFB300",
        "hysteria" or "hysteria2" or "hy2" => "#FF3366",
        _ => "#00F0FF"
    };

    public ServerNode Clone()
    {
        return (ServerNode)MemberwiseClone();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
