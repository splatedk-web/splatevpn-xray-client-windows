using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public record HttpProxyEndpoint(string Host, int Port);
public static class UpstreamProxy
{
    public static HttpProxyEndpoint Validate(string host, int port)
    {
        host = host.Trim().Trim('[', ']');
        if (port is < 1 or > 65535 || host.Length is < 1 or > 253 ||
            (!IPAddress.TryParse(host, out _) && Uri.CheckHostName(host) != UriHostNameType.Dns))
            throw new ArgumentException("Внешний HTTP-прокси: укажите имя хоста без http:// и порт от 1 до 65535.");
        return new(host, port);
    }
    public static HttpProxyEndpoint? FromSettings(AppSettings s) => s.UpstreamProxyEnabled ? Validate(s.UpstreamProxyHost, s.UpstreamProxyPort) : null;
    public static HttpProxyEndpoint ParseWindows(string server)
    {
        var entries = server.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chosen = entries.FirstOrDefault(v => v.StartsWith("https=",StringComparison.OrdinalIgnoreCase)) ?? entries.FirstOrDefault(v => v.StartsWith("http=",StringComparison.OrdinalIgnoreCase)) ?? entries.FirstOrDefault(v => !v.Contains('='));
        if (chosen == null) throw new ArgumentException("В Windows не найден ручной HTTP/HTTPS-прокси. Введите адрес и порт вручную. PAC-скрипты автоматически не импортируются.");
        var authority = chosen.Contains('=') ? chosen.Split('=',2)[1] : chosen;
        if (!Uri.TryCreate(authority.Contains("://") ? authority : "http://"+authority,UriKind.Absolute,out var uri) || uri.Scheme != "http" || uri.UserInfo.Length>0 || uri.AbsolutePath!="/" || uri.Query.Length>0 || uri.Fragment.Length>0)
            throw new ArgumentException("Не удалось прочитать HTTP-прокси Windows. Укажите хост и порт вручную.");
        return Validate(uri.Host,uri.Port);
    }
    public static void Chain(JsonArray outbounds, HttpProxyEndpoint endpoint, string? uplink = null)
    {
        foreach (var outbound in outbounds.OfType<JsonObject>())
        {
            if (outbound["protocol"]?.ToString() is "blackhole" or "dns") continue;
            var stream = outbound["streamSettings"] as JsonObject; if (stream == null) outbound["streamSettings"] = stream = new JsonObject();
            var sockopt = stream["sockopt"] as JsonObject ?? new(); if (stream["sockopt"] == null) stream["sockopt"] = sockopt;
            if (sockopt["dialerProxy"] == null) { sockopt["dialerProxy"]="upstream-http"; if (!outbound.ToJsonString().Contains(".internal")) sockopt["domainStrategy"]="AsIs"; }
        }
        var upstream = new JsonObject { ["tag"]="upstream-http",["protocol"]="http",["settings"]=new JsonObject{["servers"]=new JsonArray{new JsonObject{["address"]=endpoint.Host,["port"]=endpoint.Port}}} };
        if (!string.IsNullOrWhiteSpace(uplink)) upstream["streamSettings"]=new JsonObject{["sockopt"]=new JsonObject{["interface"]=uplink,["domainStrategy"]="UseIPv4"}};
        outbounds.Add(upstream);
    }
    public static void ValidateTransport(ServerNode node)
    {
        if (node.Network?.ToLowerInvariant() is "kcp" or "mkcp" or "quic" || node.Protocol?.ToLowerInvariant() is "hysteria" or "hysteria2" or "wireguard")
            throw new ArgumentException("Внешний HTTP-прокси поддерживает TCP CONNECT. Выберите VPN-сервер с TCP, WebSocket, gRPC или XHTTP.");
    }
    public static async Task CheckConnectAsync(HttpProxyEndpoint endpoint, string target, int port, CancellationToken ct)
    {
        if (target.Length is < 1 or > 253 || target.Any(c=>char.IsWhiteSpace(c)||c is '/' or '\\' or '@') || port is < 1 or > 65535) throw new ArgumentException("Некорректный адрес VPN-сервера для CONNECT.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var client=new TcpClient();await client.ConnectAsync(endpoint.Host,endpoint.Port,timeout.Token);var stream=client.GetStream();
            var host=target.Contains(':')&&!target.StartsWith('[')?$"[{target}]":target;var authority=$"{host}:{port}";
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\nProxy-Connection: Keep-Alive\r\n\r\n"),timeout.Token);
            var bytes=new List<byte>();var one=new byte[1];
            while(bytes.Count<16384){if(await stream.ReadAsync(one,timeout.Token)==0)throw new IOException("Внешний прокси закрыл соединение без ответа на CONNECT.");bytes.Add(one[0]);if(bytes.Count>=4&&bytes.TakeLast(4).SequenceEqual(new byte[]{13,10,13,10}))break;}
            if (bytes.Count < 4 || !bytes.TakeLast(4).SequenceEqual(new byte[]{13,10,13,10})) throw new IOException("Слишком большой ответ HTTP-прокси.");
            var first=Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n")[0];var parts=first.Split(' ');
            if(parts.Length<2||!parts[0].StartsWith("HTTP/")||!int.TryParse(parts[1],out var status))throw new IOException("Внешний прокси вернул некорректный ответ CONNECT.");
            if(status==407)throw new IOException("Внешний прокси требует авторизацию. Эта настройка рассчитана на HTTP-прокси без логина и пароля.");
            if(status!=200)throw new IOException($"Внешний прокси не разрешил CONNECT к выбранному VPN-серверу (HTTP {status}). Проверьте доступ и разрешённый порт сервера, например 443.");
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){throw new IOException("Внешний HTTP-прокси не ответил за 10 секунд. Настройки Windows не изменены.");}
        catch(SocketException ex){throw new IOException("Не удалось подключиться к внешнему HTTP-прокси. Проверьте сеть, адрес и порт.",ex);}
    }
}
