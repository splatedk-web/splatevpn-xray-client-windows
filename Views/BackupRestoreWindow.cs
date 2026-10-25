using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SplateClientDesktop.Views;

public sealed class BackupRestoreWindow : Window
{
    public bool Merge { get; private set; } = true;
    public BackupRestoreWindow()
    {
        Title = "Восстановление резервной копии"; Width = 460;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)); Foreground = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Как восстановить данные?", FontSize = 20, Margin = new Thickness(0, 0, 0, 16) });
        var merge = new RadioButton { Content = "Объединить с текущими данными", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(merge);
        panel.Children.Add(new TextBlock { Text = "Добавятся серверы и подписки с новыми идентификаторами. Текущие настройки и существующие записи сохранятся.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 0, 0, 16) });
        panel.Children.Add(new RadioButton { Content = "Заменить все данные", Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "Серверы, подписки и настройки будут заменены содержимым копии, включая пустые списки.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 0, 0, 20) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Padding = new Thickness(12, 8, 12, 8) };
        var accept = new Button { Content = "Восстановить", IsDefault = true, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(8, 0, 0, 0) };
        accept.Click += (_, _) => { Merge = merge.IsChecked == true; DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(accept); panel.Children.Add(buttons); Content = panel;
    }
}
