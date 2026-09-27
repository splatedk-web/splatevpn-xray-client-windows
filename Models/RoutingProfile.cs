namespace SplateClientDesktop.Models;

public class RoutingProfile
{
    public string Name { get; set; } = string.Empty;
    public bool SplitTunnel { get; set; } = false;
    public List<string> AllowedIpRanges { get; set; } = new();
    public string DnsProvider { get; set; } = "cloudflare";
    public string? Description { get; set; }
}
