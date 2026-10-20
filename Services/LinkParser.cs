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
        var trimmed = UnwrapImportLink(text);
        return (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
               Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static string UnwrapImportLink(string text)
    {
        var value = text.Trim();
        foreach (var prefix in new[] { "happ://add/", "incy://import/", "incy://add/" })
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..].Trim();
                if (Regex.IsMatch(value, @"^https?%3a%2f%2f", RegexOptions.IgnoreCase)) value = Uri.UnescapeDataString(value);
                break;
            }
        return value;
    }

    public static bool SameEndpoint(ServerNode a, ServerNode b) =>
        a.Protocol.Equals(b.Protocol, StringComparison.OrdinalIgnoreCase) && a.Address.Equals(b.Address, StringComparison.OrdinalIgnoreCase) &&
        a.Port == b.Port && a.Uuid == b.Uuid && a.Encryption == b.Encryption && a.Network == b.Network && a.Security == b.Security &&
        a.Sni == b.Sni && a.PublicKey == b.PublicKey && a.ShortId == b.ShortId && a.Path == b.Path && a.Host == b.Host && a.Flow == b.Flow;

    public static ServerNode? Parse(string rawLink, string subGroup = "Основное")
    {
        var trimmed = rawLink.Trim().Replace(@"\u0026", "&").Replace("&amp;", "&");
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

        // 1. Direct line-by-line protocol link parsing
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

        // 2. Regex fallback for protocol links within raw or decoded text
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

        // 3. Clash / Mihomo YAML Support (proxies:)
        if (result.Count == 0)
        {
            var yamlNodes = ParseClashYaml(decodedText, subGroup);
            if (yamlNodes.Count == 0 && decodedText != text)
            {
                yamlNodes = ParseClashYaml(text, subGroup);
            }
            if (yamlNodes.Count > 0)
            {
                result.AddRange(yamlNodes);
            }
        }

        // 4. Sing-box / Xray JSON Support (outbounds: [ ... ])
        if (result.Count == 0)
        {
            var jsonNodes = ParseSingboxJson(decodedText, subGroup);
            if (jsonNodes.Count == 0 && decodedText != text)
            {
                jsonNodes = ParseSingboxJson(text, subGroup);
            }
            if (jsonNodes.Count > 0)
            {
                result.AddRange(jsonNodes);
            }
        }

        return result;
    }

    public static string GenerateLink(ServerNode node) => ServerShare.Link(node, preserveRaw: false);

    public static string TryDecodeBase64(string input)
    {
        var trimmed = input.Trim().Trim('\uFEFF', '\u200B').Replace("\r", "").Replace("\n", "").Replace(" ", "");
        if (trimmed.StartsWith("vless://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("ss://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("proxies:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("{\"outbounds\"", StringComparison.OrdinalIgnoreCase))
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
            if (decoded.Contains("://") ||
                decoded.Contains("proxies:") ||
                decoded.Contains("outbounds") ||
                decoded.Contains("\"protocol\"") ||
                decoded.Contains("\"server\""))
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
            ? CleanNodeName(uri.Fragment)
            : $"{host}:{port}";

        var query = uri.QueryParams;
        query.TryGetValue("flow", out var flow);
        query.TryGetValue("encryption", out var encryption);

        var rawNet = uri.GetFirstQueryParam("type", "net", "transport");
        var network = NormalizeNetwork(rawNet);

        var pbk = uri.GetFirstQueryParam("pbk", "publicKey") ?? "";
        var sid = uri.GetFirstQueryParam("sid", "shortId") ?? "";
        var spx = uri.GetFirstQueryParam("spx", "spiderX") ?? "";
        var pqv = uri.GetFirstQueryParam("pqv", "postQuantum", "mldsa65Verify") ?? "";

        var rawSec = uri.GetFirstQueryParam("security", "sec");
        var security = !string.IsNullOrEmpty(rawSec)
            ? rawSec.ToLowerInvariant().Trim()
            : !string.IsNullOrEmpty(pbk) ? "reality" : "tls";

        var sni = uri.GetFirstQueryParam("sni", "peer", "serverName") ?? host;
        var fp = uri.GetFirstQueryParam("fp", "fingerprint") ?? "chrome";

        var certName = uri.GetFirstQueryParam("verifyPeerCertByName", "certName", "peerCertName");
        var pinSha = uri.GetFirstQueryParam("pinnedPeerCertSha256", "pinSha256", "pin");

        var pathParam = uri.GetFirstQueryParam("path", "serviceName");
        var finalPath = !string.IsNullOrEmpty(pathParam)
            ? (pathParam.StartsWith('/') ? pathParam : "/" + pathParam)
            : (!string.IsNullOrEmpty(uri.Path) && uri.Path != "/" ? uri.Path : "/");

        var httpHost = uri.GetFirstQueryParam("host") ?? "";
        var mode = uri.GetFirstQueryParam("mode") ?? "auto";
        var padding = uri.GetFirstQueryParam("xPaddingBytes", "x_padding_bytes", "padding") ?? "100-1000";
        var alpn = uri.GetFirstQueryParam("alpn") ?? "";

        var xmuxConnStr = uri.GetFirstQueryParam("xmuxMaxConnections", "maxConnections", "xmux");
        int? xmuxMaxConn = int.TryParse(xmuxConnStr, out var parsedXmux) ? parsedXmux : null;
        var xmuxReqTimes = uri.GetFirstQueryParam("xmuxMaxRequestTimes", "hMaxRequestTimes");
        var xmuxReusableSecs = uri.GetFirstQueryParam("xmuxMaxReusableSecs", "hMaxReusableSecs");

        // Check 3x-ui / modern extra json
        if (query.TryGetValue("extra", out var extraStr) && !string.IsNullOrEmpty(extraStr))
        {
            try
            {
                var decodedExtra = SafeUrlDecode(extraStr);
                using var doc = JsonDocument.Parse(decodedExtra);
                if (doc.RootElement.TryGetProperty("mode", out var m)) mode = m.GetString() ?? mode;
                if (doc.RootElement.TryGetProperty("xPaddingBytes", out var p)) padding = p.GetString() ?? padding;
                if (doc.RootElement.TryGetProperty("pqv", out var pq)) pqv = pq.GetString() ?? pqv;
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
            Mldsa65Verify = pqv,
            VerifyPeerCertByName = certName,
            PinnedPeerCertSha256 = pinSha,
            XmuxMaxConnections = xmuxMaxConn,
            XmuxMaxRequestTimes = xmuxReqTimes,
            XmuxMaxReusableSecs = xmuxReusableSecs,
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
            ? CleanNodeName(uri.Fragment)
            : $"Trojan-{host}";

        var rawNet = uri.GetFirstQueryParam("type", "net", "transport");
        var network = NormalizeNetwork(rawNet);

        var pbk = uri.GetFirstQueryParam("pbk", "publicKey") ?? "";
        var sid = uri.GetFirstQueryParam("sid", "shortId") ?? "";
        var spx = uri.GetFirstQueryParam("spx", "spiderX") ?? "";
        var pqv = uri.GetFirstQueryParam("pqv", "postQuantum", "mldsa65Verify") ?? "";

        var rawSec = uri.GetFirstQueryParam("security", "sec");
        var security = !string.IsNullOrEmpty(rawSec)
            ? rawSec.ToLowerInvariant().Trim()
            : !string.IsNullOrEmpty(pbk) ? "reality" : "tls";

        var sni = uri.GetFirstQueryParam("sni", "peer", "serverName") ?? host;
        var fp = uri.GetFirstQueryParam("fp", "fingerprint") ?? "chrome";

        var certName = uri.GetFirstQueryParam("verifyPeerCertByName", "certName", "peerCertName");
        var pinSha = uri.GetFirstQueryParam("pinnedPeerCertSha256", "pinSha256", "pin");

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
            Mldsa65Verify = pqv,
            VerifyPeerCertByName = certName,
            PinnedPeerCertSha256 = pinSha,
            Path = finalPath,
            Host = httpHost,
            Alpn = alpn,
            RawLink = raw,
            SubscriptionGroup = subGroup
        };
    }

    private static ServerNode? ParseVmess(string raw, string subGroup)
    {
        var fragment = raw.Contains('#') ? CleanNodeName(raw.Substring(raw.IndexOf('#') + 1)) : null;
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
        var alpn = root.TryGetProperty("alpn", out var alpnEl) ? alpnEl.GetString() ?? "" : "";

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
            Alpn = alpn,
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
            fragment = CleanNodeName(withoutScheme.Substring(withoutScheme.IndexOf('#') + 1));
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

    // ============================================================
    // Clash / Mihomo YAML Parser
    // ============================================================

    public static List<ServerNode> ParseClashYaml(string yamlText, string subGroup = "Основное")
    {
        var result = new List<ServerNode>();
        if (string.IsNullOrWhiteSpace(yamlText)) return result;

        var lines = yamlText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        bool inProxies = false;
        var currentBlock = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("proxies:", StringComparison.OrdinalIgnoreCase))
            {
                inProxies = true;
                continue;
            }
            if (inProxies && (trimmed.StartsWith("proxy-groups:", StringComparison.OrdinalIgnoreCase) ||
                              trimmed.StartsWith("rules:", StringComparison.OrdinalIgnoreCase) ||
                              trimmed.StartsWith("rule-providers:", StringComparison.OrdinalIgnoreCase)))
            {
                if (currentBlock.Count > 0)
                {
                    var node = ParseClashProxyBlock(currentBlock, subGroup);
                    if (node != null) result.Add(node);
                    currentBlock.Clear();
                }
                break;
            }

            if (inProxies)
            {
                if (line.StartsWith("  - ") || line.StartsWith("- "))
                {
                    if (currentBlock.Count > 0)
                    {
                        var node = ParseClashProxyBlock(currentBlock, subGroup);
                        if (node != null) result.Add(node);
                        currentBlock.Clear();
                    }
                    currentBlock.Add(line);
                }
                else if (currentBlock.Count > 0)
                {
                    currentBlock.Add(line);
                }
            }
        }

        if (currentBlock.Count > 0)
        {
            var node = ParseClashProxyBlock(currentBlock, subGroup);
            if (node != null) result.Add(node);
        }

        return result;
    }

    private static ServerNode? ParseClashProxyBlock(List<string> lines, string subGroup)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string currentParent = "";

        foreach (var rawLine in lines)
        {
            var line = rawLine;
            if (line.StartsWith("  - ")) line = line.Substring(4);
            else if (line.StartsWith("- ")) line = line.Substring(2);

            var indent = rawLine.Length - rawLine.TrimStart().Length;
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx > 0)
            {
                var key = trimmed.Substring(0, colonIdx).Trim().Trim('\'', '\"');
                var val = trimmed.Substring(colonIdx + 1).Trim().Trim('\'', '\"');

                if (string.IsNullOrEmpty(val) && (key.EndsWith("-opts") || key == "headers"))
                {
                    currentParent = key;
                }
                else
                {
                    var fullKey = indent > 4 && !string.IsNullOrEmpty(currentParent)
                        ? $"{currentParent}.{key}"
                        : key;
                    dict[fullKey] = val;
                }
            }
        }

        dict.TryGetValue("type", out var type);
        dict.TryGetValue("server", out var server);
        dict.TryGetValue("port", out var portStr);
        dict.TryGetValue("name", out var name);

        if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(server) || !int.TryParse(portStr, out var port))
            return null;

        name = !string.IsNullOrEmpty(name) ? CleanNodeName(name) : $"{server}:{port}";
        type = type.ToLowerInvariant();

        dict.TryGetValue("uuid", out var uuid);
        if (string.IsNullOrEmpty(uuid)) dict.TryGetValue("password", out uuid);

        dict.TryGetValue("network", out var net);
        var network = NormalizeNetwork(net);

        dict.TryGetValue("tls", out var tlsStr);
        bool isTls = bool.TryParse(tlsStr, out var b) && b;

        dict.TryGetValue("servername", out var sni);
        if (string.IsNullOrEmpty(sni)) dict.TryGetValue("sni", out sni);

        dict.TryGetValue("flow", out var flow);
        dict.TryGetValue("client-fingerprint", out var fp);
        if (string.IsNullOrEmpty(fp)) dict.TryGetValue("fingerprint", out fp);

        dict.TryGetValue("reality-opts.public-key", out var pbk);
        dict.TryGetValue("reality-opts.short-id", out var sid);
        dict.TryGetValue("reality-opts.spider-x", out var spx);
        dict.TryGetValue("reality-opts.pqv", out var pqv);
        if (string.IsNullOrEmpty(pqv)) dict.TryGetValue("reality-opts.mldsa65-verify", out pqv);

        var security = "none";
        if (!string.IsNullOrEmpty(pbk)) security = "reality";
        else if (isTls) security = "tls";

        dict.TryGetValue("ws-opts.path", out var wsPath);
        dict.TryGetValue("grpc-opts.grpc-service-name", out var grpcName);
        dict.TryGetValue("xhttp-opts.path", out var xhttpPath);
        var finalPath = !string.IsNullOrEmpty(xhttpPath) ? xhttpPath : (!string.IsNullOrEmpty(wsPath) ? wsPath : (!string.IsNullOrEmpty(grpcName) ? grpcName : "/"));

        dict.TryGetValue("ws-opts.headers.Host", out var wsHost);
        dict.TryGetValue("xhttp-opts.headers.Host", out var xhttpHost);
        var httpHost = !string.IsNullOrEmpty(xhttpHost) ? xhttpHost : (!string.IsNullOrEmpty(wsHost) ? wsHost : "");

        dict.TryGetValue("xhttp-opts.mode", out var xhttpMode);
        dict.TryGetValue("cipher", out var cipher);

        var node = new ServerNode
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Protocol = type switch
            {
                "vless" => "vless",
                "vmess" => "vmess",
                "trojan" => "trojan",
                "ss" => "shadowsocks",
                _ => type
            },
            Address = server,
            Port = port,
            Uuid = uuid ?? "",
            Network = network,
            Security = security,
            Sni = sni ?? server,
            Flow = flow ?? "",
            Fingerprint = !string.IsNullOrEmpty(fp) ? fp : "chrome",
            PublicKey = pbk ?? "",
            ShortId = sid ?? "",
            SpiderX = spx ?? "",
            Mldsa65Verify = pqv ?? "",
            Path = finalPath,
            Host = httpHost,
            Mode = !string.IsNullOrEmpty(xhttpMode) ? xhttpMode : "auto",
            Encryption = !string.IsNullOrEmpty(cipher) ? cipher : "none",
            SubscriptionGroup = subGroup
        };

        node.RawLink = GenerateLink(node);
        return node;
    }

    // ============================================================
    // Sing-box / Xray JSON Parser
    // ============================================================

    public static List<ServerNode> ParseSingboxJson(string jsonText, string subGroup = "Основное")
    {
        var result = new List<ServerNode>();
        if (string.IsNullOrWhiteSpace(jsonText)) return result;

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            JsonElement outboundsEl;

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                outboundsEl = doc.RootElement;
            }
            else if (doc.RootElement.TryGetProperty("outbounds", out var outProp) && outProp.ValueKind == JsonValueKind.Array)
            {
                outboundsEl = outProp;
            }
            else
            {
                return result;
            }

            foreach (var el in outboundsEl.EnumerateArray())
            {
                var node = ParseSingleJsonOutbound(el, subGroup);
                if (node != null) result.Add(node);
            }
        }
        catch { }

        return result;
    }

    private static ServerNode? ParseSingleJsonOutbound(JsonElement el, string subGroup)
    {
        try
        {
            string? type = null;
            if (el.TryGetProperty("type", out var t)) type = t.GetString();
            else if (el.TryGetProperty("protocol", out var p)) type = p.GetString();

            if (string.IsNullOrEmpty(type)) return null;
            type = type.ToLowerInvariant();

            if (type != "vless" && type != "vmess" && type != "trojan" && type != "shadowsocks" && type != "ss")
                return null;

            string? tag = null;
            if (el.TryGetProperty("tag", out var tg)) tag = tg.GetString();
            else if (el.TryGetProperty("name", out var nm)) tag = nm.GetString();

            string? server = null;
            if (el.TryGetProperty("server", out var srv)) server = srv.GetString();
            else if (el.TryGetProperty("address", out var addr)) server = addr.GetString();

            int port = 443;
            if (el.TryGetProperty("server_port", out var sp)) port = sp.GetInt32();
            else if (el.TryGetProperty("port", out var pt))
            {
                if (pt.ValueKind == JsonValueKind.Number) port = pt.GetInt32();
                else if (pt.ValueKind == JsonValueKind.String && int.TryParse(pt.GetString(), out var parsedPt)) port = parsedPt;
            }

            if (string.IsNullOrEmpty(server)) return null;

            string uuid = "";
            string flow = "";
            if (el.TryGetProperty("uuid", out var uid)) uuid = uid.GetString() ?? "";
            else if (el.TryGetProperty("password", out var pwd)) uuid = pwd.GetString() ?? "";

            if (el.TryGetProperty("flow", out var fl)) flow = fl.GetString() ?? "";

            // TLS / Reality
            string security = "none";
            string sni = server;
            string pbk = "";
            string sid = "";
            string fp = "chrome";
            string pqv = "";

            if (el.TryGetProperty("tls", out var tlsEl))
            {
                bool enabled = true;
                if (tlsEl.TryGetProperty("enabled", out var en)) enabled = en.GetBoolean();
                if (enabled) security = "tls";

                if (tlsEl.TryGetProperty("server_name", out var sn)) sni = sn.GetString() ?? sni;
                else if (tlsEl.TryGetProperty("sni", out var sn2)) sni = sn2.GetString() ?? sni;

                if (tlsEl.TryGetProperty("utls", out var utlsEl) && utlsEl.TryGetProperty("fingerprint", out var fpEl))
                {
                    fp = fpEl.GetString() ?? fp;
                }

                if (tlsEl.TryGetProperty("reality", out var rEl))
                {
                    bool rEnabled = true;
                    if (rEl.TryGetProperty("enabled", out var ren)) rEnabled = ren.GetBoolean();
                    if (rEnabled)
                    {
                        security = "reality";
                        if (rEl.TryGetProperty("public_key", out var pk)) pbk = pk.GetString() ?? "";
                        if (rEl.TryGetProperty("short_id", out var si)) sid = si.GetString() ?? "";
                        if (rEl.TryGetProperty("pqv", out var pq)) pqv = pq.GetString() ?? "";
                    }
                }
            }

            // Transport
            string network = "tcp";
            string path = "/";
            string host = "";
            string mode = "auto";

            if (el.TryGetProperty("transport", out var trEl))
            {
                if (trEl.TryGetProperty("type", out var trType))
                    network = NormalizeNetwork(trType.GetString());

                if (trEl.TryGetProperty("path", out var pth)) path = pth.GetString() ?? path;
                if (trEl.TryGetProperty("headers", out var hdrs) && hdrs.TryGetProperty("Host", out var hst))
                    host = hst.GetString() ?? host;
                if (trEl.TryGetProperty("mode", out var md)) mode = md.GetString() ?? mode;
            }

            var node = new ServerNode
            {
                Id = Guid.NewGuid().ToString(),
                Name = !string.IsNullOrEmpty(tag) ? CleanNodeName(tag) : $"{server}:{port}",
                Protocol = type == "ss" ? "shadowsocks" : type,
                Address = server,
                Port = port,
                Uuid = uuid,
                Network = network,
                Security = security,
                Sni = sni,
                Flow = flow,
                Fingerprint = fp,
                PublicKey = pbk,
                ShortId = sid,
                Mldsa65Verify = pqv,
                Path = path,
                Host = host,
                Mode = mode,
                SubscriptionGroup = subGroup
            };

            node.RawLink = GenerateLink(node);
            return node;
        }
        catch
        {
            return null;
        }
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

    private static string CleanNodeName(string raw)
    {
        var decoded = SafeUrlDecode(raw.Replace('+', ' ')).Trim();
        // Remove trailing anti-censorship / TSPU padding comment hashes if present
        if (decoded.Contains("# anti-tspu-salt"))
        {
            decoded = decoded.Replace("# anti-tspu-salt", "").Trim();
        }
        return decoded;
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
            raw = raw.Replace(@"\u0026", "&").Replace("&amp;", "&");
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
                var qStr = withoutFrag.Substring(qIdx + 1).Replace(@"\u0026", "&").Replace("&amp;", "&");
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
                UserInfo = Uri.UnescapeDataString(authority.Substring(0, atIdx));
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
