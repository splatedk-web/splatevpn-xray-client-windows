using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class StorageChecks
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "splate-storage-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = new ServerStorage(Path.Combine(root, "source"));
            var target = new ServerStorage(Path.Combine(root, "target"));
            var node = new ServerNode { Id = "test-node", Address = "test.example", Port = 443 };
            source.SaveServers(new() { node });
            source.SaveSubscriptions(new() { new SubscriptionItem { Id = "sub", Name = "Test", Url = "https://example.com/sub" } });
            source.SaveSettings(new AppSettings { EnableAiXboxDns = true, Theme = "light", GlassBlurEnabled = false, GlassTransparency = .6, AiDnsProvider = "geohide_eu", AiTlsFragment = true, AdBlockAllowlist = "example.com" });
            var exporter = new ConfigExportImportService(source);
            var importer = new ConfigExportImportService(target);
            var file = Path.Combine(root, "backup.spcfg");
            await exporter.ExportAsync(file, "test-only");
            await importer.ImportAsync(file, "test-only");
            Check(target.LoadServers().Single().Id == "test-node", "server roundtrip");
            Check(target.LoadSubscriptions().Single().Id == "sub", "subscription roundtrip");
            Check(!target.LoadSettings().EnableAiXboxDns && target.LoadSettings().EnableAiDns && target.LoadSettings().AiRoutingMode == "dns_ai" && target.LoadSettings().Theme == "light", "legacy Xbox migrates to selected SmartDNS during import");
            Check(!target.LoadSettings().GlassBlurEnabled && target.LoadSettings().GlassTransparency == .6, "blur and glass settings survive export/import");
            Check(target.LoadSettings().AiDnsProvider == "geohide_eu" && target.LoadSettings().AiTlsFragment && target.LoadSettings().AdBlockAllowlist == "example.com", "provider, fragment and exceptions roundtrip");
            Check(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!.GlassBlurEnabled, "old settings enable blur by default");
            var oldServers = File.ReadAllText(Path.Combine(root, "target", "servers.json"));
            var oldSettings = File.ReadAllText(Path.Combine(root, "target", "settings.json"));
            await File.WriteAllBytesAsync(file, new byte[] { 1, 2, 3 });
            await RejectAsync(() => importer.ImportAsync(file, "test-only"));
            node.Port = 70000;
            source.SaveServers(new() { node });
            await exporter.ExportAsync(file, "test-only");
            await RejectAsync(() => importer.ImportAsync(file, "test-only"));
            Check(File.ReadAllText(Path.Combine(root, "target", "servers.json")) == oldServers, "invalid backup preserves servers");
            Check(File.ReadAllText(Path.Combine(root, "target", "settings.json")) == oldSettings, "invalid backup preserves settings");
            Directory.CreateDirectory(Path.Combine(root, "target", "servers.json.tmp"));
            bool failed = false;
            try { target.SaveServers(new()); } catch (IOException) { failed = true; } catch (UnauthorizedAccessException) { failed = true; }
            Check(failed, "write failure reported");
            Check(File.ReadAllText(Path.Combine(root, "target", "servers.json")) == oldServers, "failed save preserves previous file");
            Console.WriteLine("PASS backup roundtrip, invalid import preservation, atomic server save failure");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    private static async Task RejectAsync(Func<Task> action)
    {
        try { await action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid backup accepted");
    }
}
