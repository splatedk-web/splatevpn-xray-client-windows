using System.Windows.Controls;
using SplateClientDesktop.ViewModels;

namespace SplateClientDesktop.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        
        // Simple DI resolve
        DataContext = App.GetService<MainViewModel>();
    }
}
