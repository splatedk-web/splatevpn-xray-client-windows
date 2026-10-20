using System;
using System.Threading.Tasks;

namespace SplateClientDesktop.Services;

public interface IXrayProcessManager
{
    event Action<string>? OnLogMessage;
    event Action<int>? OnProcessExited;
    bool IsRunning { get; }
    Task<bool> StartAsync(string configJson, int httpPort = 10809, int socksPort = 10808, System.Threading.CancellationToken ct = default);
    void Stop();
}
