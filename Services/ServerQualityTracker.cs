namespace SplateClientDesktop.Services;

// Same moving average, jitter penalty and exponential quarantine as Android.
public sealed class ServerQualityTracker(Func<long>? clock = null)
{
    private sealed class Quality
    {
        public double Mean, Jitter;
        public int Successes, Failures;
        public long LastSuccess = long.MinValue, LastFailure = long.MinValue, RetryAfter = long.MinValue;
    }
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private readonly Dictionary<string, Quality> _history = new();
    private readonly object _lock = new();
    public void Record(string id, long delay)
    {
        lock (_lock)
        {
            if (!_history.TryGetValue(id, out var q)) _history[id] = q = new();
            var now = _clock();
            if (delay <= 0)
            {
                q.Failures = Math.Min(5, q.Failures + 1); q.LastFailure = now;
                q.RetryAfter = now + Math.Min(300000L, 60000L << (q.Failures - 1));
                return;
            }
            if (q.Successes == 0 || now - q.LastSuccess > 300000) { q.Mean = delay; q.Jitter = 0; q.Successes = 0; }
            else { q.Jitter = q.Jitter * .7 + Math.Abs(delay - q.Mean) * .3; q.Mean = q.Mean * .7 + delay * .3; }
            q.Successes++; q.LastSuccess = now; q.RetryAfter = long.MinValue;
            if (q.Failures > 0 && q.LastFailure != long.MinValue && now - q.LastFailure > 300000) q.Failures--;
        }
    }
    public bool IsQuarantined(string id)
    {
        lock (_lock) return _history.TryGetValue(id, out var q) && _clock() < q.RetryAfter;
    }
    public long Score(string id, long delay, bool active = false)
    {
        lock (_lock)
        {
            if (delay <= 0 || IsQuarantined(id)) return long.MaxValue;
            var now = _clock();
            _history.TryGetValue(id, out var q);
            double score = q?.Successes > 0 && now - q.LastSuccess <= 300000 ? Math.Max(delay, q.Mean) + q.Jitter * 2 : delay;
            if (q?.Failures > 0 && now - q.LastFailure <= 300000) score += q.Failures * 180;
            if (score > 300) score += (score - 300) * .5;
            if (active) score -= Math.Min(score * .1, 25);
            return Math.Max(1, (long)score);
        }
    }
}
