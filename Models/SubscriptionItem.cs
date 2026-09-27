using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SplateClientDesktop.Models;

public class SubscriptionItem : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = "Подписка";
    private string _url = "";
    private bool _autoUpdate = true;
    private long _dataLimit = 0;
    private long _dataUsed = 0;
    private long _expiryTime = 0;
    private DateTime? _lastUpdated;
    private int _nodeCount = 0;
    private bool _isUpdating = false;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Url
    {
        get => _url;
        set => SetField(ref _url, value);
    }

    public bool AutoUpdate
    {
        get => _autoUpdate;
        set => SetField(ref _autoUpdate, value);
    }

    public long DataLimit
    {
        get => _dataLimit;
        set
        {
            if (SetField(ref _dataLimit, value))
            {
                OnPropertyChanged(nameof(UsagePercentage));
                OnPropertyChanged(nameof(FormattedUsage));
            }
        }
    }

    public long DataUsed
    {
        get => _dataUsed;
        set
        {
            if (SetField(ref _dataUsed, value))
            {
                OnPropertyChanged(nameof(UsagePercentage));
                OnPropertyChanged(nameof(FormattedUsage));
            }
        }
    }

    public long ExpiryTime
    {
        get => _expiryTime;
        set
        {
            if (SetField(ref _expiryTime, value))
            {
                OnPropertyChanged(nameof(FormattedExpiry));
                OnPropertyChanged(nameof(ExpireTime));
            }
        }
    }

    public long ExpireTime
    {
        get => _expiryTime;
        set => ExpiryTime = value;
    }

    public DateTime? LastUpdated
    {
        get => _lastUpdated;
        set
        {
            if (SetField(ref _lastUpdated, value))
            {
                OnPropertyChanged(nameof(LastUpdatedDisplay));
            }
        }
    }

    public int NodeCount
    {
        get => _nodeCount;
        set => SetField(ref _nodeCount, value);
    }

    public bool IsUpdating
    {
        get => _isUpdating;
        set => SetField(ref _isUpdating, value);
    }

    public double UsagePercentage
    {
        get
        {
            if (DataLimit <= 0) return 0;
            return Math.Clamp((double)DataUsed / DataLimit * 100.0, 0, 100.0);
        }
    }

    public string FormattedUsage
    {
        get
        {
            if (DataLimit <= 0) return "Без ограничений";
            return $"{FormatBytes(DataUsed)} / {FormatBytes(DataLimit)}";
        }
    }

    public string FormattedExpiry
    {
        get
        {
            if (ExpiryTime <= 0) return "Бессрочно";
            try
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(ExpiryTime).ToLocalTime();
                return $"До {dt:dd.MM.yyyy}";
            }
            catch
            {
                return "Бессрочно";
            }
        }
    }

    public string LastUpdatedDisplay
    {
        get
        {
            if (!LastUpdated.HasValue) return "Не обновлялось";
            return $"Обновлено {LastUpdated.Value:dd.MM HH:mm}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
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
