using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace SplateClientDesktop.Services;

/// <summary>Checks rendered text in an isolated Chromium session on the supplied
/// SOCKS route. No app IPC, account cookies, automatic login or CAPTCHA solving.</summary>
public static class GeminiPageVerifier
{
    public static Task<AiDnsTargetResult> CheckAsync(int socksPort, CancellationToken token) => CheckCoreAsync(socksPort, token, null);
    internal static Task<AiDnsTargetResult> CheckFixtureAsync(string html, CancellationToken token) => CheckCoreAsync(1, token, html);
    private static async Task<AiDnsTargetResult> CheckCoreAsync(int socksPort, CancellationToken token, string? fixture)
    {
        if (Application.Current == null) return new("Gemini", AiDnsOutcome.Failed, Detail: "Проверка JavaScript требует окно приложения");
        if (!Application.Current.Dispatcher.CheckAccess())
            return await await Application.Current.Dispatcher.InvokeAsync(() => CheckCoreAsync(socksPort, token, fixture));
        var clock = Stopwatch.StartNew();
        var profile = Path.Combine(Path.GetTempPath(), "SplateGeminiCheck", Guid.NewGuid().ToString("N"));
        using var browser = new WebView2();
        var window = new Window { Content = browser, Width = 1100, Height = 780, Left = -16000, Top = -16000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow, Opacity = 0 };
        CoreWebView2Environment? environment = null;
        try
        {
            token.ThrowIfCancellationRequested();
            window.Show();
            var options = new CoreWebView2EnvironmentOptions(fixture != null ? "" : $"--proxy-server=socks5://127.0.0.1:{socksPort} --proxy-bypass-list=<-loopback> --disable-quic --force-webrtc-ip-handling-policy=disable_non_proxied_udp --host-resolver-rules=\"MAP * ~NOTFOUND, EXCLUDE 127.0.0.1\"");
            environment = await CoreWebView2Environment.CreateAsync(WebViewRuntimeProvider.GetBrowserFolder(AppContext.BaseDirectory), profile, options);
            await browser.EnsureCoreWebView2Async(environment);
            token.ThrowIfCancellationRequested();
            browser.CoreWebView2.Settings.IsWebMessageEnabled = false;
            browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            browser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            browser.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            browser.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            browser.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
            string? navigationError = null; bool loaded = false; ulong navigationId = 0;
            browser.CoreWebView2.NavigationStarting += (_, e) =>
            {
                navigationId = e.NavigationId; loaded = false; navigationError = null;
                if (fixture != null) return;
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                    !(uri.Host == "gemini.google.com" || uri.Host == "accounts.google.com" || uri.Host == "consent.google.com" || uri.Host == "www.google.com")) e.Cancel = true;
            };
            browser.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (e.NavigationId != navigationId || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
                loaded = e.IsSuccess; navigationError = e.IsSuccess ? null : e.WebErrorStatus.ToString();
            };
            if (fixture == null) browser.CoreWebView2.Navigate("https://gemini.google.com/app?hl=en");
            else browser.CoreWebView2.NavigateToString(fixture);
            string previousText = ""; int stable = 0;
            // Wait for scripts and hydration, not just the first HTML response.
            for (var i = 0; i < 24; i++)
            {
                await Task.Delay(1000, token);
                if (navigationError != null) return new("Gemini", AiDnsOutcome.Failed, clock.ElapsedMilliseconds, "Браузер / TLS: " + navigationError);
                if (!loaded) continue;
                var value = await browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({text:(document.body?.innerText||'').slice(0,100000),url:location.href})");
                var json = JsonSerializer.Deserialize<string>(value);
                if (json == null) continue;
                using var document = JsonDocument.Parse(json);
                var text = document.RootElement.GetProperty("text").GetString() ?? "";
                var url = document.RootElement.GetProperty("url").GetString() ?? "";
                var outcome = AiDnsAutoSelector.ClassifyRendered(200, text, url);
                if (outcome == AiDnsOutcome.GeoBlocked) return new("Gemini", outcome, clock.ElapsedMilliseconds, "JavaScript: региональный запрет на странице Gemini");
                stable = text == previousText && text.Length > 20 ? stable + 1 : 0; previousText = text;
                if (i >= 11 && stable >= 4)
                {
                    if (url.StartsWith("https://accounts.google.com/", StringComparison.Ordinal) || url.StartsWith("https://consent.google.com/", StringComparison.Ordinal)) outcome = AiDnsOutcome.Limited;
                    return new("Gemini", outcome, clock.ElapsedMilliseconds, outcome == AiDnsOutcome.Accessible ? "JavaScript: публичная страница загружена; работа аккаунта не проверялась" : "JavaScript: вход, CAPTCHA или ошибка страницы; доступ не подтверждён");
                }
            }
            return new("Gemini", AiDnsOutcome.Failed, clock.ElapsedMilliseconds, "JavaScript: страница не завершила загрузку");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) { return new("Gemini", AiDnsOutcome.Failed, clock.ElapsedMilliseconds, "Проверка JavaScript: " + e.Message); }
        finally
        {
            browser.Dispose(); window.Close();
            // The isolated browser can keep its profile locked briefly after disposal.
            _ = Task.Run(async () => { for (var i = 0; i < 10; i++) { try { if (Directory.Exists(profile)) Directory.Delete(profile, true); return; } catch (IOException) { } catch (UnauthorizedAccessException) { } await Task.Delay(1000); } });
        }
    }
}
