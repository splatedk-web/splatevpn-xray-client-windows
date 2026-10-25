namespace SplateClientDesktop.Services;

public static class AiDnsFailover
{
    private static bool Failed(AiDnsTargetResult t) => t.Outcome is AiDnsOutcome.Failed or AiDnsOutcome.GeoBlocked;
    private static bool Improves(AiDnsCandidateResult baseline, AiDnsCandidateResult candidate, HashSet<string> failed) =>
        baseline.Targets.Length > 0 && baseline.Targets.All(old => candidate.Targets.Any(next => next.Service == old.Service &&
            (old.Outcome == AiDnsOutcome.Accessible ? next.Outcome == AiDnsOutcome.Accessible : old.Outcome != AiDnsOutcome.Limited || next.Outcome is AiDnsOutcome.Accessible or AiDnsOutcome.Limited))) &&
        candidate.Targets.Any(t => failed.Contains(t.Service) && t.Outcome == AiDnsOutcome.Accessible);
    public static async Task<AiDnsCandidateResult?> FindAsync(AiDnsChoice current, Func<AiDnsChoice, CancellationToken, Task<AiDnsCandidateResult>> probe,
        CancellationToken token, Func<CancellationToken, Task>? wait = null, IEnumerable<AiDnsChoice>? candidates = null)
    {
        wait ??= ct => Task.Delay(TimeSpan.FromSeconds(15), ct);
        var first = await probe(current, token);
        if (!first.Targets.Any(Failed)) return null;
        await wait(token);
        var second = await probe(current, token);
        var failed = first.Targets.Where(Failed).Select(t => t.Service).Intersect(second.Targets.Where(Failed).Select(t => t.Service)).ToHashSet();
        if (failed.Count == 0) return null;
        var baseline = new AiDnsCandidateResult(current, first.Targets.Concat(second.Targets).GroupBy(t => t.Service).Select(g =>
            g.FirstOrDefault(t => t.Outcome == AiDnsOutcome.Accessible) ?? g.FirstOrDefault(t => t.Outcome == AiDnsOutcome.Limited) ?? g.Last()).ToArray());
        candidates ??= SmartDnsProviders.All.SelectMany(p => new[] { new AiDnsChoice(p.Key, false), new AiDnsChoice(p.Key, true) });
        foreach (var choice in candidates.Distinct().Where(c => c != current).OrderBy(c => c.TlsFragment != current.TlsFragment))
        {
            token.ThrowIfCancellationRequested();
            var tested = await probe(choice, token);
            if (tested.Choice != choice || !Improves(baseline, tested, failed)) continue;
            await wait(token);
            var confirmed = await probe(choice, token);
            if (confirmed.Choice != choice || !Improves(baseline, confirmed, failed)) continue;
            var recovered = failed.Where(service => tested.Targets.Any(t => t.Service == service && t.Outcome == AiDnsOutcome.Accessible) &&
                confirmed.Targets.Any(t => t.Service == service && t.Outcome == AiDnsOutcome.Accessible)).ToHashSet();
            if (recovered.Count == 0) continue;
            var latest = await probe(current, token);
            if (!latest.Targets.Any(t => recovered.Contains(t.Service) && Failed(t))) return null;
            if (Improves(latest, confirmed, recovered)) return confirmed;
        }
        return null;
    }
}
