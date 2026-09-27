using System;
using System.Windows;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Views;

public partial class AddSubscriptionWindow
{
    public SubscriptionItem? ResultSubscription { get; private set; }

    public AddSubscriptionWindow(string? initialUrl = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(initialUrl))
        {
            TxtSubUrl.Text = initialUrl.Trim();
            try
            {
                if (Uri.TryCreate(initialUrl, UriKind.Absolute, out var u))
                    TxtSubName.Text = u.Host;
            }
            catch { }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var url = TxtSubUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Введите ссылку на подписку.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtSubUrl.Focus();
            return;
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Ссылка на подписку должна начинаться с https:// или http://", "Неверный формат", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtSubUrl.Focus();
            return;
        }

        var name = TxtSubName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var u))
                    name = u.Host;
                else
                    name = "Подписка";
            }
            catch
            {
                name = "Подписка";
            }
        }

        ResultSubscription = new SubscriptionItem
        {
            Name = name,
            Url = url,
            AutoUpdate = TglAutoUpdate.IsChecked ?? true
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
