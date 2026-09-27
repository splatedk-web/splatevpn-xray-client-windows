using System.Windows;
using System.Windows.Input;
using SplateClientDesktop.ViewModels;

namespace SplateClientDesktop.Views;

public partial class CompactOverlayWindow : Window
{
    private readonly Window _mainWindow;

    public CompactOverlayWindow(MainViewModel viewModel, Window mainWindow)
    {
        InitializeComponent();
        DataContext = viewModel;
        _mainWindow = mainWindow;

        // Position overlay near bottom-right above taskbar
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Bottom - Height - 24;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void RestoreMainWindow_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        Hide();
    }

    private void CloseOverlay_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
