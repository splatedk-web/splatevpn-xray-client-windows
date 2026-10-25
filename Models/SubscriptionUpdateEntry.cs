namespace SplateClientDesktop.Models;

public record SubscriptionUpdateEntry(DateTimeOffset Time, string SubscriptionId, string Name, int Before, int After, string Result, string? Error);
