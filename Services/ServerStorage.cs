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

    public ServerStorage()
    {
        _storageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SplateVPN"
        );
        Directory.CreateDirectory(_storageDir);
        _serversFile = Path.Combine(_storageDir, "servers.json");
        _settingsFile = Path.Combine(_storageDir, "settings.json");
        _subscriptionsFile = Path.Combine(_storageDir, "subscriptions.json");
    }

    private readonly string _subscriptionsFile;

    public List<ServerNode> LoadServers()
    {
        try
        {
            if (File.Exists(_serversFile))
            {
                var json = File.ReadAllText(_serversFile);
                var list = JsonSerializer.Deserialize<List<ServerNode>>(json);
                if (list != null && list.Count > 0)
                {
                    bool updated = false;
                    foreach (var node in list)
                    {
                        if (!string.IsNullOrEmpty(node.RawLink) && 
                            (string.IsNullOrEmpty(node.Mldsa65Verify) || node.XmuxMaxConnections == null))
                        {
                            var parsed = LinkParser.Parse(node.RawLink);
                            if (parsed != null)
                            {
                                if (!string.IsNullOrEmpty(parsed.Mldsa65Verify) && string.IsNullOrEmpty(node.Mldsa65Verify))
                                {
                                    node.Mldsa65Verify = parsed.Mldsa65Verify;
                                    updated = true;
                                }
                                if (parsed.XmuxMaxConnections != null && node.XmuxMaxConnections == null)
                                {
                                    node.XmuxMaxConnections = parsed.XmuxMaxConnections;
                                    node.XmuxMaxRequestTimes = parsed.XmuxMaxRequestTimes;
                                    node.XmuxMaxReusableSecs = parsed.XmuxMaxReusableSecs;
                                    updated = true;
                                }
                            }
                        }
                    }
                    if (updated)
                    {
                        SaveServers(list);
                    }
                    return list;
                }
            }
        }
        catch { }

        return new List<ServerNode>();
    }

    public void SaveServers(List<ServerNode> servers)
    {
        try
        {
            var json = JsonSerializer.Serialize(servers, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_serversFile, json);
        }
        catch { }
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
        try
        {
            var json = JsonSerializer.Serialize(subscriptions, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_subscriptionsFile, json);
        }
        catch { }
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFile))
            {
                var json = File.ReadAllText(_settingsFile);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null) return s;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFile, json);
        }
        catch { }
    }
}