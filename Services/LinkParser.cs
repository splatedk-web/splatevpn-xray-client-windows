using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public static class LinkParser
{
    private static readonly Regex ProtocolLinkRegex = new(
        @"(?:vless|vmess|trojan|ss):\/\/[^\s""'<>]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static bool IsSubscriptionUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        return (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
               Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static ServerNode? Parse(string rawLink, string subGroup = "Основное")
    {
        var trimmed = rawLink.Trim();
        try
        {
            if (trimmed.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                return ParseVless(trimmed, subGroup);
            if (trimmed.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
                return ParseTrojan(trimmed, subGroup);
            if (trimmed.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
                return ParseVmess(trimmed, subGroup);
            if (trimmed.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
                return ParseShadowsocks(trimmed, subGroup);
        }
        catch
        {
            return null;
        }
        return null;
    }

    public static List<ServerNode> ParseMultiple(string text, string subGroup = "Основное")
    {
        var result = new List<ServerNode>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var decodedText = TryDecodeBase64(text);

        var lines = decodedText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;

            var node = Parse(trimmed, subGroup);
            if (node != null)
            {
                result.Add(node);
            }
        }

        if (result.Count == 0)
        {
            var matches = ProtocolLinkRegex.Matches(decodedText);
            foreach (Match match in matches)
            {
                var node = Parse(match.Value, subGroup);
                if (node != null)
                {
                    result.Add(node);
                }
            }
        }

        if (result.Count == 0 && decodedText != text)
        {
            var matches = ProtocolLinkRegex.Matches(text);
            foreach (Match match in matches)
            {
                var node = Parse(match.Value, subGroup);
                if (node != null)
                {
                    result.Add(node);
                }
            }
        }

        return result;
    }

    public static string GenerateLink(ServerNode node)
    {
        if (node == null) return string.Empty;

        var encName = Uri.EscapeDataString(node.Name.Trim());

        switch (node.Protocol.ToLowerInvariant())
        {
            case "vless":
            {
                var sb = new StringBuilder();
                sb.Append($"vless://{node.Uuid}@{node.Address}:{node.Port}?");
                sb.Append($"type={Uri.EscapeDataString(node.Network)}");
                sb.Append($"&security={Uri.EscapeDataString(node.Security)}");
                if (!string.IsNullOrEmpty(node.Flow)) sb.Append($"&flow={Uri.EscapeDataString(node.Flow)}");
                if (!string.IsNullOrEmpty(node.Sni)) sb.Append($"&sni={Uri.EscapeDataString(node.Sni)}");
                if (!string.IsNullOrEmpty(node.Fingerprint)) sb.Append($"&fp={Uri.EscapeDataString(node.Fingerprint)}");
                if (!string.IsNullOrEmpty(node.PublicKey)) sb.Append($"&pbk={Uri.EscapeDataString(node.PublicKey)}");
                if (!string.IsNullOrEmpty(node.ShortId)) sb.Append($"&sid={Uri.EscapeDataString(node.ShortId)}");
                if (!string.IsNullOrEmpty(node.SpiderX)) sb.Append($"&spx={Uri.EscapeDataString(node.SpiderX)}");
                if (!string.IsNullOrEmpty(node.Path) && node.Path != "/") sb.Append($"&path={Uri.EscapeDataString(node.Path)}");
                if (!string.IsNullOrEmpty(node.Host)) sb.Append($"&host={Uri.EscapeDataString(node.Host)}");
                if (!string.IsNullOrEmpty(node.Mode) && node.Mode != "auto") sb.Append($"&mode={Uri.EscapeDataString(node.Mode)}");
                if (!string.IsNullOrEmpty(node.Padding)) sb.Append($"&xPaddingBytes={Uri.EscapeDataString(node.Padding)}");
                if (!string.IsNullOrEmpty(encName)) sb.Append($"#{encName}");
                return sb.ToString();
            }

            case "trojan":
            {
                var sb = new StringBuilder();
                sb.Append($"trojan://{node.Uuid}@{node.Address}:{node.Port}?");
                sb.Append($"type={Uri.EscapeDataString(node.Network)}");
                sb.Append($"&security={Uri.EscapeDataString(node.Security)}");
                if (!string.IsNullOrEmpty(node.Sni)) sb.Append($"&sni={Uri.EscapeDataString(node.Sni)}");
                if (!string.IsNullOrEmpty(node.Fingerprint)) sb.Append($"&fp={Uri.EscapeDataString(node.Fingerprint)}");
                if (!string.IsNullOrEmpty(node.PublicKey)) sb.Append($"&pbk={Uri.EscapeDataString(node.PublicKey)}");
                if (!string.IsNullOrEmpty(node.ShortId)) sb.Append($"&sid={Uri.EscapeDataString(node.ShortId)}");
                if (!string.IsNullOrEmpty(node.Path) && node.Path != "/") sb.Append($"&path={Uri.EscapeDataString(node.Path)}");
                if (!string.IsNullOrEmpty(node.Host)) sb.Append($"&host={Uri.EscapeDataString(node.Host)}");
                if (!string.IsNullOrEmpty(encName)) sb.Append($"#{encName}");
                return sb.ToString();
            }

            case "vmess":
            {
                var vmessObj = new Dictionary<string, object>
                {
                    ["v"] = "2",
                    ["ps"] = node.Name,
                    ["add"] = node.Address,
                    ["port"] = node.Port,
                    ["id"] = node.Uuid,
                    ["aid"] = 0,
                    ["scy"] = string.IsNullOrEmpty(node.Encryption) ? "auto" : node.Encryption,
                    ["net"] = node.Network,
                    ["type"] = "none",
                    ["host"] = node.Host,
                    ["path"] = node.Path,
                    ["tls"] = node.Security.Equals("tls", StringComparison.OrdinalIgnoreCase) ? "tls" : "none",
                    ["sni"] = node.Sni
                };
                var json = JsonSerializer.Serialize(vmessObj);
                var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                return $"vmess://{b64}";
            }

            case "ss" or "shadowsocks":
            {
                var userinfo = $"{node.Encryption}:{node.Uuid}";
                var b64Userinfo = Convert.ToBase64String(Encoding.UTF8.GetBytes(userinfo));
                return $"ss://{b64Userinfo}@{node.Address}:{node.Port}#{encName}";
            }

            default:
                return node.RawLink;
        }
    }

    private static string TryDecodeBase64(string input)
    {
        var trimmed = input.Trim().Replace("\r", "").Replace("\n", "").Replace(" ", "");
        if (trimmed.StartsWith("vless://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
        {
            return input;
        }

        try
        {
            var cleaned = trimmed.Replace('-', '+').Replace('_', '/');
            var pad = (4 - cleaned.Length % 4) % 4;
            if (pad > 0)
            {
                cleaned = cleaned.PadRight(cleaned.Length + pad, '=');
            }
            var bytes = Convert.FromBase64String(cleaned);
            var decoded = Encoding.UTF8.GetString(bytes);
            if (decoded.Contains("://"))
            {
                return decoded;
            }
        }
        catch { }

        return input;
    }

    private static ServerNode ParseVless(string raw, string subGroup)
    {
        var uri = new ParsedUri(raw);
        var uuid = uri.UserInfo ?? "";
        var host = uri.Host ?? "";
        var port = uri.Port > 0 ? uri.Port : 443;
        var name = !string.IsNullOrEmpty(uri.Fragment)
            ? SafeUrlDecode(uri.Fragment)
            : $"{host}:{port}";

        var query = uri.QueryParams;
        query.TryGetValue("flow", out var flow);
        query.TryGetValue("encryption", out var encryption);
        
        var rawNet = uri.GetFirstQueryParam("type", "net", "transport");
        var network = NormalizeNetwork(rawNet);

        var pbk = uri.GetFirstQueryParam("pbk", "publicKey") ?? "";
        var sid = uri.GetFirstQueryParam("sid", "shortId") ?? "";
        var spx = uri.GetFirstQueryParam("spx", "spiderX") ?? "";

        var rawSec = uri.GetFirstQueryParam("security", "sec");
        var security = !string.IsNullOrEmpty(rawSec)
            ? rawSec.ToLowerInvariant().Trim()
            : !string.IsNullOrEmpty(pbk) ? "reality" : "tls";

        var sni = uri.GetFirstQueryParam("sni", "peer", "serverName") ?? host;
        var fp = uri.GetFirstQueryParam("fp", "fingerprint") ?? "chrome";

        var pathParam = uri.GetFirstQueryParam("path", "serviceName");
        var finalPath = !string.IsNullOrEmpty(pathParam)
            ? (pathParam.StartsWith('/') ? pathParam : "/" + pathParam)
            : (!string.IsNullOrEmpty(uri.Path) && uri.Path != "/" ? uri.Path : "/");

        var httpHost = uri.GetFirstQueryParam("host") ?? "";
        var mode = uri.GetFirstQueryParam("mode") ?? "auto";
        var padding = uri.GetFirstQueryParam("xPaddingBytes", "x_padding_bytes", "padding") ?? "100-1000";
        var alpn = uri.GetFirstQueryParam("alpn") ?? "";

        // Check 3x-ui extra json
        if (query.TryGetValue("extra", out var extraStr) && !string.IsNullOrEmpty(extraStr))
        {
            try
            {
                var decodedExtra = SafeUrlDecode(extraStr);
                using var doc = JsonDocument.Parse(decodedExtra);
                if (doc.RootElement.TryGetProperty("mode", out var m)) mode = m.GetString() ?? mode;
                if (doc.RootElement.TryGetProperty("xPaddingBytes", out var p)) padding = p.GetString() ?? padding;
            }
            catch { }
        }

        return new ServerNode
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Protocol = "vless",
            Address = host,
            Port = port,
            Uuid = uuid,
            Flow = flow ?? "",
            Encryption = string.IsNullOrEmpty(encryption) ? "none" : encryption,
            Network = network,
            Security = security,
            Sni = sni,
            Fingerprint = fp,
            PublicKey = pbk,
            ShortId = sid,
            SpiderX = spx,
            Path = finalPath,
            Host = httpHost,
            Mode = mode,
            Padding = padding,
            Alpn = alpn,
            RawLink = raw,
            SubscriptionGroup = subGroup
        };
    }

    private static ServerNode ParseTrojan(string raw, string subGroup)
    {
        var uri = new ParsedUri(raw);
        var password = uri.UserInfo ?? "";
        var host = uri.Host ?? "";
        var port = uri.Port > 0 ? uri.Port : 443;
        var name = !string.IsNullOrEmpty(uri.Fragment)
            ? SafeUrlDecode(uri.Fragment)
            : $"Trojan-{host}";

        var rawNet = uri.GetFirstQueryParam("type", "net", "transport");
        var network = NormalizeNetwork(rawNet);

        var pbk = uri.GetFirstQueryParam("pbk", "publicKey") ?? "";
        var sid = uri.GetFirstQueryParam("sid", "shortId") ?? "";
        var spx = uri.GetFirstQueryParam("spx", "spiderX") ?? "";

        var rawSec = uri.GetFirstQueryParam("security", "sec");
        var security = !string.IsNullOrEmpty(rawSec)
            ? rawSec.ToLowerInvariant().Trim()
            : !string.IsNullOrEmpty(pbk) ? "reality" : "tls";

        var sni = uri.GetFirstQueryParam("sni", "peer", "serverName") ?? host;
        var fp = uri.GetFirstQueryParam("fp", "fingerprint") ?? "chrome";

        var pathParam = uri.GetFirstQueryParam("path", "serviceName");
        var finalPath = !string.IsNullOrEmpty(pathParam)
            ? (pathParam.StartsWith('/') ? pathParam : "/" + pathParam)
            : (!string.IsNullOrEmpty(uri.Path) && uri.Path != "/" ? uri.Path : "/");

        var httpHost = uri.GetFirstQueryParam("host") ?? "";
        var alpn = uri.GetFirstQueryParam("alpn") ?? "";

        return new ServerNode
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Protocol = "trojan",
            Address = host,
            Port = port,
            Uuid = password,
            Network = network,
            Security = security,
            Sni = sni,
            Fingerprint = fp,
            PublicKey = pbk,
            ShortId = sid,
            SpiderX = spx,
            Path = finalPath,
            Host = httpHost,
            Alpn = alpn,
            RawLink = raw,
            SubscriptionGroup = subGroup
        };
    }

    private static ServerNode? ParseVmess(string raw, string subGroup)
    {
        var fragment = raw.Contains('#') ? SafeUrlDecode(raw.Substring(raw.IndexOf('#') + 1)) : null;
        var base64Part = raw.Substring(8);
        if (base64Part.Contains('#')) base64Part = base64Part.Substring(0, base64Part.IndexOf('#'));
        if (base64Part.Contains('?')) base64Part = base64Part.Substring(0, base64Part.IndexOf('?'));

        var json = TryDecodeBase64(base64Part.Trim());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var host = root.TryGetProperty("add", out var add) ? add.GetString() : null;
        if (string.IsNullOrEmpty(host)) return null;

        var port = 443;
        if (root.TryGetProperty("port", out var pEl))
        {
            if (pEl.ValueKind == JsonValueKind.Number) port = pEl.GetInt32();
            else if (pEl.ValueKind == JsonValueKind.String && int.TryParse(pEl.GetString(), out var pVal)) port = pVal;
        }

        var uuid = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        var name = root.TryGetProperty("ps", out var psEl) ? psEl.GetString() : fragment;
        if (string.IsNullOrEmpty(name)) name = $"VMess-{host}";

        var rawNet = root.TryGetProperty("net", out var nEl) ? nEl.GetString() : null;
        var network = NormalizeNetwork(rawNet);

        var tls = root.TryGetProperty("tls", out var tlsEl) ? tlsEl.GetString() : "none";
        var security = tls?.Equals("tls", StringComparison.OrdinalIgnoreCase) == true ? "tls" : "none";

        var httpHost = root.TryGetProperty("host", out var hEl) ? hEl.GetString() ?? "" : "";
        var sni = root.TryGetProperty("sni", out var sniEl) ? sniEl.GetString() : (!string.IsNullOrEmpty(httpHost) ? httpHost : host);
        var rawPath = root.TryGetProperty("path", out var pathEl) ? pathEl.GetString() ?? "/" : "/";
        var path = rawPath.StartsWith('/') ? rawPath : "/" + rawPath;
        var scy = root.TryGetProperty("scy", out var scyEl) ? scyEl.GetString() ?? "auto" : "auto";
        var fp = root.TryGetProperty("fp", out var fpEl) ? fpEl.GetString() ?? "chrome" : "chrome";

        return new ServerNode
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Protocol = "vmess",
            Address = host,
            Port = port,
            Uuid = uuid,
            Encryption = scy,
            Network = network,
            Security = security,
            Sni = sni ?? host,
            Host = httpHost,
            Fingerprint = fp,
            Path = path,
            RawLink = raw,
            SubscriptionGroup = subGroup
        };
    }

    private static ServerNode? ParseShadowsocks(string raw, string subGroup)
    {
        var withoutScheme = raw.Substring(5);
        string? fragment = null;
        if (withoutScheme.Contains('#'))
        {
            fragment = SafeUrlDecode(withoutScheme.Substring(withoutScheme.IndexOf('#') + 1));
            withoutScheme = withoutScheme.Substring(0, withoutScheme.IndexOf('#'));
        }

        if (withoutScheme.Contains('?'))
        {
            withoutScheme = withoutScheme.Substring(0, withoutScheme.IndexOf('?'));
        }

        string method = "aes-256-gcm";
        string password = "";
        string host = "";
        int port = 8388;

        if (withoutScheme.Contains('@'))
        {
            var userInfoPart = withoutScheme.Substring(0, withoutScheme.LastIndexOf('@'));
            var hostPort = withoutScheme.Substring(withoutScheme.LastIndexOf('@') + 1);

            var decodedUserInfo = userInfoPart.Contains(':') ? userInfoPart : TryDecodeBase64(userInfoPart);
            if (decodedUserInfo.Contains(':'))
            {
                method = decodedUserInfo.Substring(0, decodedUserInfo.IndexOf(':'));
                password = decodedUserInfo.Substring(decodedUserInfo.IndexOf(':') + 1);
            }

            ParseHostPort(hostPort, out host, out port);
        }
        else
        {
            var decoded = TryDecodeBase64(withoutScheme);
            if (decoded.Contains('@'))
            {
                var userInfoPart = decoded.Substring(0, decoded.LastIndexOf('@'));
                var hostPort = decoded.Substring(decoded.LastIndexOf('@') + 1);

                if (userInfoPart.Contains(':'))
                {
                    method = userInfoPart.Substring(0, userInfoPart.IndexOf(':'));
                    password = userInfoPart.Substring(userInfoPart.IndexOf(':') + 1);
                }

                ParseHostPort(hostPort, out host, out port);
            }
        }

        if (string.IsNullOrEmpty(host)) return null;

        return new ServerNode
        {
            Id = Guid.NewGuid().ToString(),
            Name = fragment ?? $"Shadowsocks-{host}:{port}",
            Protocol = "shadowsocks",
            Address = host,
            Port = port,
            Uuid = password,
            Encryption = method,
            Network = "tcp",
            Security = "none",
            RawLink = raw,
            SubscriptionGroup = subGroup
        };
    }

    private static void ParseHostPort(string hostPort, out string host, out int port)
    {
        host = "";
        port = 8388;
        if (hostPort.StartsWith('['))
        {
            var close = hostPort.IndexOf(']');
            if (close > 0)
            {
                host = hostPort.Substring(1, close - 1);
                var colon = hostPort.IndexOf(':', close);
                if (colon > 0 && int.TryParse(hostPort.Substring(colon + 1), out var p))
                {
                    port = p;
                }
            }
        }
        else
        {
            var colon = hostPort.LastIndexOf(':');
            if (colon > 0)
            {
                host = hostPort.Substring(0, colon);
                if (int.TryParse(hostPort.Substring(colon + 1), out var p))
                {
                    port = p;
                }
            }
            else
            {
                host = hostPort;
            }
        }
    }

    private static string NormalizeNetwork(string? rawNet)
    {
        if (string.IsNullOrWhiteSpace(rawNet)) return "tcp";
        var net = rawNet.Trim().ToLowerInvariant();
        return net switch
        {
            "splithttp" or "xhttp" => "xhttp",
            "websocket" or "ws" => "ws",
            "grpc" or "gun" => "grpc",
            "h2" or "http" => "h2",
            _ => "tcp"
        };
    }

    private static string SafeUrlDecode(string str)
    {
        try
        {
            return WebUtility.UrlDecode(str);
        }
        catch
        {
            return str;
        }
    }

    internal class ParsedUri
    {
        public string Scheme { get; } = "";
        public string? UserInfo { get; }
        public string? Host { get; }
        public int Port { get; } = -1;
        public string? Path { get; }
        public string? Fragment { get; }
        public Dictionary<string, string> QueryParams { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ParsedUri(string raw)
        {
            var hashIdx = raw.IndexOf('#');
            string withoutFrag;
            if (hashIdx >= 0)
            {
                Fragment = raw.Substring(hashIdx + 1);
                withoutFrag = raw.Substring(0, hashIdx);
            }
            else
            {
                Fragment = null;
                withoutFrag = raw;
            }

            var qIdx = withoutFrag.IndexOf('?');
            string beforeQuery;
            if (qIdx >= 0)
            {
                var qStr = withoutFrag.Substring(qIdx + 1);
                var pairs = qStr.Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var pair in pairs)
                {
                    var eq = pair.IndexOf('=');
                    if (eq > 0)
                    {
                        var key = pair.Substring(0, eq).Trim();
                        var val = SafeUrlDecode(pair.Substring(eq + 1));
                        QueryParams[key] = val;
                    }
                }
                beforeQuery = withoutFrag.Substring(0, qIdx);
            }
            else
            {
                beforeQuery = withoutFrag;
            }

            var schemeEnd = beforeQuery.IndexOf("://", StringComparison.Ordinal);
            string authorityAndPath;
            if (schemeEnd >= 0)
            {
                Scheme = beforeQuery.Substring(0, schemeEnd).ToLowerInvariant();
                authorityAndPath = beforeQuery.Substring(schemeEnd + 3);
            }
            else
            {
                authorityAndPath = beforeQuery;
            }

            var slashIdx = authorityAndPath.IndexOf('/');
            string authority;
            if (slashIdx >= 0)
            {
                Path = authorityAndPath.Substring(slashIdx);
                authority = authorityAndPath.Substring(0, slashIdx);
            }
            else
            {
                Path = "/";
                authority = authorityAndPath;
            }

            var atIdx = authority.LastIndexOf('@');
            string hostPort;
            if (atIdx >= 0)
            {
                UserInfo = authority.Substring(0, atIdx);
                hostPort = authority.Substring(atIdx + 1);
            }
            else
            {
                UserInfo = null;
                hostPort = authority;
            }

            if (hostPort.StartsWith('['))
            {
                var closeBracket = hostPort.IndexOf(']');
                if (closeBracket >= 0)
                {
                    Host = hostPort.Substring(1, closeBracket - 1);
                    var colonIdx = hostPort.IndexOf(':', closeBracket);
                    if (colonIdx >= 0 && int.TryParse(hostPort.Substring(colonIdx + 1), out var p))
                    {
                        Port = p;
                    }
                }
                else
                {
                    Host = hostPort;
                }
            }
            else
            {
                var colonIdx = hostPort.LastIndexOf(':');
                if (colonIdx >= 0)
                {
                    Host = hostPort.Substring(0, colonIdx);
                    if (int.TryParse(hostPort.Substring(colonIdx + 1), out var p))
                    {
                        Port = p;
                    }
                }
                else
                {
                    Host = hostPort;
                }
            }
        }

        public string? GetFirstQueryParam(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (QueryParams.TryGetValue(k, out var val) && !string.IsNullOrEmpty(val))
                    return val;
            }
            return null;
        }
    }
}