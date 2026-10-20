using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public class XrayConfigGenerator
{
    private static readonly string[] XboxAiDomains =
    {
        "domain:openai.com", "domain:chatgpt.com", "domain:oaistatic.com", "domain:oaiusercontent.com",
        "domain:sora.com", "domain:ai.com", "geosite:openai",
        "domain:gemini.google.com", "domain:aistudio.google.com", "domain:ai.google.dev",
        "domain:generativelanguage.googleapis.com", "domain:gemini.googleapis.com",
        "domain:proactivebackend-pa.googleapis.com", "domain:alkalimakersuite-pa.googleapis.com",
        "domain:makersuite.google.com", "domain:bard.google.com"
    };
    public string GenerateConfig(
        ServerNode? node,
        string mode = "smart_ru",
        bool enableSniffing = true,
        bool routeOnlySniffing = true,
        bool blockQuic = true,
        bool blockIpv6 = true,
        string dnsProvider = "cloudflare",
        bool isFakeDns = false,
        bool enableTlsFragment = false,
        string xhttpModeOverride = "auto",
        int socksPort = 10808,
        int httpPort = 10809,
        int bufferSizeKb = 4096,
        string tcpCongestion = "bbr",
        bool enableAiBypass = true,
        string? customProxyRules = null,
        string? customDirectRules = null,
        bool enableTun = false,
        string? perAppProxyList = null,
        string? perAppDirectList = null,
        string? uplinkInterface = null,
        int metricsPort = 0,
        bool enableAiXboxDns = false,
        bool enableAiDns = false,
        string aiRoutingMode = "dns_ai",
        string customDohUrl = "",
        string? customBlockRules = null,
        bool enableAdBlock = false,
        bool enableMux = false,
        int muxConcurrency = 8,
        string tlsFragmentLength = "100-200",
        string tlsFragmentInterval = "10-20",
        string tlsFragmentPackets = "tlshello",
        bool shareLan = false,
        string aiDnsProvider = "dns_ai",
        bool aiTlsFragment = false,
        string? adBlockMatcher = null,
        string adBlockAllowlist = "")
    {
        if (socksPort == httpPort || socksPort is < 1 or > 65535 || httpPort is < 1 or > 65535)
            throw new ArgumentException("HTTP и SOCKS: укажите разные порты от 1 до 65535.");
        // Old desktop profiles may contain Android-only options. Keep the stored lists,
        // but never emit unsupported process rules or an unusable FakeDNS configuration.
        var modernAiDns = enableAiDns && enableAiBypass && aiRoutingMode == "dns_ai";
        var selectedSmartDns = SmartDnsProviders.ForAi(aiDnsProvider);
        var generalSmartDns = SmartDnsProviders.Find(dnsProvider);
        var aiDnsUrl = modernAiDns ? selectedSmartDns.DohUrl : "https://xbox-dns.ru/dns-query";
        var aiDomains = modernAiDns ? SmartDnsProviders.AiDomains : XboxAiDomains;
        enableAiXboxDns = enableAiXboxDns || modernAiDns;
        if (enableAiXboxDns) enableSniffing = true;
        if (enableAdBlock) enableSniffing = true;
        if (muxConcurrency is < 1 or > 128) throw new ArgumentException("Mux: параллельность от 1 до 128.");
        if (!ValidRange(tlsFragmentLength, 1, 65535) || !ValidRange(tlsFragmentInterval, 0, 10000) ||
            !(tlsFragmentPackets == "tlshello" || ValidRange(tlsFragmentPackets, 1, 100)))
            throw new ArgumentException("Некорректные параметры фрагментации TLS.");
        isFakeDns = isFakeDns && enableTun && enableSniffing; var root = new JsonObject();

        root["stats"] = new JsonObject();
        if (metricsPort > 0)
            root["metrics"] = new JsonObject { ["tag"] = "metrics", ["listen"] = $"127.0.0.1:{metricsPort}" };
        // 1. Log
        root["log"] = new JsonObject
        {
            ["loglevel"] = "info"
        };

        // 2. DNS
        var (dohUrl, fallbackIp) = dnsProvider.ToLowerInvariant() switch
        {
            "google" => ("8.8.8.8", "8.8.8.8"),
            "quad9" => ("9.9.9.9", "9.9.9.9"),
            "adguard" => ("94.140.14.14", "94.140.14.14"),
            "yandex" => ("tcp://77.88.8.8:53", "77.88.8.8"),
            "xbox" => ("https://xbox-dns.ru/dns-query", "111.88.96.57"),
            "dns_ai" => ("https://dns.dns-ai.ru/dns-query", "1.1.1.1"),
            "custom" => (ValidateDoh(customDohUrl), "1.1.1.1"),
            _ => ("1.1.1.1", "1.1.1.1")
        };

        var servers = new JsonArray();
        if (generalSmartDns != null) dohUrl = generalSmartDns.DohUrl;
        if (isFakeDns) servers.Add("fakedns");

        // Split-DNS: Local Yandex DNS for RU domains (TCP 53 prevents UDP transaction ID loss)
        var ruDomains = new JsonArray
        {
            "geosite:category-ru",
            "geosite:tld-ru",
            "geosite:yandex",
            "geosite:vk",
            "geosite:mailru",
            "domain:gosuslugi.ru",
            "domain:sberbank.ru",
            "domain:tinkoff.ru",
            "domain:vtb.ru",
            "domain:alfabank.ru",
            "domain:raiffeisen.ru",
            "domain:gazprombank.ru",
            "domain:mos.ru",
            "domain:nalog.ru",
            "domain:ru",
            "domain:su",
            "domain:xn--p1ai"
        };
        if (!string.IsNullOrWhiteSpace(node?.Address) && !System.Net.IPAddress.TryParse(node.Address, out _))
        {
            ruDomains.Add($"domain:{node.Address.Trim()}");
        }

        var ruDns = new JsonObject
        {
            ["address"] = "tcp://77.88.8.8:53",
            ["port"] = 53,
            ["skipFallback"] = true,
            ["domains"] = ruDomains
        };
        if (mode == "smart_ru") servers.Add(ruDns);

        if (enableAiXboxDns)
            servers.Add(new JsonObject
            {
                ["address"] = aiDnsUrl,
                ["tag"] = "dns-xbox",
                ["queryStrategy"] = "UseIPv4",
                ["domains"] = new JsonArray(aiDomains.Select(d => JsonValue.Create(d)).ToArray()),
                ["skipFallback"] = true
            });

        // Main DoH via proxy (bypasses ISP/TSPU DPI blocking)
        servers.Add(new JsonObject { ["address"] = dohUrl, ["tag"] = generalSmartDns != null ? "dns-ai-provider" : "dns-query" });




        var protocol = node?.Protocol?.ToLowerInvariant() ?? "vless";
        var rawSec = node?.Security?.ToLowerInvariant()?.Trim() ?? "none";
        var vlessProxyDomain = ServerAddressAlias(node);
        var hosts = new JsonObject
        {
            ["cloudflare-dns.com"] = "1.1.1.1",
            ["dns.google"] = "8.8.8.8",
            ["dns.quad9.net"] = "9.9.9.9",
            ["dns.adguard.com"] = "94.140.14.14",
            ["xbox-dns.ru"] = "111.88.96.57"
        };
        if (modernAiDns || dnsProvider == "dns_ai") hosts["dns.dns-ai.ru"] = new JsonArray { "194.87.196.13", "186.246.49.127" };
        foreach (var provider in new[] { modernAiDns ? selectedSmartDns : null, generalSmartDns }.OfType<SmartDnsProvider>())
            hosts[provider.Host] = new JsonArray(provider.BootstrapIpv4.Select(ip => JsonValue.Create(ip)).ToArray());
        if (enableAdBlock) AdFilterService.AddDnsHosts(hosts, adBlockMatcher, adBlockAllowlist);
        if (vlessProxyDomain != null) hosts[vlessProxyDomain] = node!.Address;
        root["dns"] = new JsonObject
        {
            ["servers"] = servers,
            ["hosts"] = hosts,
            ["queryStrategy"] = blockIpv6 ? "UseIPv4" : "UseIP",
            ["tag"] = "dns-query",
            ["enableParallelQuery"] = true,
            ["serveStale"] = true,
            ["serveExpiredTTL"] = 86400,
            ["disableFallbackIfMatch"] = true,
            ["useSystemHosts"] = true
        };

        // 2.1 FakeDNS Pool (only if explicitly enabled)
        if (isFakeDns)
        {
            root["fakedns"] = new JsonArray
            {
                new JsonObject
                {
                    ["ipPool"] = "198.18.0.0/15",
                    ["poolSize"] = 65535
                }
            };
        }

        // 3. Inbounds: SOCKS5 & HTTP
        var inbounds = new JsonArray();

        // Inbound sockopt: eliminate Nagle buffering on local loopback connections
        var inboundSockopt = new JsonObject
        {
            ["tcpNoDelay"] = true,
            ["tcpFastOpen"] = true
        };

        // 3.1 SOCKS5 inbound
        var socksIn = new JsonObject
        {
            ["tag"] = "socks-in",
            ["protocol"] = "socks",
            ["port"] = socksPort,
            ["listen"] = shareLan ? "0.0.0.0" : "127.0.0.1",
            ["settings"] = new JsonObject
            {
                ["auth"] = "noauth",
                ["udp"] = true
            },
            ["streamSettings"] = new JsonObject
            {
                ["sockopt"] = inboundSockopt.DeepClone()
            }
        };
        if (enableSniffing)
        {
            var socksDest = new JsonArray { "http", "tls" };
            if (isFakeDns) socksDest.Add("fakedns");
            socksIn["sniffing"] = new JsonObject
            {
                ["enabled"] = true,
                ["destOverride"] = socksDest,
                ["metadataOnly"] = false,
                ["routeOnly"] = (isFakeDns || enableAiXboxDns) ? false : routeOnlySniffing
            };
        }
        inbounds.Add(socksIn);

        // 3.2 HTTP inbound for Windows System Proxy
        var httpIn = new JsonObject
        {
            ["tag"] = "http-in",
            ["protocol"] = "http",
            ["port"] = httpPort,
            ["listen"] = shareLan ? "0.0.0.0" : "127.0.0.1",
            ["settings"] = new JsonObject
            {
                ["allowTransparent"] = false
            },
            ["streamSettings"] = new JsonObject
            {
                ["sockopt"] = inboundSockopt.DeepClone()
            }
        };
        if (enableSniffing)
        {
            var httpDest = new JsonArray { "http", "tls" };
            if (isFakeDns) httpDest.Add("fakedns");
            httpIn["sniffing"] = new JsonObject
            {
                ["enabled"] = true,
                ["destOverride"] = httpDest,
                ["metadataOnly"] = false,
                ["routeOnly"] = (isFakeDns || enableAiXboxDns) ? false : routeOnlySniffing
            };
        }
        inbounds.Add(httpIn);

        root["inbounds"] = inbounds;

        // 4. Outbounds
        var outbounds = new JsonArray();
        var proxyOutbound = BuildProxyOutbound(node, xhttpModeOverride, tcpCongestion, enableTlsFragment, vlessProxyDomain);
        var effectiveNetwork = proxyOutbound["streamSettings"]?["network"]?.GetValue<string>();
        if (enableMux && protocol is "vless" or "vmess" && effectiveNetwork is not ("xhttp" or "splithttp" or "grpc") && node?.Flow != "xtls-rprx-vision")
            proxyOutbound["mux"] = new JsonObject { ["enabled"] = true, ["concurrency"] = muxConcurrency, ["xudpConcurrency"] = 8 };
        outbounds.Add(proxyOutbound);

        if (enableTlsFragment)
        {
            outbounds.Add(new JsonObject
            {
                ["tag"] = "fragment",
                ["protocol"] = "freedom",
                ["settings"] = new JsonObject
                {
                    ["domainStrategy"] = "AsIs",
                    ["fragment"] = new JsonObject
                    {
                        ["packets"] = tlsFragmentPackets,
                        ["length"] = tlsFragmentLength,
                        ["interval"] = tlsFragmentInterval
                    }
                }
            });
        }

        var directSockopt = new JsonObject
        {
            ["domainStrategy"] = "UseIP",
            ["tcpNoDelay"] = true,
            ["tcpKeepAliveInterval"] = 15,
            ["tcpKeepAliveIdle"] = 15
        };
        if (!string.IsNullOrEmpty(tcpCongestion) && !tcpCongestion.Equals("bbr", StringComparison.OrdinalIgnoreCase))
        {
            directSockopt["tcpCongestion"] = tcpCongestion;
        }

        outbounds.Add(new JsonObject
        {
            ["tag"] = "direct",
            ["protocol"] = "freedom",
            ["streamSettings"] = new JsonObject
            {
                ["sockopt"] = directSockopt
            }
        });

        if (enableAiXboxDns)
        {
            var aiSockopt = directSockopt.DeepClone().AsObject();
            aiSockopt["domainStrategy"] = "ForceIPv4";
            outbounds.Add(new JsonObject
            {
                ["tag"] = "ai-smartdns",
                ["protocol"] = "freedom",
                ["settings"] = modernAiDns && aiTlsFragment ? new JsonObject { ["fragment"] = new JsonObject { ["packets"] = "tlshello", ["length"] = "10-20", ["interval"] = "1-2" } } : new JsonObject(),
                ["streamSettings"] = new JsonObject { ["sockopt"] = aiSockopt }
            });
        }

        outbounds.Add(new JsonObject
        {
            ["tag"] = "dns-out",
            ["protocol"] = "dns"
        });

        outbounds.Add(new JsonObject
        {
            ["tag"] = "block",
            ["protocol"] = "blackhole",
            ["settings"] = new JsonObject
            {
                ["response"] = new JsonObject
                {
                    ["type"] = "http"
                }
            }
        });

        if (!string.IsNullOrWhiteSpace(uplinkInterface))
        {
            foreach (var outbound in outbounds.OfType<JsonObject>())
            {
                if (outbound["protocol"]?.GetValue<string>() is not ("freedom" or "vless" or "vmess" or "trojan" or "shadowsocks")) continue;
                var stream = outbound["streamSettings"] as JsonObject ?? new JsonObject();
                if (outbound["streamSettings"] == null) outbound["streamSettings"] = stream;
                var opt = stream["sockopt"] as JsonObject ?? new JsonObject();
                if (stream["sockopt"] == null) stream["sockopt"] = opt;
                opt["interface"] = uplinkInterface;
                if (outbound["tag"]?.GetValue<string>() != "ai-smartdns") opt["domainStrategy"] = "UseIPv4";
            }
        }
        root["outbounds"] = outbounds;

        // 5. Routing Rules
        var rules = new JsonArray();
        if (enableAdBlock) rules.Add(AdFilterService.BlockRule(adBlockMatcher, adBlockAllowlist));
        if (generalSmartDns != null) rules.Add(new JsonObject { ["type"] = "field", ["inboundTag"] = new JsonArray { "dns-ai-provider" }, ["outboundTag"] = "direct" });
        if (enableAiXboxDns)
            rules.Add(new JsonObject { ["type"] = "field", ["inboundTag"] = new JsonArray { "dns-xbox" }, ["outboundTag"] = "direct" });
        if (mode == "smart_ru")
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray { "dns-query" },
                ["ip"] = new JsonArray { "77.88.8.8" },
                ["outboundTag"] = "direct"
            });
        rules.Add(new JsonObject { ["type"] = "field", ["inboundTag"] = new JsonArray { "dns-query" }, ["outboundTag"] = "proxy" });

        // 5.1 Route incoming DNS (UDP 53) to Xray DNS
        rules.Add(new JsonObject
        {
            ["type"] = "field",
            ["inboundTag"] = new JsonArray { "socks-in", "http-in" },
            ["port"] = "53",
            ["outboundTag"] = "dns-out"
        });

        // 5.2 Block IPv6 if enabled
        if (blockIpv6)
        {
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["ip"] = new JsonArray { "::/0" },
                ["outboundTag"] = "block"
            });
        }

        // 5.3 Block QUIC (UDP 443) globally
        if (blockQuic)
        {
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["network"] = "udp",
                ["port"] = "443",
                ["outboundTag"] = "block"
            });
        }

        // 5.4 NTP (UDP 123) direct
        rules.Add(new JsonObject
        {
            ["type"] = "field",
            ["network"] = "udp",
            ["port"] = "123",
            ["outboundTag"] = "direct"
        });

        // 5.5 Per-App Routing (Process / Application based)



        // Match the reference client's priority: diagnostics measure the VPN,
        // even when smart-RU or a broad user direct rule would bypass it.
        rules.Add(new JsonObject
        {
            ["type"] = "field",
            ["domain"] = new JsonArray { "domain:cp.cloudflare.com", "domain:speed.cloudflare.com", "domain:cachefly.net", "full:ipwho.is", "full:api.ip.sb", "full:ipinfo.io", "full:connectivitycheck.gstatic.com", "full:www.msftconnecttest.com" },
            ["outboundTag"] = "proxy"
        });
        AddCustomRoutingRules(rules, customBlockRules, "block");
        if (enableAiXboxDns)
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "ai-smartdns",
                ["domain"] = new JsonArray(aiDomains.Select(d => JsonValue.Create(d)).ToArray())
            });

        // 5.6 User Custom Rules (Always Proxy / Always Direct)
        AddCustomRoutingRules(rules, customProxyRules, "proxy");
        AddCustomRoutingRules(rules, customDirectRules, "direct");

        if (enableAiBypass)
        {
            // Keep desktop AI apps on the selected VPN, including long-lived TLS connections.
            // AI rules precede smart-RU IP routing.
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "proxy",
                ["domain"] = new JsonArray
                {
                    "domain:openai.com",
                    "domain:chatgpt.com",
                    "domain:oaistatic.com",
                    "domain:oaiusercontent.com",
                    "domain:sora.com",
                    "domain:ai.com",
                    "domain:gemini.google.com",
                    "domain:generativelanguage.googleapis.com",
                    "domain:proactivebackend-pa.googleapis.com",
                    "domain:alkalimakersuite-pa.googleapis.com",
                    "domain:aistudio.google.com",
                    "domain:makersuite.google.com",
                    "domain:bard.google.com",
                    "domain:deepmind.google",
                    "domain:claude.ai",
                    "domain:claude.com",
                    "domain:anthropic.com",
                    "domain:anthropic.ai",
                    "domain:poe.com",
                    "domain:copilot.microsoft.com",
                    "domain:perplexity.ai",
                    "domain:x.ai",
                    "domain:grok.com",
                    "geosite:openai",
                    "geosite:anthropic"
                }
            });
        }

        if (mode == "smart_ru")
        {
            // Direct RU Domains
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "direct",
                ["domain"] = new JsonArray
                {
                    "geosite:category-ru",
                    "geosite:tld-ru",
                    "geosite:yandex",
                    "geosite:vk",
                    "geosite:mailru",
                    "domain:gosuslugi.ru",
                    "domain:sberbank.ru",
                    "domain:tinkoff.ru",
                    "domain:vtb.ru",
                    "domain:alfabank.ru",
                    "domain:raiffeisen.ru",
                    "domain:gazprombank.ru",
                    "domain:mos.ru",
                    "domain:nalog.ru",
                    "domain:ru",
                    "domain:su",
                    "domain:xn--p1ai"
                }
            });

            // Direct RU IPs & Private IPs
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "direct",
                ["ip"] = new JsonArray { "geoip:ru", "geoip:private" }
            });

            // Fast path for popular foreign / CDN / measurement domains to proxy (bypasses RU IP DNS check)
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "proxy",
                ["domain"] = new JsonArray
                {
                    "geosite:youtube",
                    "geosite:google",
                    "geosite:telegram",
                    "geosite:discord",
                    "geosite:instagram",
                    "geosite:twitter",
                    "geosite:facebook",
                    "geosite:openai",
                    "geosite:notion",
                    "geosite:spotify",
                    "geosite:netflix",
                    "geosite:github",
                    "geosite:cloudflare",
                    "geosite:speedtest",
                    "domain:rutracker.org",
                    "domain:nnmclub.to",
                    "domain:kinozal.tv",
                    "domain:fast.com",
                    "domain:speedtest.net",
                    "domain:ooklaserver.net"
                }
            });

            // Telegram IPs to proxy
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "proxy",
                ["ip"] = new JsonArray { "geoip:telegram" }
            });
        }
        else
        {
            // Global mode: direct only private IPs
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "direct",
                ["ip"] = new JsonArray { "geoip:private" }
            });
        }

        // Default route everything else to proxy (both TCP and UDP natively supported by modern Xray 26.9.9)
        rules.Add(new JsonObject
        {
            ["type"] = "field",
            ["network"] = "tcp,udp",
            ["outboundTag"] = "proxy"
        });

        root["routing"] = new JsonObject
        {
            ["domainStrategy"] = "IPIfNonMatch",
            ["rules"] = rules
        };

        // 6. Policy: High-performance buffer and connection timeouts (matching Android)
        root["policy"] = new JsonObject
        {
            ["levels"] = new JsonObject
            {
                ["0"] = new JsonObject
                {
                    ["bufferSize"] = bufferSizeKb > 0 ? bufferSizeKb : 4096,
                    ["handshake"] = 8,
                    ["connIdle"] = 300,
                    ["uplinkOnly"] = 5,
                    ["downlinkOnly"] = 30
                }
            },
            ["system"] = new JsonObject
            {
                ["statsInboundUplink"] = true,
                ["statsInboundDownlink"] = true,
                ["statsOutboundUplink"] = true,
                ["statsOutboundDownlink"] = true
            }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };
        return root.ToJsonString(options);
    }

    public static string? ServerAddressAlias(ServerNode? node)
    {
        if (node == null || !node.Protocol.Equals("vless", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(node.Security) && !node.Security.Equals("none", StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(node.Address)) return null;
        if (System.Net.IPAddress.TryParse(node.Address, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            if (System.Net.IPAddress.IsLoopback(ip) || (bytes.Length == 4 && (bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || (bytes[0] == 192 && bytes[1] == 168))) ||
                (bytes.Length == 16 && ((bytes[0] & 0xfe) == 0xfc || ip.IsIPv6LinkLocal))) return null;
        }
        else if (node.Address.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || node.Address.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) || node.Address.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return null;
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(node.Address.ToLowerInvariant()));
        return "server-" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant() + ".internal";
    }

    public static string ValidateDoh(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("Собственный DoH должен быть HTTPS URL.");
        if (uri.AbsolutePath == "/" && uri.Query.Length == 0 && SmartDnsProviders.All.FirstOrDefault(p => p.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase)) is { } provider) return provider.DohUrl;
        return uri.AbsoluteUri;
    }

    public static bool ValidRange(string value, int minimum, int maximum)
    {
        var parts = value.Split('-');
        return parts.Length is 1 or 2 && parts.All(p => int.TryParse(p, out var n) && n >= minimum && n <= maximum) &&
            (parts.Length == 1 || int.Parse(parts[0]) <= int.Parse(parts[1]));
    }

    public static JsonObject BuildProxyOutbound(
        ServerNode? node,
        string xhttpModeOverride = "auto",
        string tcpCongestion = "bbr",
        bool enableTlsFragment = false,
        string? vlessProxyDomain = null,
        string tag = "proxy")
    {
        vlessProxyDomain ??= ServerAddressAlias(node);
        var proxy = new JsonObject
        {
            ["tag"] = tag
        };

        var protocol = node?.Protocol?.ToLowerInvariant() ?? "vless";
        proxy["protocol"] = protocol;

        var network = string.IsNullOrEmpty(node?.Network) ? "tcp" : node.Network.ToLowerInvariant();
        if (network == "splithttp") network = "xhttp";
        var security = string.IsNullOrEmpty(node?.Security) ? (protocol == "trojan" ? "tls" : "none") : node.Security.ToLowerInvariant();

        var settings = new JsonObject();
        if (protocol == "vless")
        {
            var user = new JsonObject
            {
                ["id"] = string.IsNullOrEmpty(node?.Uuid) ? "00000000-0000-0000-0000-000000000000" : node.Uuid,
                ["encryption"] = string.IsNullOrEmpty(node?.Encryption) ? "none" : node.Encryption
            };
            if (!string.IsNullOrEmpty(node?.Flow))
            {
                user["flow"] = node.Flow;
            }
            var serverItem = new JsonObject
            {
                ["address"] = vlessProxyDomain ?? (string.IsNullOrEmpty(node?.Address) ? "127.0.0.1" : node.Address),
                ["port"] = node?.Port > 0 ? node.Port : 443,
                ["users"] = new JsonArray { user }
            };

            settings["vnext"] = new JsonArray { serverItem };
        }
        else if (protocol == "trojan")
        {
            var serverItem = new JsonObject
            {
                ["address"] = string.IsNullOrEmpty(node?.Address) ? "127.0.0.1" : node.Address,
                ["port"] = node?.Port > 0 ? node.Port : 443,
                ["password"] = node?.Uuid ?? ""
            };
            settings["servers"] = new JsonArray { serverItem };
        }
        else if (protocol == "vmess")
        {
            var user = new JsonObject
            {
                ["id"] = node?.Uuid ?? "",
                ["security"] = "auto"
            };
            var serverItem = new JsonObject
            {
                ["address"] = string.IsNullOrEmpty(node?.Address) ? "127.0.0.1" : node.Address,
                ["port"] = node?.Port > 0 ? node.Port : 443,
                ["users"] = new JsonArray { user }
            };
            settings["vnext"] = new JsonArray { serverItem };
        }
        else if (protocol == "shadowsocks")
        {
            var serverItem = new JsonObject
            {
                ["address"] = string.IsNullOrEmpty(node?.Address) ? "127.0.0.1" : node.Address,
                ["port"] = node?.Port > 0 ? node.Port : 8388,
                ["method"] = string.IsNullOrEmpty(node?.Encryption) || node.Encryption == "none" ? "aes-256-gcm" : node.Encryption,
                ["password"] = node?.Uuid ?? ""
            };
            settings["servers"] = new JsonArray { serverItem };
        }

        proxy["settings"] = settings;

        // StreamSettings
        var streamSettings = new JsonObject
        {
            ["network"] = network,
            ["security"] = security
        };

        // Resolve ALPN protocol negotiation array (Critical for HTTP/2 multiplexing on XHTTP/H2)
        var alpnArray = new JsonArray();
        if (!string.IsNullOrEmpty(node?.Alpn))
        {
            foreach (var a in node.Alpn.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = a.Trim();
                if (network == "ws" && trimmed.Equals("h2", StringComparison.OrdinalIgnoreCase)) continue;
                alpnArray.Add(trimmed);
            }
        }
        if (alpnArray.Count == 0)
        {
            if (network == "xhttp" || network == "h2")
            {
                alpnArray.Add("h2");
                alpnArray.Add("http/1.1");
            }
            else if (network == "ws")
            {
                alpnArray.Add("http/1.1");
            }
        }

        if (security == "reality")
        {
            var reality = new JsonObject
            {
                ["show"] = false,
                ["fingerprint"] = string.IsNullOrEmpty(node?.Fingerprint) ? "chrome" : node.Fingerprint,
                ["serverName"] = string.IsNullOrEmpty(node?.Sni) ? (node?.Address ?? "") : node.Sni,
                ["publicKey"] = node?.PublicKey ?? "",
                ["shortId"] = node?.ShortId ?? "",
                ["spiderX"] = node?.SpiderX ?? ""
            };
            if (!string.IsNullOrEmpty(node?.Mldsa65Verify))
            {
                reality["mldsa65Verify"] = node.Mldsa65Verify;
            }
            if (alpnArray.Count > 0)
            {
                reality["alpn"] = alpnArray.DeepClone();
            }
            streamSettings["realitySettings"] = reality;
        }
        else if (security == "tls")
        {
            var serverName = !string.IsNullOrEmpty(node?.Sni) ? node.Sni : (node?.Address ?? "");
            var tls = new JsonObject
            {
                ["serverName"] = serverName,
                ["fingerprint"] = string.IsNullOrEmpty(node?.Fingerprint) ? "chrome" : node.Fingerprint,
                ["enableSessionResumption"] = true
            };
            if (!string.IsNullOrEmpty(node?.VerifyPeerCertByName))
            {
                tls["verifyPeerCertByName"] = node.VerifyPeerCertByName;
            }
            if (!string.IsNullOrEmpty(node?.PinnedPeerCertSha256))
            {
                tls["pinnedPeerCertSha256"] = node.PinnedPeerCertSha256;
            }

            if (alpnArray.Count > 0)
            {
                tls["alpn"] = alpnArray.DeepClone();
            }
            streamSettings["tlsSettings"] = tls;
        }

        // Outbound TCP sockopt for maximum throughput
        var sockopt = new JsonObject
        {
            ["tcpNoDelay"] = true,
            ["tcpKeepAliveInterval"] = 15,
            ["tcpKeepAliveIdle"] = 15
        };
        if (vlessProxyDomain != null) sockopt["domainStrategy"] = "UseIP";
        // On Windows, 'bbr' is not a valid Winsock TCP_CONGESTION algorithm and returns WSAEINVAL
        if (!string.IsNullOrEmpty(tcpCongestion) && !tcpCongestion.Equals("bbr", StringComparison.OrdinalIgnoreCase))
        {
            sockopt["tcpCongestion"] = tcpCongestion;
        }
        if (enableTlsFragment && security == "tls")
        {
            sockopt["dialerProxy"] = "fragment";
        }
        streamSettings["sockopt"] = sockopt;

        if (network == "xhttp")
        {
            streamSettings["network"] = "xhttp";

            var xhttp = new JsonObject
            {
                ["path"] = string.IsNullOrEmpty(node?.Path) ? "/" : node.Path,
                ["mode"] = ResolveXhttpMode(xhttpModeOverride, node)
            };
            if (!string.IsNullOrEmpty(node?.Host)) xhttp["host"] = node.Host;
            if (!string.IsNullOrEmpty(node?.Padding))
            {
                // In Xray 26.9.9 xPaddingBytes is directly on xhttpSettings
                xhttp["xPaddingBytes"] = node.Padding;
            }
            // Multiplexing for high throughput on XHTTP (only if server specifies xmux)
            if (node?.XmuxMaxConnections > 0)
            {
                var xmux = new JsonObject
                {
                    ["maxConnections"] = node.XmuxMaxConnections.Value
                };
                if (!string.IsNullOrEmpty(node.XmuxMaxRequestTimes)) xmux["hMaxRequestTimes"] = node.XmuxMaxRequestTimes;
                if (!string.IsNullOrEmpty(node.XmuxMaxReusableSecs)) xmux["hMaxReusableSecs"] = node.XmuxMaxReusableSecs;
                xhttp["xmux"] = xmux;
            }
            streamSettings["xhttpSettings"] = xhttp;
        }
        else if (network == "ws")
        {
            var rawPath = string.IsNullOrEmpty(node?.Path) ? "/" : node.Path;
            var normalizedPath = rawPath.StartsWith("/") ? rawPath : "/" + rawPath;
            var ws = new JsonObject
            {
                ["path"] = normalizedPath,
                ["heartbeatPeriod"] = 15
            };
            var hostVal = !string.IsNullOrEmpty(node?.Host) ? node.Host : node?.Sni;
            if (!string.IsNullOrEmpty(hostVal))
            {
                ws["host"] = hostVal;
            }
            streamSettings["wsSettings"] = ws;
        }
        else if (network == "grpc")
        {
            streamSettings["grpcSettings"] = new JsonObject
            {
                ["serviceName"] = string.IsNullOrEmpty(node?.Path) ? "gun" : node.Path
            };
        }

        proxy["streamSettings"] = streamSettings;
        return proxy;
    }

    private static string ResolveXhttpMode(string xhttpModeOverride, ServerNode? node)
    {
        if (!string.IsNullOrEmpty(xhttpModeOverride) &&
            !xhttpModeOverride.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
            IsValidXhttpMode(xhttpModeOverride))
        {
            return xhttpModeOverride.ToLowerInvariant();
        }

        if (!string.IsNullOrEmpty(node?.Mode) && IsValidXhttpMode(node.Mode))
            return node.Mode.ToLowerInvariant();

        return "auto";
    }

    private static bool IsValidXhttpMode(string? mode) =>
        !string.IsNullOrEmpty(mode)
        && mode.ToLowerInvariant() is "auto" or "stream-one" or "stream-up" or "stream-down" or "packet-up";

    private static void AddCustomRoutingRules(JsonArray rules, string? customRulesText, string outboundTag)
    {
        if (string.IsNullOrWhiteSpace(customRulesText)) return;

        var domains = new JsonArray();
        var ips = new JsonArray();

        var lines = customRulesText.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var item = rawLine.Trim();
            if (string.IsNullOrEmpty(item) || item.StartsWith('#') || item.StartsWith("//"))
                continue;

            // Check if IP address, CIDR, or geoip
            if (item.StartsWith("geoip:", StringComparison.OrdinalIgnoreCase) ||
                System.Net.IPAddress.TryParse(item, out _) ||
                item.Contains('/') && (item.Contains('.') || item.Contains(':')))
            {
                ips.Add(item);
            }
            else
            {
                // Domain format
                if (item.StartsWith("domain:", StringComparison.OrdinalIgnoreCase) ||
                    item.StartsWith("geosite:", StringComparison.OrdinalIgnoreCase) ||
                    item.StartsWith("full:", StringComparison.OrdinalIgnoreCase) ||
                    item.StartsWith("regexp:", StringComparison.OrdinalIgnoreCase))
                {
                    domains.Add(item);
                }
                else
                {
                    domains.Add($"domain:{item}");
                }
            }
        }

        if (domains.Count > 0)
        {
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = outboundTag,
                ["domain"] = domains
            });
        }

        if (ips.Count > 0)
        {
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = outboundTag,
                ["ip"] = ips
            });
        }
    }

}
