using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace SplateClientDesktop.Services;

public static class NativeThemeService
{
    private static JsonDocument? _palettes;
    public static void Apply(string theme, double glass)
    {
        if (Application.Current == null) return;
        _palettes ??= JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "palettes.json")));
        if (theme == "system")
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            theme = key?.GetValue("AppsUseLightTheme") is int n && n == 0 ? "dark" : "light";
        }
        if (!_palettes.RootElement.TryGetProperty(theme, out var palette)) palette = _palettes.RootElement.GetProperty("dark");
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(palette.GetProperty("isDark").GetBoolean() ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
        foreach (var (resource, token) in new[] { ("AppBackgroundBrush", "bg-dark"), ("AppSurfaceBrush", "surface-dark"), ("AppSurfaceElevatedBrush", "surface-elevated"), ("AppBorderSubtleBrush", "border-subtle"), ("AppAccentCyanBrush", "accent-cyan"), ("AppAccentEmeraldBrush", "accent-connected"), ("AppAccentVioletBrush", "accent-secondary"), ("AppAccentCrimsonBrush", "accent-crimson"), ("AppTextPrimaryBrush", "text-primary"), ("AppTextSecondaryBrush", "text-secondary") })
        {
            var color = (Color)ColorConverter.ConvertFromString(palette.GetProperty(token).GetString()!);
            if (resource == "AppSurfaceBrush") color.A = (byte)(255 * palette.GetProperty("glassAlpha").GetDouble() * (1 - Math.Clamp(glass, 0, 1) * .68));
            var brush = new SolidColorBrush(color); brush.Freeze(); Application.Current.Resources[resource] = brush;
        }
    }
}
