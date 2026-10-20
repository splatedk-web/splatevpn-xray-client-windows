using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public static class ServerShare
{
    public static string Link(ServerNode n, bool preserveRaw = true)
    {
        if (preserveRaw && !string.IsNullOrWhiteSpace(n.RawLink)) return n.RawLink;
        var protocol = n.Protocol.ToLowerInvariant();
        var host = n.Address.Contains(':') ? $"[{n.Address}]" : n.Address;
        var name = Uri.EscapeDataString(n.DisplayName);
        if (protocol == "vmess")
            return "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                v = "2",
                ps = n.Name,
                add = n.Address,
                port = n.Port.ToString(),
                id = n.Uuid,
                aid = "0",
                scy = n.Encryption,
                net = n.Network,
                type = "none",
                host = n.Host,
                path = n.Path,
                tls = n.Security == "tls" ? "tls" : "",
                sni = n.Sni,
                alpn = n.Alpn,
                fp = n.Fingerprint
            })));
        if (protocol is "ss" or "shadowsocks")
            return "ss://" + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{n.Encryption}:{n.Uuid}")).TrimEnd('=') + $"@{host}:{n.Port}#{name}";
        if (protocol is not ("vless" or "trojan")) throw new NotSupportedException("Этот протокол нельзя экспортировать ссылкой.");
        var query = new Dictionary<string, string?>
        {
            ["encryption"] = n.Encryption,
            ["type"] = n.Network,
            ["security"] = n.Security,
            ["sni"] = n.Sni,
            ["fp"] = n.Fingerprint,
            ["pbk"] = n.PublicKey,
            ["sid"] = n.ShortId,
            ["spx"] = n.SpiderX,
            ["path"] = n.Path,
            ["host"] = n.Host,
            ["flow"] = n.Flow,
            ["alpn"] = n.Alpn,
            ["mode"] = n.Mode,
            ["pqv"] = n.Mldsa65Verify,
            ["xPaddingBytes"] = n.Padding,
            ["xmuxMaxConnections"] = n.XmuxMaxConnections?.ToString(),
            ["xmuxMaxRequestTimes"] = n.XmuxMaxRequestTimes,
            ["xmuxMaxReusableSecs"] = n.XmuxMaxReusableSecs,
            ["verifyPeerCertByName"] = n.VerifyPeerCertByName,
            ["pinnedPeerCertSha256"] = n.PinnedPeerCertSha256
        };
        return $"{protocol}://{Uri.EscapeDataString(n.Uuid)}@{host}:{n.Port}?" +
            string.Join('&', query.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value!))) + "#" + name;
    }

    public static string Qr(string link)
    {
        var matrix = new ZXing.MultiFormatWriter().encode(link, ZXing.BarcodeFormat.QR_CODE, 360, 360);
        var pixels = new byte[matrix.Width * matrix.Height];
        for (var y = 0; y < matrix.Height; y++) for (var x = 0; x < matrix.Width; x++) pixels[y * matrix.Width + x] = matrix[x, y] ? (byte)0 : (byte)255;
        var source = BitmapSource.Create(matrix.Width, matrix.Height, 96, 96, PixelFormats.Gray8, null, pixels, matrix.Width);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream(); encoder.Save(stream);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }
}
