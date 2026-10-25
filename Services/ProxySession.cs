using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace SplateClientDesktop.Services;

public record ProxyPreflight(bool Manual, bool Pac, string Signature)
{
    public bool HasConflict => Manual || Pac;
    public static ProxyPreflight None => new(false, false, "");
}

// Tests use a disposable registry key, never the user's Internet Settings.
internal sealed class ProxySession(string registryPath, string snapshotPath, Action refresh)
{
    private readonly object _gate = new();
    private static readonly string[] Names = ["ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL"];
    private record SavedValue(string Name, bool Exists, RegistryValueKind Kind, string? Text, int Number);
    private record Snapshot(string OwnedServer, List<SavedValue> Values, bool Suspended = false, List<SavedValue>? AppliedValues = null);
    private Snapshot? _snapshot;
    private RegistryKey Open(bool writable = false) => Registry.CurrentUser.OpenSubKey(registryPath, writable)
        ?? throw new IOException("Нет доступа к настройкам системного прокси Windows.");
    private static List<SavedValue> Read(RegistryKey key) => Names.Select(name =>
    {
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var kind = value == null ? RegistryValueKind.String : key.GetValueKind(name);
        if (value != null && kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString or RegistryValueKind.DWord))
            throw new IOException("Неизвестный формат настройки системного прокси. Проверьте параметры Windows.");
        return new SavedValue(name, value != null, kind, value as string, value is int n ? n : 0);
    }).ToList();
    private static ProxyPreflight Status(List<SavedValue> values) => new(
        values.Any(v => v.Name == "ProxyEnable" && v.Exists && v.Number != 0),
        values.Any(v => v.Name == "AutoConfigURL" && v.Exists && !string.IsNullOrWhiteSpace(v.Text)),
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values))));
    private bool Owns(List<SavedValue> current) => _snapshot?.AppliedValues is { } applied
        ? applied.SequenceEqual(current)
        : _snapshot != null && Status(current) is { Manual: true, Pac: false } && current.Any(v => v.Name == "ProxyServer" && v.Text == _snapshot.OwnedServer);
    private static void Write(RegistryKey key, List<SavedValue> values)
    {
        foreach (var value in values)
            if (!value.Exists) key.DeleteValue(value.Name, false);
            else key.SetValue(value.Name, value.Kind == RegistryValueKind.DWord ? (object)value.Number : value.Text ?? "", value.Kind);
    }
    private void Persist()
    {
        if (_snapshot == null) { if (File.Exists(snapshotPath)) File.Delete(snapshotPath); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        AtomicFileSet.Write(new Dictionary<string, byte[]> { [snapshotPath] = JsonSerializer.SerializeToUtf8Bytes(_snapshot) });
    }
    public void Recover()
    {
        lock (_gate)
        {
            if (_snapshot != null || !File.Exists(snapshotPath)) return;
            var saved = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(snapshotPath))
                ?? throw new IOException("Не удалось прочитать сохранённые настройки прокси.");
            if (saved.Values.Count != Names.Length || saved.Values.Select(v => v.Name).Except(Names).Any() || saved.Values.Select(v => v.Name).Distinct().Count() != Names.Length)
                throw new IOException("Повреждена копия настроек прокси.");
            _snapshot = saved;
            if (!Restore()) throw new IOException("Не удалось восстановить системный прокси после прошлого сеанса.");
        }
    }
    public HttpProxyEndpoint OriginalHttpProxy()
    {
        lock (_gate)
        {
            using var key = Open(); var current = Read(key);
            var values = _snapshot != null && Owns(current) ? _snapshot.Values : current;
            if (!Status(values).Manual) throw new IOException("В Windows не включён ручной HTTP-прокси. Укажите адрес и порт вручную; PAC не импортируется.");
            return UpstreamProxy.ParseWindows(values.First(v => v.Name == "ProxyServer").Text ?? "");
        }
    }
    public ProxyPreflight ReadStatus() { lock (_gate) { using var key = Open(); return Status(Read(key)); } }
    public ProxyPreflight InspectForTun()
    {
        lock (_gate)
        {
            using var key = Open(); var current = Read(key);
            // Proxy → TUN would otherwise restore this hidden original proxy.
            return Status(_snapshot is { Suspended: false } && Owns(current) ? _snapshot.Values : current);
        }
    }
    private void Apply(RegistryKey key, List<SavedValue> current, Snapshot target)
    {
        var previous = _snapshot; _snapshot = target;
        try
        {
            Persist(); Write(key, target.AppliedValues!); refresh();
            if (!Read(key).SequenceEqual(target.AppliedValues!)) throw new IOException("Windows не применила настройки прокси. Проверьте политики системы.");
        }
        catch
        {
            // Keep the durable restoration record if rollback itself fails.
            Write(key, current); refresh(); _snapshot = previous; Persist(); throw;
        }
    }
    public void Enable(int httpPort)
    {
        lock (_gate)
        {
            Recover(); using var key = Open(true); var current = Read(key);
            if (_snapshot != null && !Owns(current)) { _snapshot = null; Persist(); }
            var server = $"http=127.0.0.1:{httpPort};https=127.0.0.1:{httpPort}";
            var expected = new List<SavedValue> {
                new("ProxyEnable", true, RegistryValueKind.DWord, null, 1), new("ProxyServer", true, RegistryValueKind.String, server, 0),
                new("ProxyOverride", true, RegistryValueKind.String, "<local>;localhost;127.*;10.*;192.168.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*", 0),
                new("AutoConfigURL", false, RegistryValueKind.String, null, 0) };
            Apply(key, current, new(server, _snapshot?.Values ?? current, false, expected));
        }
    }
    public void SuspendForTun(ProxyPreflight approved)
    {
        lock (_gate)
        {
            using (var existing = Open())
                if (_snapshot is { Suspended: true } && Owns(Read(existing)) &&
                    (Status(Read(existing)) == approved || Status(_snapshot.Values) == approved)) return;
            if (InspectForTun() != approved) throw new IOException("Настройки прокси изменились после проверки. Повторите подключение.");
            if (!Restore()) throw new IOException("Не удалось освободить системный прокси перед запуском TUN.");
            using var key = Open(approved.HasConflict); var current = Read(key);
            if (Status(current) != approved) throw new IOException("Настройки прокси изменились. Повторите подключение.");
            if (!approved.HasConflict) return;
            var expected = current.Select(v => v.Name == "ProxyEnable" ? new SavedValue(v.Name, true, RegistryValueKind.DWord, null, 0) :
                v.Name == "AutoConfigURL" ? new SavedValue(v.Name, false, RegistryValueKind.String, null, 0) : v).ToList();
            Apply(key, current, new("", current, true, expected));
            if (ReadStatus().HasConflict) throw new IOException("Прокси остался включён. TUN не запущен: проверьте параметры Windows.");
        }
    }
    public bool Restore()
    {
        lock (_gate)
        {
            if (_snapshot == null) return true;
            try
            {
                using var key = Open(true);
                if (Owns(Read(key))) { Write(key, _snapshot.Values); refresh(); if (!Read(key).SequenceEqual(_snapshot.Values)) return false; }
                _snapshot = null; Persist(); return true;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Proxy restore: " + ex.Message); return false; }
        }
    }
}
