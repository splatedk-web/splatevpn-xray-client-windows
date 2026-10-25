namespace SplateClientDesktop.Models;

public record ConnectionProfile(string Id, string Name, string Snapshot)
{
    public string? NetworkKey { get; init; }
    public string? NetworkLabel { get; init; }
}
