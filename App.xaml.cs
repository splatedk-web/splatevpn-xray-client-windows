using System;
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

    public static T GetService<T>() where T : class => _host!.Services.GetRequiredService<T>();

    public App()
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

        DispatcherUnhandledException += (s, e) =>
        {
            try
            {
                SystemProxyManager.DisableProxy();
                MessageBox.Show(
                    $"Произошла непредвиденная ошибка:\n{e.Exception.Message}\n\nТуннель деактивирован для безопасности сети.",
                    "Ошибка приложения SplateVPN",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            catch { }
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                SystemProxyManager.DisableProxy();
            }
            catch { }
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host!.StartAsync();

        var mainWindow = GetService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        SystemProxyManager.DisableProxy();

        if (_host != null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        base.OnExit(e);
    }
}