namespace SplateClientDesktop.Models;

public record TemporaryRoute(string Domain, string Route, DateTimeOffset ExpiresAt);
