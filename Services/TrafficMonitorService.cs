using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace SplateClientDesktop.Services;

public class TrafficMonitorService : ITrafficMonitorService, IDisposable
{
    private System.Threading.Timer? _timer;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(1);
    private long _prevBytesReceived;
    private long _prevBytesSent;
    private long _totalDownloaded;
    private long _totalUploaded;
    private readonly object _lock = new();

    public event Action<string, string>? SpeedUpdated;
    public event Action<long, long>? BytesPerSecUpdated;
    public event Action<long, long>? SessionBytesUpdated;

    public void Start()
    {
        // Reset counters
        lock (_lock)
        {
            _prevBytesReceived = GetTotalBytesReceived();
            _prevBytesSent = GetTotalBytesSent();
            _totalDownloaded = 0;
            _totalUploaded = 0;
        }
        _timer = new Timer(Callback, null, TimeSpan.Zero, _interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Callback(object? state)
    {
        try
        {
            var currentReceived = GetTotalBytesReceived();
            var currentSent = GetTotalBytesSent();
            long deltaReceived, deltaSent;
            lock (_lock)
            {
                deltaReceived = _prevBytesReceived > 0 ? Math.Max(0, currentReceived - _prevBytesReceived) : 0;
                deltaSent = _prevBytesSent > 0 ? Math.Max(0, currentSent - _prevBytesSent) : 0;
                _prevBytesReceived = currentReceived;
                _prevBytesSent = currentSent;
                _totalDownloaded += deltaReceived;
                _totalUploaded += deltaSent;
            }
            var downloadSpeed = FormatSpeed(deltaReceived);
            var uploadSpeed = FormatSpeed(deltaSent);
            SpeedUpdated?.Invoke(downloadSpeed, uploadSpeed);
            BytesPerSecUpdated?.Invoke(deltaReceived, deltaSent);
            SessionBytesUpdated?.Invoke(_totalDownloaded, _totalUploaded);
        }
        catch
        {
            // Swallow any exception to keep timer alive
        }
    }

    private static long GetTotalBytesReceived()
    {
        long total = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                try
                {
                    total += ni.GetIPv4Statistics().BytesReceived;
                }
                catch { }
            }
        }
        catch { }
        return total;
    }

    private static long GetTotalBytesSent()
    {
        long total = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                try
                {
                    total += ni.GetIPv4Statistics().BytesSent;
                }
                catch { }
            }
        }
        catch { }
        return total;
    }

    private static string FormatSpeed(long bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:F1} KB/s";
        return $"{bytesPerSec / (1024.0 * 1024):F1} MB/s";
    }

    public void Dispose()
    {
        Stop();
    }
}
