using System.Diagnostics;
using System.IO;

namespace SplateClientDesktop.Services;

public static class WebViewRuntimeProvider
{
    public static string GetBrowserFolder(string baseDirectory)
    {
        var folder = Path.Combine(baseDirectory, "runtime", "WebView2");
        foreach (var file in new[] { "msedgewebview2.exe", "msedge.dll", "icudtl.dat" })
            if (!File.Exists(Path.Combine(folder, file)))
                throw new FileNotFoundException("Не хватает файлов интерфейса. Распакуйте архив целиком или переустановите SplateVPN. Папка runtime должна находиться рядом с SplateClient.exe.", Path.Combine(folder, file));
        return folder;
    }

    public static async Task PrepareAsync(string folder)
    {
        // Microsoft requires these read/execute grants for unpackaged Fixed Runtime v120+ on Windows 10.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Разместите папку runtime на локальном диске без символической ссылки.");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "icacls.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { folder, "/grant", "*S-1-15-2-2:(OI)(CI)(RX)", "*S-1-15-2-1:(OI)(CI)(RX)" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Не удалось подготовить файлы интерфейса.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        var details = await errors;
        if (process.ExitCode != 0) throw new IOException("Нет доступа к папке runtime. Распакуйте приложение в доступную вам папку или запустите установщик. " + details);
    }
}
