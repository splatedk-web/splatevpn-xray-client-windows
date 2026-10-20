using System.Collections.Generic;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public interface IServerStorage
{
    List<ServerNode> LoadServers();
    void SaveServers(List<ServerNode> servers);
    System.Threading.Tasks.Task SaveServersAsync(List<ServerNode> servers);
    List<SubscriptionItem> LoadSubscriptions();
    void SaveSubscriptions(List<SubscriptionItem> subscriptions);
    void SaveCatalog(List<ServerNode> servers, List<SubscriptionItem> subscriptions) => SaveCatalogWithRollback(this, servers, subscriptions);
    static void SaveCatalogWithRollback(IServerStorage storage, List<ServerNode> servers, List<SubscriptionItem> subscriptions)
    {
        var previousServers = storage.LoadServers(); var previousSubscriptions = storage.LoadSubscriptions();
        try { storage.SaveServers(servers); storage.SaveSubscriptions(subscriptions); }
        catch (Exception original)
        {
            try { storage.SaveServers(previousServers); storage.SaveSubscriptions(previousSubscriptions); }
            catch (Exception rollback) { throw new AggregateException("Ошибка записи каталога и восстановления предыдущей версии.", original, rollback); }
            throw;
        }
    }
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    void SaveBackup(List<ServerNode> servers, List<SubscriptionItem> subscriptions, AppSettings settings)
    {
        var oldServers = LoadServers(); var oldSubscriptions = LoadSubscriptions(); var oldSettings = LoadSettings();
        try { SaveServers(servers); SaveSubscriptions(subscriptions); SaveSettings(settings); }
        catch (Exception original)
        {
            var errors = new List<Exception> { original };
            try { SaveServers(oldServers); } catch (Exception ex) { errors.Add(ex); }
            try { SaveSubscriptions(oldSubscriptions); } catch (Exception ex) { errors.Add(ex); }
            try { SaveSettings(oldSettings); } catch (Exception ex) { errors.Add(ex); }
            if (errors.Count > 1) throw new AggregateException("Ошибка импорта и восстановления предыдущих данных.", errors);
            throw;
        }
    }
}
