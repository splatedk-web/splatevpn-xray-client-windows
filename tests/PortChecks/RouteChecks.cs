using System.Net.NetworkInformation;
using System.Text.Json.Nodes;
using SplateClientDesktop.Models;
using SplateClientDesktop.Services;

internal static class RouteChecks
{
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static async Task RunAsync()
    {
        foreach (var mode in new[] { "off", "vpn", "dns_ai" })
        {
            var settings = new AppSettings { SettingsSchemaVersion = 2, AiRoutingMode = mode, EnableAiXboxDns = true };
            SettingsCompatibility.Normalize(settings);
            Check(!settings.EnableAiXboxDns && settings.EnableAiBypass == (mode != "off") && settings.EnableAiDns == (mode == "dns_ai"), "one mode determines all AI flags");
            Check(!SettingsCompatibility.Normalize(settings), "migration is idempotent");
        }
        var old = new AppSettings { EnableAiXboxDns = true, DnsProvider = "xbox", EnableAiBypass = false };
        SettingsCompatibility.Normalize(old);
        Check(old.EnableAiDns && old.EnableAiBypass && !old.EnableAiXboxDns && old.DnsProvider == "cloudflare", "remove obsolete Xbox endpoint");
        var normal = new AppSettings(); SettingsCompatibility.Normalize(normal);
        Check(normal.AiRoutingMode == "vpn" && !normal.EnableAiDns, "old defaults keep VPN instead of enabling direct SmartDNS");
        var off = new AppSettings { EnableAiBypass = false }; SettingsCompatibility.Normalize(off);
        Check(off.AiRoutingMode == "off", "disabled AI route stays disabled");

        const string geminiBlock = "Supercharge your creativity and productivity Chat to start writing, planning, learning and more with Google AI <b>Gemini isn’t currently supported in your country. Stay tuned!</b>";
        Check(AiDnsAutoSelector.Classify(200, geminiBlock) == AiDnsOutcome.GeoBlocked, "HTTP 200 with exact reported Gemini regional block is failure");
        Check(AiDnsAutoSelector.Classify(200, "Gemini isn&#8217;t currently <b>supported</b> in your country.") == AiDnsOutcome.GeoBlocked, "HTML entities and formatting cannot hide regional block");
        Check(AiDnsAutoSelector.Classify(200, "<script>translations='Gemini isn’t currently supported in your country'</script><main>Gemini home</main>") == AiDnsOutcome.Accessible, "unused script translations do not imply a block");
        Check(AiDnsAutoSelector.ClassifyRendered(200, "Something went wrong. Try again later.") == AiDnsOutcome.Limited, "rendered generic Gemini error cannot qualify");
        Check(AiDnsAutoSelector.ClassifyRendered(200, "Verify you are human CAPTCHA") == AiDnsOutcome.Limited, "rendered CAPTCHA cannot qualify");
        Check(AiDnsAutoSelector.ClassifyRendered(200, "Gemini home Sign in") == AiDnsOutcome.Accessible, "normal sign-in header alone is not a failed page");
        var query = AiServiceProbe.DnsQuery("gemini.google.com");
        byte[] Answer(byte[] ip)
        {
            var result = query.Concat(new byte[] { 0xc0, 0x0c, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4 }).Concat(ip).ToArray();
            result[2] = 0x81; result[3] = 0x80; result[7] = 1; return result;
        }
        void Reject(byte[] packet, string reason) { try { AiServiceProbe.ValidateDnsAnswer(packet, query); } catch (IOException) { return; } throw new Exception(reason); }
        Check(AiServiceProbe.ValidateDnsAnswer(Answer([8, 8, 8, 8]), query).Single() == "8.8.8.8", "parse DNS response with compressed answer name");
        foreach (var ip in new byte[][] { [0,0,0,0], [127,0,0,1], [10,0,0,1], [192,168,1,1], [172,19,0,1], [100,64,0,1], [224,0,0,1] }) Reject(Answer(ip), "reject blocking/private DNS result");
        var malformed = Answer([8,8,8,8]); malformed[0] ^= 1; Reject(malformed, "reject foreign transaction");
        malformed = Answer([8,8,8,8]); malformed[3] = 0x83; Reject(malformed, "reject NXDOMAIN");
        malformed = Answer([8,8,8,8]); malformed[2] |= 2; Reject(malformed, "reject truncated flag");
        malformed = Answer([8,8,8,8]); malformed[14] ^= 1; Reject(malformed, "reject other DNS question");
        Reject(Answer([8,8,8,8])[..^1], "reject truncated RDATA");
        var first = new AiDnsCandidateResult(new("dns_ai", false), [new("Gemini", AiDnsOutcome.Accessible), new("ChatGPT", AiDnsOutcome.Failed)]);
        var swapped = first with { Targets = [new("Gemini", AiDnsOutcome.Failed), new("ChatGPT", AiDnsOutcome.Accessible)] };
        Check(await AiDnsAutoSelector.ConfirmAsync([first], _ => Task.FromResult(swapped), "Gemini") == null, "confirmation must retain Gemini access, not swap successful services");
        Check(await AiDnsAutoSelector.ConfirmAsync([swapped], _ => throw new Exception("wrong candidate"), "Gemini") == null, "ChatGPT alone does not qualify for Gemini selection");

        var uplink = await TunDeviceManager.GetUplinkAsync();
        Check(NetworkInterface.GetAllNetworkInterfaces().Any(n => n.Name == uplink.Name && n.GetIPProperties().GetIPv4Properties()?.Index == uplink.Index), "physical interface matches native Unicode name and numeric index");
        var json = JsonNode.Parse(new XrayConfigGenerator().GenerateConfig(new ServerNode { Address = "127.0.0.1", Security = "none" }, customDirectRules: "ipwho.is", mode: "smart_ru"))!;
        var rules = json["routing"]!["rules"]!.AsArray();
        var diagnostic = rules.First(r => r?["domain"]?.ToJsonString().Contains("full:ipwho.is") == true)!;
        Check(diagnostic["outboundTag"]!.ToString() == "proxy" && rules.IndexOf(diagnostic) < rules.ToList().FindIndex(r => r?["domain"]?.ToJsonString().Contains("domain:ipwho.is") == true), "IP diagnostic precedes direct rules");
        Console.WriteLine("PASS routing repair: native uplink binding, migration and unified AI modes; DNS wire errors/private answers; same-service confirmation; VPN-only diagnostic routing");
    }
}
