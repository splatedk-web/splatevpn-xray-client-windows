using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class AuditFixChecks
{
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { return; }
        throw new Exception("Operation unexpectedly succeeded");
    }

    public static async Task RunAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var temporary = Path.Combine(Path.GetTempPath(), "splate-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var source = new ServerStorage(Path.Combine(temporary, "source"));
            source.SaveServers(new() { new ServerNode { Address = "new.example", Id = "new" } });
            source.SaveSubscriptions(new() { new SubscriptionItem { Name = "new" } });
            source.SaveSettings(new AppSettings());
            var backup = Path.Combine(temporary, "backup.spcfg");
            await new ConfigExportImportService(source).ExportAsync(backup, "password-one");
            Check(!ConfigExportImportService.IsLegacyBackup(backup), "new backup format");
            var first = File.ReadAllBytes(backup);
            await new ConfigExportImportService(source).ExportAsync(backup, "password-one");
            Check(!first.SequenceEqual(File.ReadAllBytes(backup)), "fresh salt and nonce");

            var destination = Path.Combine(temporary, "destination");
            var target = new ServerStorage(destination);
            target.SaveServers(new() { new ServerNode { Address = "old.example", Id = "old" } });
            target.SaveSettings(new AppSettings { Theme = "light" });
            var original = File.ReadAllBytes(Path.Combine(destination, "servers.json"));
            var importer = new ConfigExportImportService(target);
            await Reject(() => importer.ImportAsync(backup, "wrong-password"));
            first = File.ReadAllBytes(backup); first[^1] ^= 1; File.WriteAllBytes(backup, first);
            await Reject(() => importer.ImportAsync(backup, "password-one"));
            Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(destination, "servers.json"))), "tampering/wrong password preserve data");
            await new ConfigExportImportService(source).ExportAsync(backup, "password-one");
            Directory.CreateDirectory(Path.Combine(destination, "subscriptions.json"));
            await Reject(() => importer.ImportAsync(backup, "password-one"));
            Check(original.SequenceEqual(File.ReadAllBytes(Path.Combine(destination, "servers.json"))), "import rolls back committed server file");
            Check(target.LoadSettings().Theme == "light", "failed import leaves settings intact");
            Directory.Delete(Path.Combine(destination, "subscriptions.json"));
            await importer.ImportAsync(backup, "password-one");
            Check(target.LoadServers().Single().Id == "new", "successful transaction");
            target.SaveServers(new() { new ServerNode { Address = "old.example", Id = "old" } });
            target.SaveSubscriptions(new() { new SubscriptionItem { Name = "old" } });
            var beforeServers = File.ReadAllBytes(Path.Combine(destination, "servers.json"));
            var beforeSubscriptions = File.ReadAllBytes(Path.Combine(destination, "subscriptions.json"));
            var settingsFile = Path.Combine(destination, "settings.json");
            File.Delete(settingsFile); Directory.CreateDirectory(settingsFile);
            await Reject(() => importer.ImportAsync(backup, "password-one"));
            Check(beforeServers.SequenceEqual(File.ReadAllBytes(Path.Combine(destination, "servers.json"))) &&
                beforeSubscriptions.SequenceEqual(File.ReadAllBytes(Path.Combine(destination, "subscriptions.json"))), "third replacement failure rolls back both catalog files");
            Directory.Delete(settingsFile);

            // Reproduce an old backup produced by the previous UI.
            using (var aes = Aes.Create())
            {
                aes.Key = Rfc2898DeriveBytes.Pbkdf2("splate_pass", Encoding.UTF8.GetBytes("SplateVPN_Salt_2025"), 100_000, HashAlgorithmName.SHA256, 32);
                var plain = JsonSerializer.SerializeToUtf8Bytes(new { Servers = source.LoadServers(), Subscriptions = source.LoadSubscriptions(), Settings = source.LoadSettings() });
                using var encryptor = aes.CreateEncryptor();
                File.WriteAllBytes(backup, aes.IV.Concat(encryptor.TransformFinalBlock(plain, 0, plain.Length)).ToArray());
            }
            Check(ConfigExportImportService.IsLegacyBackup(backup), "legacy detection");
            await importer.ImportAsync(backup, "");
            Check(target.LoadServers().Single().Id == "new", "legacy UI backup import");

            var serverFile = Path.Combine(destination, "servers.json");
            File.WriteAllText(serverFile, "[null,{\"Address\":\"valid.example\",\"Encryption\":null},{\"Port\":\"invalid\"}]");
            var damaged = File.ReadAllText(serverFile);
            Check(target.LoadServers().Single().Address == "valid.example" && target.LastLoadWarning != null, "bad entries isolated");
            Check(File.ReadAllText(serverFile) == damaged, "damaged catalog not automatically overwritten");
            File.WriteAllText(serverFile, "[{\"Address\":\"valid.example\",\"RawLink\":\"vless://00000000-0000-0000-0000-000000000001@valid.example:443?security=none\"}]");
            Directory.CreateDirectory(serverFile + ".tmp");
            Check(target.LoadServers().Count == 1 && target.LastLoadWarning != null, "migration write failure retains loaded nodes");
            Directory.Delete(serverFile + ".tmp");
            File.WriteAllText(serverFile, "broken json");
            await Reject(() => Task.Run(() => target.LoadServers()));
            Check(File.ReadAllText(serverFile) == "broken json", "unreadable catalog reports error and stays intact");
            Console.WriteLine("PASS audit storage: password, tampering, legacy backup, import rollback, damaged entries and migration failure");

            var core = Path.Combine(temporary, "core"); Directory.CreateDirectory(core);
            File.Copy(Path.Combine(root, "core", "xray.exe"), Path.Combine(core, "xray.exe"));
            var geoip = File.ReadAllBytes(Path.Combine(root, "core", "geoip.dat"));
            var geosite = File.ReadAllBytes(Path.Combine(root, "core", "geosite.dat"));
            var ipPath = Path.Combine(core, "geoip.dat"); var sitePath = Path.Combine(core, "geosite.dat");
            File.WriteAllText(ipPath, "previous-ip"); File.WriteAllText(sitePath, "previous-site");
            async Task<bool> Update(byte[] ip, byte[] site, bool failSecond = false)
            {
                using var http = new HttpClient(new GeoHandler(ip, site, failSecond));
                return await GeoAssetUpdaterService.UpdateGeoAssetsAsync(core, client: http);
            }
            Check(!await Update(new byte[10], geosite), "short database rejected");
            Check(!await Update(geoip, geosite, true), "second download failure rejected");
            Check(!await Update(new byte[1024 * 1024 + 1], geosite), "large malformed protobuf rejected by Xray");
            Check(File.ReadAllText(ipPath) == "previous-ip" && File.ReadAllText(sitePath) == "previous-site", "validation failures preserve both files");
            File.Delete(sitePath); Directory.CreateDirectory(sitePath);
            Check(!await Update(geoip, geosite), "second replacement failure reported");
            Check(File.ReadAllText(ipPath) == "previous-ip", "geo replacement rollback");
            Directory.Delete(sitePath);
            Check(await Update(geoip, geosite), "real bundled databases accepted by Xray");
            Check(File.ReadAllBytes(ipPath).SequenceEqual(geoip) && File.ReadAllBytes(sitePath).SequenceEqual(geosite), "both validated databases installed");
            Console.WriteLine("PASS audit geo: short/malformed files, partial download, replacement rollback and real Xray validation");
        }
        finally { Directory.Delete(temporary, true); }
    }

    private sealed class GeoHandler(byte[] ip, byte[] site, bool failSecond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var isIp = request.RequestUri!.AbsolutePath.EndsWith("/geoip.dat");
            return Task.FromResult(new HttpResponseMessage(!isIp && failSecond ? HttpStatusCode.BadGateway : HttpStatusCode.OK)
            { Content = new ByteArrayContent(isIp ? ip : site) });
        }
    }
}
