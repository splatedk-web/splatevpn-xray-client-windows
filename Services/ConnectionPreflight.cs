using System.IO;
using System.Net;
using System.Net.Sockets;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public static class ConnectionPreflight
{
    public static void Check(string root, AppSettings settings, IReadOnlyCollection<int> ownedPorts)
    {
        if (settings.HttpPort is < 1 or > 65535 || settings.SocksPort is < 1 or > 65535 || settings.HttpPort == settings.SocksPort)
            throw new IOException("Проверка перед подключением: укажите разные HTTP/SOCKS-порты от 1 до 65535.");
        foreach (var file in settings.EnableTunMode ? new[] { "xray.exe", "tun2socks.exe", "wintun.dll" } : new[] { "xray.exe" })
            if (!File.Exists(Path.Combine(root, "core", file))) throw new IOException($"Проверка перед подключением: отсутствует core/{file}. Переустановите приложение или распакуйте portable полностью.");
        foreach (var (name, port) in new[] { ("HTTP", settings.HttpPort), ("SOCKS", settings.SocksPort) })
        {
            if (ownedPorts.Contains(port)) continue;
            try
            {
                var address = settings.ShareLan ? IPAddress.Any : IPAddress.Loopback;
                using var listener = new TcpListener(address, port); listener.ExclusiveAddressUse = true; listener.Start();
                if (name == "SOCKS") { using var udp = new UdpClient(AddressFamily.InterNetwork); udp.ExclusiveAddressUse = true; udp.Client.Bind(new IPEndPoint(address, port)); }
            }
            catch (SocketException error) { throw new IOException($"Проверка перед подключением: порт {name} {port} занят или недоступен. Закройте другой прокси/VPN либо измените порты SplateVPN в настройках.", error); }
        }
    }
}
