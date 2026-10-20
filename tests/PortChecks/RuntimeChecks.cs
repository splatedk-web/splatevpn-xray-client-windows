using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using SplateClientDesktop;
using SplateClientDesktop.Services;
using SplateClientDesktop.ViewModels;

internal static class RuntimeChecks
{
    public static async Task RunAsync(MainViewModel vm)
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        Check(File.Exists(Path.Combine(WebViewRuntimeProvider.GetBrowserFolder(root),"msedgewebview2.exe")),"bundled runtime selected independently of registry");
        try {WebViewRuntimeProvider.GetBrowserFolder(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()));throw new Exception("missing runtime accepted");}
        catch(FileNotFoundException){}
        await Task.CompletedTask;
        var window=new MainWindow(vm);
        // This test window must not run application shutdown/network cleanup on the user's machine.
        window.Closing-=(System.ComponentModel.CancelEventHandler)Delegate.CreateDelegate(typeof(System.ComponentModel.CancelEventHandler),window,
            typeof(MainWindow).GetMethod("OnClosing",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly)!);
        var recovery=typeof(MainWindow).GetMethod("ShowRuntimeRecovery",BindingFlags.NonPublic|BindingFlags.Instance)!;
        recovery.Invoke(window,[new WebView2RuntimeNotFoundException()]);
        Check(((Border)window.FindName("RuntimeRecoveryPanel")).Visibility==Visibility.Visible,"native recovery visible without browser");
        Check(window.FindName("InstallRuntimeButton")==null,"no dependency on broken system installer");
        Check(((TextBlock)window.FindName("RuntimeRecoveryStatus")).Text.Contains("Распакуйте архив целиком"),"incomplete package recovery explained");
        recovery.Invoke(window,[new IOException("Injected profile error")]);
        Check(((TextBlock)window.FindName("RuntimeRecoveryStatus")).Text.Contains("Injected profile error"),"other failures retain diagnostic");
        window.Close();
        Console.WriteLine("PASS bundled WebView2 selection, missing-file recovery and other-error diagnostics; no system installation required");
    }
    private static void Check(bool value,string label){if(!value)throw new Exception(label);}
}
