using System.Windows;
using System.Windows.Controls;

namespace SplateClientDesktop.Views;

public sealed class BackupPasswordWindow : Window
{
    private readonly PasswordBox _password = new();
    public string Passphrase => _password.Password;

    public BackupPasswordWindow(bool exporting)
    {
        Title = exporting ? "Пароль резервной копии" : "Открыть резервную копию";
        Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = exporting
            ? "Задайте пароль (минимум 8 символов). Он понадобится для восстановления копии."
            : "Введите пароль, заданный при экспорте.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(_password);
        PasswordBox? confirmation = null;
        if (exporting)
        {
            panel.Children.Add(new TextBlock { Text = "Повторите пароль", Margin = new Thickness(0, 12, 0, 4) });
            confirmation = new PasswordBox(); panel.Children.Add(confirmation);
        }
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Padding = new Thickness(12, 6, 12, 6) };
        var accept = new Button { Content = "Продолжить", IsDefault = true, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        accept.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(Passphrase) || (exporting && Passphrase.Length < 8))
            { error.Text = exporting ? "Введите минимум 8 символов." : "Введите пароль."; return; }
            if (confirmation != null && Passphrase != confirmation.Password)
            { error.Text = "Пароли не совпадают."; return; }
            DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(accept); panel.Children.Add(buttons); Content = panel;
        Loaded += (_, _) => _password.Focus();
    }
}
