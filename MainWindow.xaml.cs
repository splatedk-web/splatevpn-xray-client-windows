using System;
using System.ComponentModel;
using System.Windows;
using SplateClientDesktop.Services;
using SplateClientDesktop.ViewModels;

namespace SplateClientDesktop;

public partial class MainWindow
{
    private SystemTrayManager? _trayManager;
    private MainViewModel? _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Navigate(typeof(Views.Pages.HomePage));

        if (_viewModel != null)
        {
            _viewModel.RequestNavigateToServers += () =>
            {
                Dispatcher.Invoke(() => RootNavigation.Navigate(typeof(Views.Pages.ServersPage)));
            };
        }
        
        _trayManager = new SystemTrayManager(this);
        _trayManager.Initialize();

        _trayManager.OnOpenRequested += () =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        _trayManager.OnToggleConnectionRequested += () =>
        {
            if (_viewModel != null)
                _viewModel.ToggleConnectionCommand.Execute(null);
        };

        _trayManager.OnExitRequested += () =>
        {
            _viewModel?.Disconnect();
            _trayManager?.Dispose();
            _trayManager = null;
            Application.Current.Shutdown();
        };

        if (_viewModel != null)
        {
            _viewModel.TrayTooltipChanged += tooltip =>
            {
                _trayManager?.UpdateTooltip(tooltip);
            };

            _viewModel.NotificationRequested += (title, message) =>
            {
                _trayManager?.ShowNotification(title, message);
            };
        }

        // Register Global Hotkey Win+Alt+V (0x0008 = Win, 0x0001 = Alt, 0x56 = V)
        RegisterGlobalHotkey();
    }

    private const int HOTKEY_ID = 9001;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_WIN = 0x0008;
    private const uint VK_V = 0x56;
    private const int WM_HOTKEY = 0x0312;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private void RegisterGlobalHotkey()
    {
        try
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var hwnd = helper.Handle;
            RegisterHotKey(hwnd, HOTKEY_ID, MOD_ALT | MOD_WIN, VK_V);

            var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
            source?.AddHook(HwndHook);
        }
        catch { }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            if (_viewModel != null)
            {
                _viewModel.ToggleConnectionCommand.Execute(null);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _viewModel?.MinimizeToTray == true)
        {
            Hide();
        }
    }

    private Views.CompactOverlayWindow? _overlayWindow;

    private void OpenOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        if (_overlayWindow == null)
        {
            _overlayWindow = new Views.CompactOverlayWindow(_viewModel, this);
        }
        _overlayWindow.Show();
        _overlayWindow.Activate();
        Hide();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel?.MinimizeToTray == true && _viewModel?.IsConnected == true)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                UnregisterHotKey(helper.Handle, HOTKEY_ID);
            }
            catch { }

            _viewModel?.Disconnect();
            _trayManager?.Dispose();
            _trayManager = null;
        }
    }
}