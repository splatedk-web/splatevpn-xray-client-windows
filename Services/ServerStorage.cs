using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public class ServerStorage : IServerStorage
{
    private readonly string _storageDir;
    private readonly string _serversFile;
    private readonly string _settingsFile;

    public ServerStorage(string? storageDirectory = null)
    {
        _storageDir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SplateVPN"
        );
        Directory.CreateDirectory(_storageDir);
        _serversFile = Path.Combine(_storageDir, "servers.json");
        _settingsFile = Path.Combine(_storageDir, "settings.json");
        _subscriptionsFile = Path.Combine(_storageDir, "subscriptions.json");
    }

    private readonly string _subscriptionsFile;
    public string? LastLoadWarning { get; private set; }

    public List<ServerNode> LoadServers()
    {
        lock (_ioLock) return LoadServersCore();
    }

    private List<ServerNode> LoadServersCore()
    {
        LastLoadWarning = null;
        try
        {
            if (File.Exists(_serversFile))
            {
                var json = File.ReadAllText(_serversFile);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Каталог серверов должен содержать массив.");
                var list = new List<ServerNode>();
                var skipped = 0;
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    try
                    {
                        var node = element.Deserialize<ServerNode>();
                        if (node == null)
                        { skipped++; continue; }
                        node.Encryption ??= "none";
                        list.Add(node);
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
                    { skipped++; }
                }
                if (skipped > 0) LastLoadWarning = $"Пропущено повреждённых записей серверов: {skipped}. Исходный файл сохранён.";
                if (list != null && list.Count > 0)
                {
                    bool updated = false;
                    foreach (var node in list)
                    {
                        if (!string.IsNullOrEmpty(node.RawLink) &&
                            (string.IsNullOrEmpty(node.Mldsa65Verify) ||
                             node.XmuxMaxConnections == null ||
                             node.Encryption.Contains(@"\u0026") ||
                             node.Encryption.Contains("extra=") ||
                             (node.Alpn != null && node.Alpn.Contains(@"\u0026")) ||
                             node.RawLink.Contains(@"\u0026")))
                        {
                            var parsed = LinkParser.Parse(node.RawLink, node.SubscriptionGroup);
                            if (parsed != null)
                            {
                                node.Network = parsed.Network;
                                node.Security = parsed.Security;
                                node.Encryption = parsed.Encryption;
                                node.PublicKey = parsed.PublicKey;
                                node.ShortId = parsed.ShortId;
                                node.SpiderX = parsed.SpiderX;
                                node.Sni = parsed.Sni;
                                node.Fingerprint = parsed.Fingerprint;
                                node.Mldsa65Verify = parsed.Mldsa65Verify;
                                node.Path = parsed.Path;
                                node.Host = parsed.Host;
                                node.Mode = parsed.Mode;
                                node.Padding = parsed.Padding;
                                node.Alpn = parsed.Alpn;
                                node.Flow = parsed.Flow;
                                node.XmuxMaxConnections = parsed.XmuxMaxConnections;
                                node.XmuxMaxRequestTimes = parsed.XmuxMaxRequestTimes;
                                node.XmuxMaxReusableSecs = parsed.XmuxMaxReusableSecs;
                                node.RawLink = node.RawLink.Replace(@"\u0026", "&").Replace("&amp;", "&");
                                updated = true;
                            }
                        }
                    }
                    if (updated && skipped == 0)
                    {
                        try { SaveServers(list); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { LastLoadWarning = "Серверы загружены, но миграцию не удалось сохранить: " + ex.Message; }
                    }
                    return list;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            LastLoadWarning = "Не удалось прочитать каталог серверов: " + ex.Message;
            throw new InvalidDataException(LastLoadWarning, ex);
        }

        return new List<ServerNode>();
    }

    private readonly object _ioLock = new();
    public void SaveBackup(List<ServerNode> servers, List<SubscriptionItem> subscriptions, AppSettings settings)
    {
        lock (_ioLock)
            AtomicFileSet.Write(new Dictionary<string, byte[]>
            {
                [_serversFile] = JsonSerializer.SerializeToUtf8Bytes(servers),
                [_subscriptionsFile] = JsonSerializer.SerializeToUtf8Bytes(subscriptions),
                [_settingsFile] = JsonSerializer.SerializeToUtf8Bytes(settings)
            });
    }
    public void SaveCatalog(List<ServerNode> servers, List<SubscriptionItem> subscriptions)
    {
        lock (_ioLock) IServerStorage.SaveCatalogWithRollback(this, servers, subscriptions);
    }

    public void SaveServers(List<ServerNode> servers)
    {
        lock (_ioLock)
        {
            var json = JsonSerializer.Serialize(servers, new JsonSerializerOptions { WriteIndented = true });
            var temp = _serversFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _serversFile, overwrite: true);
        }
    }

    public Task SaveServersAsync(List<ServerNode> servers)
    {
        return Task.Run(() => SaveServers(servers));
    }

    public List<SubscriptionItem> LoadSubscriptions()
    {
        try
        {
            if (File.Exists(_subscriptionsFile))
            {
                var json = File.ReadAllText(_subscriptionsFile);
                var list = JsonSerializer.Deserialize<List<SubscriptionItem>>(json);
                if (list != null) return list;
            }
        }
        catch { }

        // Migration from legacy single subscription in Settings
        var settings = LoadSettings();
        if (!string.IsNullOrWhiteSpace(settings.SubscriptionUrl))
        {
            var host = "Подписка 1";
            try
            {
                if (Uri.TryCreate(settings.SubscriptionUrl, UriKind.Absolute, out var u))
                    host = u.Host;
            }
            catch { }

            var legacy = new List<SubscriptionItem>
            {
                new SubscriptionItem
                {
                    Name = host,
                    Url = settings.SubscriptionUrl,
                    DataLimit = settings.SubscriptionDataLimit,
                    DataUsed = settings.SubscriptionDataUsed,
                    ExpiryTime = long.TryParse(settings.SubscriptionExpiry, out var exp) ? exp : 0,
                    AutoUpdate = settings.AutoUpdateSubscription
                }
            };
            SaveSubscriptions(legacy);
            return legacy;
        }

        return new List<SubscriptionItem>();
    }

    public void SaveSubscriptions(List<SubscriptionItem> subscriptions)
    {
        lock (_ioLock)
        {
            var json = JsonSerializer.Serialize(subscriptions, new JsonSerializerOptions { WriteIndented = true });
            var temp = _subscriptionsFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _subscriptionsFile, overwrite: true);
        }
    }

    public AppSettings LoadSettings()
    {
        lock (_ioLock)
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    var json = File.ReadAllText(_settingsFile);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        if (SettingsCompatibility.Normalize(s))
                        {
                            var backup = Path.Combine(_storageDir, "settings.before-2.9.1.json");
                            if (!File.Exists(backup)) File.Copy(_settingsFile, backup);
                            SaveSettings(s);
                        }
                        return s;
                    }
                }
            }
            catch { }
            return new AppSettings { EnableAiDns = true };
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        lock (_ioLock)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            var temp = _settingsFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _settingsFile, overwrite: true);
        }
    }
}
