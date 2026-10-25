using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace SplateClientDesktop.Services;

public record NetworkProfileContext(string Key, string Label)
{
    public static async Task<NetworkProfileContext> CaptureAsync(CancellationToken ct)
    {
        var uplink = await TunDeviceManager.GetUplinkAsync(ct);
        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name == uplink.Name && n.OperationalStatus == OperationalStatus.Up)
            ?? throw new InvalidOperationException("Активный сетевой интерфейс не найден.");
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
        {
            if (!Guid.TryParse(nic.Id, out var id)) throw new InvalidOperationException("Не удалось определить Wi-Fi.");
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out var handle) != 0) throw new InvalidOperationException("Служба Wi-Fi недоступна.");
            try
            {
                // WLAN_CONNECTION_ATTRIBUTES: state/mode, 256 WCHAR profile name, DOT11_SSID.
                var result = WlanQueryInterface(handle, ref id, 7, IntPtr.Zero, out var size, out var data, out _);
                if (result != 0) throw new InvalidOperationException("Имя Wi-Fi недоступно. Проверьте разрешение Windows на доступ к расположению.");
                try
                {
                    if (size < 556) throw new InvalidOperationException("Неполный ответ службы Wi-Fi.");
                    var length = Marshal.ReadInt32(data, 520);
                    if (length is < 1 or > 32) throw new InvalidOperationException("Не удалось прочитать имя Wi-Fi.");
                    var bytes = new byte[length]; Marshal.Copy(IntPtr.Add(data, 524), bytes, 0, length);
                    return new("wifi:" + Convert.ToHexString(bytes), "Wi-Fi · " + Encoding.UTF8.GetString(bytes));
                }
                finally { WlanFreeMemory(data); }
            }
            finally { WlanCloseHandle(handle, IntPtr.Zero); }
        }
        var mobile = (int)nic.NetworkInterfaceType is 243 or 244;
        return new((mobile ? "mobile:" : "adapter:") + nic.Id, (mobile ? "Мобильная сеть · " : "Сетевой интерфейс · ") + nic.Name);
    }
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid id, int opcode, IntPtr reserved, out uint size, out IntPtr data, out int valueType);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr data);
}
