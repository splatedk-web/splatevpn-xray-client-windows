using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public static class LanSharing
{
    public static void Validate(AppSettings settings)
    {
        if (settings.LanBindAddress != "" && (!IPAddress.TryParse(settings.LanBindAddress, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast)))
            throw new ArgumentException("Для раздачи выберите IPv4-адрес сетевого интерфейса.");
        if (settings.ShareLan && settings.LanAuthEnabled && (settings.LanBindAddress == "" || string.IsNullOrEmpty(settings.LanUsername) || string.IsNullOrEmpty(settings.LanPassword) || settings.LanUsername.Length is < 1 or > 64 || settings.LanPassword.Length is < 8 or > 128 ||
            settings.LanUsername.Any(char.IsControl) || settings.LanPassword.Any(char.IsControl)))
            throw new ArgumentException("Для авторизации выберите интерфейс, задайте логин и пароль от 8 символов.");
    }
    public static object[] Interfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            !n.Name.Contains("Splate", StringComparison.OrdinalIgnoreCase) && !n.Name.Contains("tun", StringComparison.OrdinalIgnoreCase))
        .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
            .Select(a => (object)new { address = a.Address.ToString(), name = n.Name })).ToArray();
}
