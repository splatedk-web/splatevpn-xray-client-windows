using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SplateClientDesktop.ViewModels;

namespace SplateClientDesktop.Views.Pages;

public partial class LogsPage : Page
{
    private MainViewModel? _viewModel;

    public LogsPage()
    {
        InitializeComponent();
        DataContext = App.GetService<MainViewModel>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            _viewModel = vm;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateFilteredLogs();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Logs))
        {
            Dispatcher.Invoke(UpdateFilteredLogs);
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        UpdateFilteredLogs();
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateFilteredLogs();
    }

    private void UpdateFilteredLogs()
    {
        if (_viewModel == null || TxtLogOutput == null) return;

        var raw = _viewModel.Logs;
        if (string.IsNullOrEmpty(raw))
        {
            TxtLogOutput.Text = "Журнал пуст.";
            return;
        }

        var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var searchQuery = TxtSearch?.Text?.Trim() ?? "";

        var filtered = lines.Where(line =>
        {
            // Category filter
            if (RbFilterXray?.IsChecked == true && !line.Contains("[XRAY", StringComparison.OrdinalIgnoreCase) && !line.Contains("app/proxyman", StringComparison.OrdinalIgnoreCase))
                return false;

            if (RbFilterVpn?.IsChecked == true && !line.Contains("[CLIENT", StringComparison.OrdinalIgnoreCase) && !line.Contains("[TUN", StringComparison.OrdinalIgnoreCase) && !line.Contains("[SERVER", StringComparison.OrdinalIgnoreCase))
                return false;

            if (RbFilterErrors?.IsChecked == true && !line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) && !line.Contains("WARN", StringComparison.OrdinalIgnoreCase) && !line.Contains("failed", StringComparison.OrdinalIgnoreCase))
                return false;

            // Search filter
            if (!string.IsNullOrEmpty(searchQuery) && !line.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        });

        TxtLogOutput.Text = string.Join(Environment.NewLine, filtered);

        if (ChkAutoScroll?.IsChecked == true)
        {
            TxtLogOutput.ScrollToEnd();
        }
    }

    private void CopyLogs_Click(object sender, RoutedEventArgs e)
    {
        var text = TxtLogOutput.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            Clipboard.SetText(text);
            MessageBox.Show("Логи скопированы в буфер обмена.", "Копирование", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Title = "Сохранить диагностический журнал",
            Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
            FileName = $"SplateVPN_Logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };
        if (sfd.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(sfd.FileName, TxtLogOutput.Text, Encoding.UTF8);
                MessageBox.Show($"Лог успешно сохранен в файл:\n{sfd.FileName}", "Экспорт завершен", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении файла:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
