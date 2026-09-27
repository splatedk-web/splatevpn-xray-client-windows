using System;

namespace SplateClientDesktop.Services;

public interface ISystemTrayManager : IDisposable
{
    event Action? OnOpenRequested;
    event Action? OnToggleConnectionRequested;
    event Action? OnExitRequested;
    
    void Initialize();
    void UpdateTooltip(string text);
    void ShowNotification(string title, string message, int timeoutMs = 3000);
}
