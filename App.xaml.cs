using System;
using System.IO;
using System.Windows;
using Application = System.Windows.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SplateClientDesktop.Services;
using SplateClientDesktop.ViewModels;

namespace SplateClientDesktop;

public partial class App : Application
{
    private static IHost? _host;
    private System.Threading.Mutex? _instanceMutex;

    public static T GetService<T>() where T : class => _host!.Services.GetRequiredService<T>();

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogAndReportCrash(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogToCrashFile(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };

        DispatcherUnhandledException += (s, e) =>
        {
            LogAndReportCrash(e.Exception, "DispatcherUnhandledException");
            e.Handled = true;
        };

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Services
                    services.AddSingleton<IServerStorage, ServerStorage>();
                    services.AddSingleton<IXrayProcessManager>(provider =>
                        new XrayProcessManager(AppDomain.CurrentDomain.BaseDirectory));
                    services.AddSingleton<IConfigExportImportService, ConfigExportImportService>();
                    services.AddSingleton<ITrafficMonitorService, TrafficMonitorService>();

                    // ViewModels
                    services.AddSingleton<MainViewModel>();

                    // Views
                    services.AddSingleton<MainWindow>();
                }).Build();
        }
        catch (Exception ex)
        {
            LogAndReportCrash(ex, "App.Constructor (DI Setup)");
            throw;
        }
    }

    private static void LogToCrashFile(Exception? ex, string source)
    {
        var message = ex?.ToString() ?? "Неизвестная ошибка";
        var date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var logEntry = $"[{date}] [{source}]\n{message}\n----------------------------------------\n";

        try
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SplateVPN");
            Directory.CreateDirectory(appDataDir);
            File.AppendAllText(Path.Combine(appDataDir, "crash.log"), logEntry);
        }
        catch { }

        try
        {
            var tempCrashFile = Path.Combine(Path.GetTempPath(), "SplateVPN_crash.log");
            File.AppendAllText(tempCrashFile, logEntry);
        }
        catch { }
    }

    private static void LogAndReportCrash(Exception? ex, string source)
    {
        try
        {
            SystemProxyManager.DisableProxy();
        }
        catch { }

        LogToCrashFile(ex, source);

        try
        {
            MessageBox.Show(
                $"Критическая ошибка SplateVPN ({source}):\n\n{ex?.Message}\n\nСтек ошибки записан в %APPDATA%\\SplateVPN\\crash.log\n\n{ex?.StackTrace}",
                "Ошибка приложения SplateVPN",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        catch { }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            if (e.Args.Length == 2 && e.Args[0] == "--wait-for-parent" && int.TryParse(e.Args[1], out var parentId))
            {
                try
                {
                    using var parent = System.Diagnostics.Process.GetProcessById(parentId);
                    using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await parent.WaitForExitAsync(timeout.Token);
                }
                catch (ArgumentException) { }
                catch (OperationCanceledException) { Shutdown(); return; }
            }
            _instanceMutex = new System.Threading.Mutex(true, @"Local\SplateVPN.Desktop", out var isFirst);
            if (!isFirst) { Shutdown(); return; }
            SystemProxyManager.RecoverStaleProxy();
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);

            if (_host != null)
            {
                await _host.StartAsync();
            }

            var mainWindow = GetService<MainWindow>();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            LogAndReportCrash(ex, "OnStartup");
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        SystemProxyManager.DisableProxy();

        if (_host != null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
