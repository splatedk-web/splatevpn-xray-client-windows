using System.IO;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public static class PresetSubscriptions
{
    public record Preset(string Id, string Name, string Url, string File, string FallbackUrl = "");
    public static readonly Preset[] Catalog =
    [
        new("preset_white_cidr", "Белый список (CIDR RU)", "https://translate.yandex.ru/translate?url=https://white.igareck.net/WHITE-CIDR-RU-all.txt&lang=de-de", "preset_white_cidr.txt", "https://github.com/igareck/vpn-configs-for-russia/raw/refs/heads/main/WHITE-CIDR-RU-all.txt"),
        new("preset_harvester", "Harvester (Рабочие узлы)", "https://raw.githubusercontent.com/luxxuria/harvester/refs/heads/main/ping_tested.txt", "preset_harvester.txt"),
        new("preset_white_universal", "Белый список (Universal)", "https://raw.githubusercontent.com/zieng2/wl/refs/heads/main/vless_universal.txt", "preset_white_universal.txt")
    ];

    // Assets are bundled so first launch works offline. Deletions are respected until explicit restore.
    public static int Seed(IList<SubscriptionItem> subscriptions, IList<ServerNode> servers, string assetDirectory)
    {
        var staged = new List<(SubscriptionItem Subscription, List<ServerNode> Nodes)>();
        foreach (var preset in Catalog)
        {
            if (subscriptions.Any(s => s.Id == preset.Id)) continue;
            var raw = File.ReadAllText(Path.Combine(assetDirectory, preset.File));
            var nodes = LinkParser.ParseMultiple(SubscriptionService.TryDecodeBase64(raw));
            if (nodes.Count == 0) throw new InvalidDataException($"Встроенная подписка «{preset.Name}» не содержит серверов.");
            foreach (var node in nodes)
            {
                node.SubscriptionId = preset.Id;
                node.SubscriptionGroup = preset.Name;
            }
            staged.Add((new SubscriptionItem
            {
                Id = preset.Id,
                Name = preset.Name,
                Url = preset.Url,
                FallbackUrl = preset.FallbackUrl,
                NodeCount = nodes.Count,
                AutoUpdate = true
            }, nodes));
        }
        foreach (var (subscription, nodes) in staged)
        {
            subscriptions.Add(subscription);
            foreach (var node in nodes) servers.Add(node);
        }
        return staged.Sum(s => s.Nodes.Count);
    }

    public static List<ServerNode> Merge(IEnumerable<ServerNode> previous, IEnumerable<ServerNode> incoming)
    {
        // Preserve identity/preferences only for the same endpoint and credentials.
        static string Key(ServerNode n) => $"{n.Protocol.ToLowerInvariant()}|{n.Address.ToLowerInvariant()}|{n.Port}|{n.Uuid}|{n.Encryption}";
        var old = previous.GroupBy(Key).ToDictionary(g => g.Key, g => new Queue<ServerNode>(g));
        return incoming.Select(node =>
        {
            if (old.TryGetValue(Key(node), out var matches) && matches.TryDequeue(out var match))
            {
                node.Id = match.Id; node.IsFavorite = match.IsFavorite;
                node.Ping = match.Ping; node.IsSelected = match.IsSelected;
            }
            return node;
        }).ToList();
    }

    public static bool Owns(SubscriptionItem subscription, ServerNode node) =>
        !string.IsNullOrEmpty(node.SubscriptionId) ? node.SubscriptionId == subscription.Id :
        !node.IsCustom && !string.IsNullOrWhiteSpace(node.SubscriptionGroup) &&
        node.SubscriptionGroup is not ("Личные" or "Основное" or "Все" or "Предустановленные" or "Default") &&
        node.SubscriptionGroup.Equals(subscription.Name, StringComparison.OrdinalIgnoreCase);
}
