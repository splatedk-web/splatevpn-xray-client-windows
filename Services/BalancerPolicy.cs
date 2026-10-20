using SplateClientDesktop.Models;

namespace SplateClientDesktop.Services;

public sealed class BalancerPolicy(Func<long>? clock = null)
{
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private long _nextOptimization;
    private long? _lastSwitch;
    private int _failures, _degraded;
    private double _baseline;
    public enum Decision { Keep, ConfirmFailure, FindReserve, Optimize }
    public void Start(AppSettings s) { _failures = _degraded = 0; _baseline = 0; _nextOptimization = _clock() + s.BalancerOptimizationMinutes * 60000L; }
    public void Switched() => _lastSwitch = _clock();
    public bool CanSwitch(AppSettings s, bool dead) => !_lastSwitch.HasValue || _clock() - _lastSwitch.Value >= (dead ? 3000 : s.BalancerCooldownSeconds * 1000L);
    public Decision Observe(long ping, AppSettings settings, bool speedtest, long downloadBps, bool networkAvailable)
    {
        if (!settings.AutoFallbackFastestServer || settings.BalancerMode == "initial_only") return Decision.Keep;
        if (ping <= 0)
        {
            if (!networkAvailable) { _failures = 0; return Decision.Keep; }
            _failures++;
            return _failures >= settings.BalancerFailureConfirmations - 1 && CanSwitch(settings, true) ? Decision.ConfirmFailure : Decision.Keep;
        }
        _failures = 0;
        var degraded = _baseline > 0 && ping > 350 && ping > _baseline * 2.2;
        _degraded = degraded ? _degraded + 1 : 0;
        if (!degraded) _baseline = _baseline <= 0 ? ping : _baseline * .85 + ping * .15;
        var due = _clock() >= _nextOptimization;
        if (due) _nextOptimization = _clock() + settings.BalancerOptimizationMinutes * 60000L;
        if (settings.BalancerMode == "balanced" && !speedtest && downloadBps < 100000 && CanSwitch(settings, false) && (_degraded >= 3 || due))
        { _degraded = 0; return Decision.Optimize; }
        return Decision.Keep;
    }
    public void ConfirmedSuccess() => _failures = 0;
    public static bool IsImprovement(long current, long candidate, AppSettings s) => current > 0 && candidate > 0 && candidate <= current * (1 - s.BalancerImprovementPercent / 100.0);
}
