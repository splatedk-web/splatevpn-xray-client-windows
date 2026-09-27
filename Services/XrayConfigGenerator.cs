using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public class XrayConfigGenerator
{
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
        string? perAppDirectList = null)
    {
        var root = new JsonObject();

        // 1. Log
        root["log"] = new JsonObject
        {
            ["loglevel"] = "info"
        };

        // 2. DNS
        var (dohUrl, fallbackIp) = dnsProvider.ToLowerInvariant() switch
        {
            "google" => ("https://8.8.8.8/dns-query", "8.8.8.8"),
            "quad9" => ("https://9.9.9.9/dns-query", "9.9.9.9"),
            "adguard" => ("https://94.140.14.14/dns-query", "94.140.14.14"),
            "yandex" => ("tcp://77.88.8.8:53", "77.88.8.8"),
            "xbox" => ("https://xbox-dns.ru/dns-query", "111.88.96.57"),
            _ => ("https://1.1.1.1/dns-query", "1.1.1.1")
        };

        var servers = new JsonArray();
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
            ["domains"] = ruDomains
        };
        servers.Add(ruDns);

        if (enableAiBypass)
        {
            var aiDns = new JsonObject
            {
                ["address"] = "https://xbox-dns.ru/dns-query",
                ["port"] = 443,
                ["domains"] = new JsonArray
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
                    "domain:bing.com",
                    "domain:perplexity.ai",
                    "domain:x.ai",
                    "domain:grok.com",
                    "geosite:openai"
                }
            };
            servers.Add(aiDns);
        }

        // Main DoH via proxy (bypasses ISP/TSPU DPI blocking)
        servers.Add(new JsonObject { ["address"] = dohUrl });
        servers.Add(fallbackIp);
        servers.Add("tcp://8.8.8.8:53");
        servers.Add("tcp://1.1.1.1:53");

        var protocol = node?.Protocol?.ToLowerInvariant() ?? "vless";
        var rawSec = node?.Security?.ToLowerInvariant()?.Trim() ?? "none";
        string? vlessProxyDomain = null;
        var hosts = new JsonObject
        {
            ["cloudflare-dns.com"] = "1.1.1.1",
            ["dns.google"] = "8.8.8.8",
            ["dns.quad9.net"] = "9.9.9.9",
            ["dns.adguard.com"] = "94.140.14.14",
            ["xbox-dns.ru"] = "111.88.96.57"
        };
        if (protocol == "vless" && rawSec == "none" && !string.IsNullOrWhiteSpace(node?.Address))
        {
            vlessProxyDomain = "server.local";
            hosts[vlessProxyDomain] = node.Address.Trim();
        }

        root["dns"] = new JsonObject
        {
            ["servers"] = servers,
            ["hosts"] = hosts,
            ["queryStrategy"] = "UseIPv4",
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
            ["listen"] = "127.0.0.1",
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
                ["routeOnly"] = routeOnlySniffing
            };
        }
        inbounds.Add(socksIn);

        // 3.2 HTTP inbound for Windows System Proxy
        var httpIn = new JsonObject
        {
            ["tag"] = "http-in",
            ["protocol"] = "http",
            ["port"] = httpPort,
            ["listen"] = "127.0.0.1",
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
                ["metadataOnly"] = true,
                ["routeOnly"] = routeOnlySniffing
            };
        }
        inbounds.Add(httpIn);

        if (enableTun)
        {
            // 3.3 Dokodemo-door / Tun Inbound for system-wide packet interception
            var tunIn = new JsonObject
            {
                ["tag"] = "tun-in",
                ["protocol"] = "dokodemo-door",
                ["port"] = socksPort + 1,
                ["listen"] = "127.0.0.1",
                ["settings"] = new JsonObject
                {
                    ["network"] = "tcp,udp",
                    ["followRedirect"] = true
                },
                ["streamSettings"] = new JsonObject
                {
                    ["sockopt"] = inboundSockopt.DeepClone()
                }
            };
            if (enableSniffing)
            {
                var tunDest = new JsonArray { "http", "tls", "quic" };
                if (isFakeDns) tunDest.Add("fakedns");
                tunIn["sniffing"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["destOverride"] = tunDest,
                    ["metadataOnly"] = false,
                    ["routeOnly"] = routeOnlySniffing
                };
            }
            inbounds.Add(tunIn);
        }

        root["inbounds"] = inbounds;

        // 4. Outbounds
        var outbounds = new JsonArray();
        outbounds.Add(BuildProxyOutbound(node, xhttpModeOverride, tcpCongestion, enableTlsFragment, vlessProxyDomain));

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
                        ["packets"] = "tlshello",
                        ["length"] = "100-200",
                        ["interval"] = "10-20"
                    }
                }
            });
        }

        outbounds.Add(new JsonObject
        {
            ["tag"] = "direct",
            ["protocol"] = "freedom",
            ["streamSettings"] = new JsonObject
            {
                ["sockopt"] = new JsonObject
                {
                    ["domainStrategy"] = "UseIP",
                    ["tcpNoDelay"] = true,
                    ["tcpKeepAliveInterval"] = 15,
                    ["tcpKeepAliveIdle"] = 15,
                    ["tcpCongestion"] = tcpCongestion
                }
            }
        });

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

        root["outbounds"] = outbounds;

        // 5. Routing Rules
        var rules = new JsonArray();

        // 5.1 Route incoming DNS (UDP 53) to Xray DNS
        rules.Add(new JsonObject
        {
            ["type"] = "field",
            ["inboundTag"] = new JsonArray { "socks-in", "http-in", "tun-in" },
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
        AddPerAppRoutingRules(rules, perAppProxyList, "proxy");
        AddPerAppRoutingRules(rules, perAppDirectList, "direct");

        // 5.6 User Custom Rules (Always Proxy / Always Direct)
        AddCustomRoutingRules(rules, customProxyRules, "proxy");
        AddCustomRoutingRules(rules, customDirectRules, "direct");

        if (enableAiBypass)
        {
            // Route AI domains to direct so they use the IPs resolved by xbox-dns.ru
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = "direct",
                ["domain"] = new JsonArray
                {
                    "geosite:openai",
                    "geosite:anthropic",
                    "geosite:google",
                    "domain:midjourney.com",
                    "domain:poe.com"
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

        // 6. Policy: High-performance buffer and connection timeouts
        root["policy"] = new JsonObject
        {
            ["levels"] = new JsonObject
            {
                ["0"] = new JsonObject
                {
                    ["bufferSize"] = bufferSizeKb > 0 ? bufferSizeKb : 4096,
                    ["handshake"] = 4,
                    ["connIdle"] = 300,
                    ["uplinkOnly"] = 0,
                    ["downlinkOnly"] = 0
                }
            },
            ["system"] = new JsonObject
            {
                ["statsOutboundUplink"] = false,
                ["statsOutboundDownlink"] = false
            }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };
        return root.ToJsonString(options);
    }

    private JsonObject BuildProxyOutbound(
        ServerNode? node,
        string xhttpModeOverride = "auto",
        string tcpCongestion = "bbr",
        bool enableTlsFragment = false,
        string? vlessProxyDomain = null)
    {
        var proxy = new JsonObject
        {
            ["tag"] = "proxy"
        };

        var protocol = node?.Protocol?.ToLowerInvariant() ?? "vless";
        proxy["protocol"] = protocol;

        var network = string.IsNullOrEmpty(node?.Network) ? "tcp" : node.Network.ToLowerInvariant();
        if (network == "splithttp") network = "xhttp";
        var security = string.IsNullOrEmpty(node?.Security) ? "reality" : node.Security.ToLowerInvariant();

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
            else if ((security == "reality" || security == "tls") && network == "tcp")
            {
                user["flow"] = "xtls-rprx-vision";
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

        if (security == "reality")
        {
            var reality = new JsonObject
            {
                ["show"] = false,
                ["fingerprint"] = string.IsNullOrEmpty(node?.Fingerprint) ? "chrome" : node.Fingerprint,
                ["serverName"] = string.IsNullOrEmpty(node?.Sni) ? "www.amazon.com" : node.Sni,
                ["publicKey"] = node?.PublicKey ?? "",
                ["shortId"] = node?.ShortId ?? "",
                ["spiderX"] = node?.SpiderX ?? ""
            };
            if (!string.IsNullOrEmpty(node?.Mldsa65Verify))
            {
                reality["mldsa65Verify"] = node.Mldsa65Verify;
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
            else if (!string.IsNullOrEmpty(node?.Address) && !string.IsNullOrEmpty(node?.Sni) && !string.Equals(node.Sni, node.Address, StringComparison.OrdinalIgnoreCase))
            {
                tls["verifyPeerCertByName"] = node.Address;
            }

            if (!string.IsNullOrEmpty(node?.PinnedPeerCertSha256))
            {
                tls["pinnedPeerCertSha256"] = node.PinnedPeerCertSha256;
            }

            if (!string.IsNullOrEmpty(node?.Alpn))
            {
                var alpnArray = new JsonArray();
                foreach (var a in node.Alpn.Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = a.Trim();
                    // WebSocket over TLS requires HTTP/1.1; negotiating H2 causes connection to hang
                    if (network == "ws" && trimmed.Equals("h2", StringComparison.OrdinalIgnoreCase)) continue;
                    alpnArray.Add(trimmed);
                }
                if (network == "ws" && alpnArray.Count == 0)
                {
                    alpnArray.Add("http/1.1");
                }
                if (alpnArray.Count > 0)
                {
                    tls["alpn"] = alpnArray;
                }
            }
            else if (network == "ws")
            {
                tls["alpn"] = new JsonArray { "http/1.1" };
            }
            streamSettings["tlsSettings"] = tls;
        }

        // Outbound TCP sockopt for maximum throughput
        var sockopt = new JsonObject
        {
            ["tcpNoDelay"] = true,
            ["tcpKeepAliveInterval"] = 15,
            ["tcpKeepAliveIdle"] = 15,
            ["tcpCongestion"] = tcpCongestion
        };
        if (enableTlsFragment && (security == "tls" || security == "reality"))
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
            // Multiplexing for high throughput on XHTTP
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
            else
            {
                xhttp["xmux"] = new JsonObject
                {
                    ["maxConcurrency"] = 16,
                    ["cMaxReuseTimes"] = 64,
                    ["hKeepAlivePeriod"] = 60
                };
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

    private static void AddPerAppRoutingRules(JsonArray rules, string? appListText, string outboundTag)
    {
        if (string.IsNullOrWhiteSpace(appListText)) return;

        var apps = new JsonArray();
        var lines = appListText.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var app = rawLine.Trim();
            if (string.IsNullOrEmpty(app) || app.StartsWith('#') || app.StartsWith("//"))
                continue;

            apps.Add(app);
        }

        if (apps.Count > 0)
        {
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["outboundTag"] = outboundTag,
                ["app"] = apps
            });
        }
    }
}