using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SplateClientDesktop.ViewModels;

namespace SplateClientDesktop.Views.Pages;

public partial class PerAppPage : Page
{
    private MainViewModel? _viewModel;
    private readonly ObservableCollection<string> _currentApps = new();
    private bool _isProxyMode = true;

    public PerAppPage()
    {
        InitializeComponent();
        DataContext = App.GetService<MainViewModel>();
        LstApps.ItemsSource = _currentApps;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            _viewModel = vm;
            LoadFromViewModel();
            LoadRunningProcessSuggestions();
        }
    }

    private void LoadFromViewModel()
    {
        if (_viewModel == null) return;

        _isProxyMode = _viewModel.Settings.SplitTunnelingMode != "bypass_selected";
        UpdateModeUi();

        var source = _isProxyMode ? _viewModel.PerAppProxyList : _viewModel.PerAppDirectList;
        _currentApps.Clear();
        if (!string.IsNullOrWhiteSpace(source))
        {
            var apps = source.Split(new[] { ',', '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var app in apps)
            {
                var clean = app.Trim();
                if (!string.IsNullOrEmpty(clean) && !_currentApps.Contains(clean, StringComparer.OrdinalIgnoreCase))
                {
                    _currentApps.Add(clean);
                }
            }
        }
        UpdateCount();
    }

    private void SaveToViewModel()
    {
        if (_viewModel == null) return;

        var joined = string.Join(", ", _currentApps);
        if (_isProxyMode)
        {
            _viewModel.PerAppProxyList = joined;
            _viewModel.Settings.PerAppProxyList = joined;
            _viewModel.Settings.SplitTunnelingMode = "proxy_selected";
        }
        else
        {
            _viewModel.PerAppDirectList = joined;
            _viewModel.Settings.PerAppDirectList = joined;
            _viewModel.Settings.SplitTunnelingMode = "bypass_selected";
        }

        _viewModel.SaveSettingsCommand.Execute(null);
        UpdateCount();
    }

    private void UpdateModeUi()
    {
        if (_isProxyMode)
        {
            BorderProxyMode.Background = new SolidColorBrush(Color.FromArgb(0x22, 0x10, 0xB9, 0x81));
            BorderProxyMode.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x10, 0xB9, 0x81));

            BorderBypassMode.Background = new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
            BorderBypassMode.BorderBrush = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
        }
        else
        {
            BorderBypassMode.Background = new SolidColorBrush(Color.FromArgb(0x22, 0x8B, 0x5C, 0xF6));
            BorderBypassMode.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x8B, 0x5C, 0xF6));

            BorderProxyMode.Background = new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
            BorderProxyMode.BorderBrush = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
        }
    }

    private void UpdateCount()
    {
        if (TxtAppCount != null)
        {
            TxtAppCount.Text = _currentApps.Count.ToString();
        }
    }

    private void ProxyMode_Click(object sender, RoutedEventArgs e)
    {
        if (!_isProxyMode)
        {
            _isProxyMode = true;
            UpdateModeUi();
            LoadFromViewModel();
        }
    }

    private void BypassMode_Click(object sender, RoutedEventArgs e)
    {
        if (_isProxyMode)
        {
            _isProxyMode = false;
            UpdateModeUi();
            LoadFromViewModel();
        }
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        AddAppFromInput();
    }

    private void TxtAppInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddAppFromInput();
        }
    }

    private void AddAppFromInput()
    {
        var raw = TxtAppInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;

        var processName = Path.GetFileName(raw);
        if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            processName += ".exe";
        }

        if (!_currentApps.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            _currentApps.Add(processName);
            SaveToViewModel();
        }
        TxtAppInput.Text = string.Empty;
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Выберите исполняемый файл приложения",
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*"
        };
        if (ofd.ShowDialog() == true)
        {
            var fileName = Path.GetFileName(ofd.FileName);
            if (!_currentApps.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                _currentApps.Add(fileName);
                SaveToViewModel();
            }
        }
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string app)
        {
            _currentApps.Remove(app);
            SaveToViewModel();
        }
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (_currentApps.Count == 0) return;
        var res = MessageBox.Show("Очистить список приложений?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            _currentApps.Clear();
            SaveToViewModel();
        }
    }

    private void LoadRunningProcessSuggestions()
    {
        PnlRunningApps.Children.Clear();
        try
        {
            var targetNames = new[] { "telegram", "discord", "chrome", "firefox", "msedge", "spotify", "steam", "epicgameslauncher", "viber", "obs64", "utorrent", "qbittorrent" };
            var running = Process.GetProcesses()
                .Select(p =>
                {
                    try { return p.ProcessName.ToLowerInvariant(); }
                    catch { return ""; }
                })
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .Intersect(targetNames)
                .Take(8);

            foreach (var proc in running)
            {
                var exeName = proc + ".exe";
                var btn = new Wpf.Ui.Controls.Button
                {
                    Content = $"+ {exeName}",
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 0, 6, 6),
                    FontSize = 11,
                    Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary
                };
                btn.Click += (_, _) =>
                {
                    if (!_currentApps.Contains(exeName, StringComparer.OrdinalIgnoreCase))
                    {
                        _currentApps.Add(exeName);
                        SaveToViewModel();
                    }
                };
                PnlRunningApps.Children.Add(btn);
            }
        }
        catch { }
    }
}
