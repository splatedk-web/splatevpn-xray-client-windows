using System;
using System.Threading.Tasks;

namespace SplateClientDesktop.Services;

public interface ITrafficMonitorService
{
    // Event raised when speeds are updated (downloadSpeed, uploadSpeed)
    event Action<string, string>? SpeedUpdated;
    // Event raised with raw bytes per second for real-time graphs (downloadBytesPerSec, uploadBytesPerSec)
    event Action<long, long>? BytesPerSecUpdated;
    // Event raised when session bytes are updated (totalDownloaded, totalUploaded)
    event Action<long, long>? SessionBytesUpdated;
    // Starts monitoring (should be called when connection is established)
    void Start(int metricsPort);
    void Resume(int metricsPort) => Start(metricsPort);
    // Stops monitoring (when disconnected)
    void Stop();
}
