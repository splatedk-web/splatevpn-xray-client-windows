using System.Collections.Generic;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public interface IServerStorage
{
    List<ServerNode> LoadServers();
    void SaveServers(List<ServerNode> servers);
    List<SubscriptionItem> LoadSubscriptions();
    void SaveSubscriptions(List<SubscriptionItem> subscriptions);
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
}
