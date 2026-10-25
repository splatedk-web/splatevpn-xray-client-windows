using System.Text.Json;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;
using SplateClientDesktop;
using SplateClientDesktop.ViewModels;
using System.Reflection;

internal static class RoutingUiChecks
{
    public static Task RunAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            app.Dispatcher.BeginInvoke(async () =>
            {
                try { await CheckAsync(); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
                finally { app.Shutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task CheckAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var profile = Path.Combine(Path.GetTempPath(), "splate-routing-ui-" + Guid.NewGuid().ToString("N"));
        var dataDirectory = profile + "-data";
        var storage = new ServerStorage(dataDirectory);
        storage.SaveSettings(new AppSettings { SettingsSchemaVersion = 2, AiRoutingMode = "dns_ai", EnableAiDns = true,
            EnableAiBypass = true, EnableGlobalHotkey = false, PresetSubscriptionsInitialized = true, AutoUpdateSubscription = false });
        var vm = new MainViewModel(storage, new XrayProcessManager(root), new ConfigExportImportService(storage), new TrafficMonitorService(), false);
        var host = new MainWindow(vm);
        host.Closing -= (System.ComponentModel.CancelEventHandler)Delegate.CreateDelegate(typeof(System.ComponentModel.CancelEventHandler), host,
            typeof(MainWindow).GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!);
        var applySettings = typeof(MainWindow).GetMethod("ApplyExtendedSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var browser = new WebView2();
        var window = new Window { Content = browser, Width = 1100, Height = 800, Left = -16000, Top = -16000,
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        try
        {
            window.Show();
            var environment = await CoreWebView2Environment.CreateAsync(WebViewRuntimeProvider.GetBrowserFolder(root), profile);
            await browser.EnsureCoreWebView2Async(environment);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var messages = Channel.CreateUnbounded<JsonElement>();
            browser.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var action = doc.RootElement.GetProperty("action").GetString();
                if (action == "ready") ready.TrySetResult();
                if (action == "saveSettings") messages.Writer.TryWrite(doc.RootElement.Clone());
            };
            browser.CoreWebView2.Navigate(new Uri(Path.Combine(root, "wwwroot", "index.html")).AbsoluteUri);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var settings = vm.Settings;
            string? acknowledgedRequestId = null;
            var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            void Hydrate()
            {
                browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "extendedSettings", payload = settings, settingsRequestId = acknowledgedRequestId }));
                browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "settings", payload = settings, settingsRequestId = acknowledgedRequestId }, camel));
            }
            async Task<string> Script(string script) => await browser.CoreWebView2.ExecuteScriptAsync(script);
            async Task AssertMode(string mode)
            {
                // PostWebMessage and ExecuteScript use separate asynchronous queues.
                string value = "";
                for (var i=0;i<100;i++)
                {
                    value = await Script("document.getElementById('setAiMode').value");
                    if (JsonSerializer.Deserialize<string>(value)==mode) return;
                    await Task.Delay(20);
                }
                throw new Exception("route reset after hydration: " + value);
            }
            async Task Save(string script, string expectedMode, bool sniffing, string? expectedRouting = null)
            {
                await Script(script);
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var message = await messages.Reader.ReadAsync(limit.Token);
                var data = message.GetProperty("settings");
                // A delayed response to the previous edit must not roll back this edit.
                Hydrate(); await Task.Delay(100);
                Check(JsonSerializer.Deserialize<string>(await Script("document.getElementById('setAiMode').value")) == expectedMode,
                    "stale settings response rolled back the newest route selection");
                Check(await Script("document.getElementById('setSniffing').checked") == sniffing.ToString().ToLowerInvariant(),
                    "stale settings response rolled back the newest sniffing selection");
                Check(data.GetProperty("aiRoutingMode").GetString() == expectedMode, "wrong route sent by UI");
                Check(data.GetProperty("enableAiDns").GetBoolean() == (expectedMode is "dns_ai" or "dns_vpn"), "inconsistent SmartDNS flag");
                Check(data.GetProperty("enableSniffing").GetBoolean() == sniffing, "sniffing was forced on by UI");
                // Invoke the actual application settings handler, not an imitation of route persistence.
                applySettings.Invoke(host, new object[] { data });
                vm.EnableSniffing = data.GetProperty("enableSniffing").GetBoolean();
                vm.EnableAiBypass = settings.AiRoutingMode != "off";
                Check(settings.AiRoutingMode == expectedMode, "backend discarded AI route");
                Check(settings.RoutingMode == (expectedRouting ?? "smart_ru"), "backend discarded primary route");
                SettingsCompatibility.Normalize(settings);
                storage.SaveSettings(settings);
                acknowledgedRequestId = message.GetProperty("settingsRequestId").GetString();
                Check(settings.EnableSniffing == sniffing, "normalization forced sniffing on");
                Hydrate(); await AssertMode(expectedMode);
                Check(await Script("document.getElementById('setSniffing').checked") == sniffing.ToString().ToLowerInvariant(), "checkbox reset after save");
                Check(JsonSerializer.Deserialize<string>(await Script("document.getElementById('setRoutingMode').value")) == settings.RoutingMode,
                    "primary dropdown reset after backend roundtrip");
                Check(storage.LoadSettings().RoutingMode == settings.RoutingMode, "primary route not persisted to disk");
                Check(await Script("document.querySelector('#setAiMode option[value=dns_ai]').disabled") == (settings.RoutingMode=="global").ToString().ToLowerInvariant(),
                    "global mode must exclude direct SmartDNS while allowing tunneled SmartDNS");
                await Task.Delay(100);
                Check(!messages.Reader.TryRead(out _), "one change triggered duplicate saves");
            }
            Hydrate(); await AssertMode("dns_ai");
            await Save("document.getElementById('setRoutingMode').value='global';document.getElementById('setRoutingMode').dispatchEvent(new Event('change'));", "vpn", true, "global");
            await Save("document.getElementById('setAiMode').value='dns_vpn';document.getElementById('setAiMode').dispatchEvent(new Event('change'));", "dns_vpn", true, "global");
            await Save("document.getElementById('setSniffing').click();", "dns_vpn", false, "global");
            await Save("document.getElementById('setAiMode').value='vpn';document.getElementById('setAiMode').dispatchEvent(new Event('change'));", "vpn", false, "global");
            await Save("document.getElementById('setSniffing').click();", "vpn", true, "global");
            await Save("document.getElementById('toggleSmartRu').click();", "vpn", true, "smart_ru");
            await Save("document.getElementById('setAiMode').value='vpn';document.getElementById('setAiMode').dispatchEvent(new Event('change'));", "vpn", true);
            await Save("document.getElementById('setSniffing').click();", "vpn", false);
            await Save("document.getElementById('setAiMode').value='off';document.getElementById('setAiMode').dispatchEvent(new Event('change'));", "off", false);
            await Save("document.getElementById('setAiMode').value='dns_ai';document.getElementById('setAiMode').dispatchEvent(new Event('change'));", "dns_ai", false);
            // Old boolean flags cannot override an explicit route in a server snapshot.
            settings.AiRoutingMode = "vpn"; settings.EnableAiXboxDns = true; settings.EnableAiDns = true;
            Hydrate(); await AssertMode("vpn");
            settings.AiRoutingMode = "off"; Hydrate(); await AssertMode("off");
            foreach (var mode in new[] { "off", "vpn", "dns_ai" })
            {
                var config = JsonDocument.Parse(new XrayConfigGenerator().GenerateConfig(
                    new ServerNode { Address = "127.0.0.1", Security = "none" }, enableSniffing: false,
                    enableAiBypass: mode != "off", enableAiDns: mode == "dns_ai", aiRoutingMode: mode, enableAdBlock: true));
                using (config)
                    Check(config.RootElement.GetProperty("inbounds").EnumerateArray().All(i => !i.TryGetProperty("sniffing", out _)), "core config ignored sniffing off");
            }
            Console.WriteLine("PASS real routing WebView2 + application handler: global locks AI to VPN, both controls persist, stale acknowledgements cannot undo edits, AI VPN/off/SmartDNS, sniffing and single save");
        }
        finally
        {
            browser.Dispose(); window.Close();
            host.Close();
            Directory.Delete(dataDirectory, true);
            // WebView2 can release profile handles after the control has been disposed.
            for (var i = 0; i < 20; i++)
            {
                try { if (Directory.Exists(profile)) Directory.Delete(profile, true); break; }
                catch (IOException) { await Task.Delay(100); }
                catch (UnauthorizedAccessException) { await Task.Delay(100); }
            }
        }
    }
}
