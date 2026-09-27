using System.Windows.Controls;
using SplateClientDesktop.ViewModels;

namespace SplateClientDesktop.Views.Pages;

public partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        
        // Simple DI resolve since we are migrating an existing monolithic viewmodel
        DataContext = App.GetService<MainViewModel>();
    }
}
