using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SplateClientDesktop.Models;

public class ServerNode : INotifyPropertyChanged
{
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
    private string _subscriptionGroup = "Основное";

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

    public bool IsCurrentlyActive
    {
        get => _isCurrentlyActive;
        set => SetField(ref _isCurrentlyActive, value);
    }

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
    public string DisplaySubtitle => $"{Protocol.ToUpperInvariant()} · {Network.ToUpperInvariant()} · {Security.ToUpperInvariant()}";
    public string ProtocolUpper => Protocol.ToUpperInvariant();
    public string PingDisplay => Ping > 0 ? $"{Ping} ms" : "--";
    public string PingColor => Ping <= 0 ? "#6B7280" : Ping < 150 ? "#10B981" : Ping < 300 ? "#F59E0B" : "#EF4444";

    public string ProtocolBadgeBackground => Protocol.ToLowerInvariant() switch
    {
        "vless" => Security.Equals("reality", StringComparison.OrdinalIgnoreCase) ? "#2010B981" : "#2006B6D4",
        "vmess" => "#203B82F6",
        "trojan" => "#208B5CF6",
        "ss" or "shadowsocks" => "#20F59E0B",
        _ => "#206B7280"
    };

    public string ProtocolBadgeForeground => Protocol.ToLowerInvariant() switch
    {
        "vless" => Security.Equals("reality", StringComparison.OrdinalIgnoreCase) ? "#34D399" : "#22D3EE",
        "vmess" => "#60A5FA",
        "trojan" => "#A78BFA",
        "ss" or "shadowsocks" => "#FBBF24",
        _ => "#9CA3AF"
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