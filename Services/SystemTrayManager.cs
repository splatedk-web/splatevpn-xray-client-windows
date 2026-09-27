using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SplateClientDesktop.Services;

public class SystemTrayManager : ISystemTrayManager
{
    private const int WM_USER = 0x0400;
    public const int WM_TRAYICON = WM_USER + 101;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    private const int NIIF_NONE = 0x00000000;
    private const int NIIF_INFO = 0x00000001;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly Window _window;
    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private bool _isCreated = false;
    private IntPtr _hIcon = IntPtr.Zero;

    public event Action? OnOpenRequested;
    public event Action? OnToggleConnectionRequested;
    public event Action? OnExitRequested;

    public SystemTrayManager(Window window)
    {
        _window = window;
    }

    public void Initialize()
    {
        var helper = new WindowInteropHelper(_window);
        _hwnd = helper.EnsureHandle();

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        _hIcon = LoadApplicationIcon();

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "SplateVPN Desktop"
        };

        _isCreated = Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    public void UpdateTooltip(string tooltip)
    {
        if (!_isCreated) return;
        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_TIP,
            szTip = tooltip.Length > 127 ? tooltip.Substring(0, 127) : tooltip
        };
        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    public void ShowNotification(string title, string message, int timeoutMs = 3000)
    {
        if (!_isCreated) return;
        try
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_INFO,
                szInfoTitle = title.Length > 63 ? title.Substring(0, 63) : title,
                szInfo = message.Length > 255 ? message.Substring(0, 255) : message,
                uTimeoutOrVersion = timeoutMs,
                dwInfoFlags = NIIF_INFO
            };
            Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }
        catch { }
    }

    private static IntPtr LoadApplicationIcon()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var icoPath = Path.Combine(baseDir, "Assets", "app.ico");
            if (File.Exists(icoPath))
            {
                var hIcon = LoadImage(IntPtr.Zero, icoPath, 1, 16, 16, 0x00000010);
                if (hIcon != IntPtr.Zero) return hIcon;
            }

            // Fallback to Windows default shield/app icon
            var iconHandle = LoadIcon(IntPtr.Zero, (IntPtr)32518);
            if (iconHandle == IntPtr.Zero)
            {
                iconHandle = LoadIcon(IntPtr.Zero, (IntPtr)32512);
            }
            return iconHandle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            var eventType = lParam.ToInt32();
            if (eventType == WM_LBUTTONUP || eventType == WM_LBUTTONDBLCLK)
            {
                OnOpenRequested?.Invoke();
                handled = true;
            }
            else if (eventType == WM_RBUTTONUP)
            {
                ShowContextMenu();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        SetForegroundWindow(_hwnd);

        var menu = new ContextMenu();

        var itemTitle = new MenuItem
        {
            Header = "SplateVPN Tactical Desktop",
            IsEnabled = false,
            FontWeight = FontWeights.Bold
        };
        menu.Items.Add(itemTitle);
        menu.Items.Add(new Separator());

        var itemToggle = new MenuItem
        {
            Header = "Подключить / Отключить"
        };
        itemToggle.Click += (_, _) => OnToggleConnectionRequested?.Invoke();
        menu.Items.Add(itemToggle);

        var itemShow = new MenuItem
        {
            Header = "Открыть приложение"
        };
        itemShow.Click += (_, _) => OnOpenRequested?.Invoke();
        menu.Items.Add(itemShow);

        menu.Items.Add(new Separator());

        var itemExit = new MenuItem
        {
            Header = "Выход"
        };
        itemExit.Click += (_, _) => OnExitRequested?.Invoke();
        menu.Items.Add(itemExit);

        menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (_isCreated)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _isCreated = false;
        }

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }
}
