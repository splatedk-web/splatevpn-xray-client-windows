namespace SplateClientDesktop.Services;

public sealed class TrafficAccumulator
{
    private long _previousDown, _previousUp;
    private bool _first = true;
    public long Download { get; private set; }
    public long Upload { get; private set; }
    public void NewCore(bool preserveSession)
    {
        if (!preserveSession) Download = Upload = 0;
        _previousDown = _previousUp = 0; _first = true;
    }
    public (long DownBps, long UpBps, long Download, long Upload) Observe(long down, long up, double seconds)
    {
        var rx = Math.Max(0, down - _previousDown); var tx = Math.Max(0, up - _previousUp);
        if (down < _previousDown) rx = Math.Max(0, down);
        if (up < _previousUp) tx = Math.Max(0, up);
        _previousDown = down; _previousUp = up; Download += rx; Upload += tx;
        var sample = (_first ? 0 : (long)(rx / Math.Max(.001, seconds)), _first ? 0 : (long)(tx / Math.Max(.001, seconds)), Download, Upload);
        _first = false; return sample;
    }
}
