using System.Windows;
using System.Windows.Threading;
using SplateClientDesktop.Services;

internal static class GeminiRenderChecks
{
    public static Task RunAsync(string? provider = null)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            app.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
                    if (provider != null) {
                        var live = await new AiDnsAutoSelector().ProbeAsync(new(provider, false), timeout.Token, GeminiPageVerifier.CheckAsync);
                        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(live)); completion.SetResult(); return;
                    }
                    var fixtures = new[] {
                        ("Gemini isn’t currently supported in your country. Stay tuned!", AiDnsOutcome.GeoBlocked),
                        ("Something went wrong. Try again later.", AiDnsOutcome.Limited),
                        ("Gemini home. Chat to start writing, planning, learning and more with Google AI. Supercharge your creativity and productivity. Sign in", AiDnsOutcome.Accessible)
                    };
                    foreach (var (message, expected) in fixtures)
                    {
                        var html = "<html><body><main id='result'>Loading Gemini…</main><script>const translations='Gemini isn’t currently supported in your country';setTimeout(()=>document.getElementById('result').textContent=" + System.Text.Json.JsonSerializer.Serialize(message) + ",1500);</script></body></html>";
                        var result = await GeminiPageVerifier.CheckFixtureAsync(html, timeout.Token);
                        if (result.Outcome != expected) throw new Exception($"Expected {expected}, got {result}");
                        Console.WriteLine($"PASS real WebView2 delayed DOM: {expected} ({result.ElapsedMs} ms)");
                    }
                    completion.SetResult();
                }
                catch (Exception e) { completion.SetException(e); }
                finally { app.Shutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
}
