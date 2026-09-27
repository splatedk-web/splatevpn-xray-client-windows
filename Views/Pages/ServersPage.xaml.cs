using System.Windows.Controls;
using SplateClientDesktop.ViewModels;

namespace SplateClientDesktop.Views.Pages;

public partial class ServersPage : Page
{
    public ServersPage()
    {
        InitializeComponent();
        
        // Simple DI resolve
        DataContext = App.GetService<MainViewModel>();
    }
}
