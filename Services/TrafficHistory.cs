using System.IO;
using System.Text.Json;

namespace SplateClientDesktop.Services;

public record TrafficDay(DateOnly Date, long Download, long Upload, long ConnectedMs);

public sealed class TrafficHistory
{
    private readonly object _lock = new();
    private readonly string _path;
    private List<TrafficDay> _days;
    private long _down, _up;
    private DateTimeOffset _lastSample, _lastPersist;
    public TrafficHistory(string path)
    {
        _path = path;
        try { _days = JsonSerializer.Deserialize<List<TrafficDay>>(File.ReadAllText(path)) ?? []; }
        catch { _days = []; }
    }
    public void BeginSession() { lock (_lock) { _down = _up = 0; _lastSample = DateTimeOffset.Now; } }
    public void Record(long down, long up)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.Now;
            if (_lastSample == default) { BeginSession(); return; }
            var slices = Split(now, now - _lastSample, Math.Max(0, down - _down), Math.Max(0, up - _up), TimeZoneInfo.Local);
            foreach (var slice in slices)
            {
                var old = _days.FirstOrDefault(d => d.Date == slice.Date) ?? new TrafficDay(slice.Date, 0, 0, 0);
                _days.RemoveAll(d => d.Date == slice.Date);
                _days.Add(new(slice.Date, old.Download + slice.Download, old.Upload + slice.Upload, old.ConnectedMs + slice.ConnectedMs));
            }
            _days = _days.Where(d => d.Date >= DateOnly.FromDateTime(now.LocalDateTime).AddDays(-399)).OrderByDescending(d => d.Date).ToList();
            _down = down; _up = up; _lastSample = now;
            if ((now - _lastPersist).TotalSeconds >= 15) Flush();
        }
    }
    public List<TrafficDay> Days { get { lock (_lock) return _days.ToList(); } }
    public void Clear() { lock (_lock) { _days.Clear(); Flush(); } }
    public void Flush()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_days));
            File.Move(_path + ".tmp", _path, true); _lastPersist = DateTimeOffset.Now;
        }
    }
    public static List<TrafficDay> Split(DateTimeOffset end, TimeSpan duration, long down, long up, TimeZoneInfo zone)
    {
        var ms = Math.Max(1, (long)duration.TotalMilliseconds);
        var cursor = end.AddMilliseconds(-ms); var remainingDown = Math.Max(0, down); var remainingUp = Math.Max(0, up);
        var result = new List<TrafficDay>();
        while (cursor < end)
        {
            var local = TimeZoneInfo.ConvertTime(cursor, zone); var date = DateOnly.FromDateTime(local.DateTime);
            var midnight = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var next = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight, zone));
            if (next > end) next = end;
            var slice = (long)(next - cursor).TotalMilliseconds; var last = next == end;
            var rx = last ? remainingDown : (long)(Math.Max(0, down) * (double)slice / ms);
            var tx = last ? remainingUp : (long)(Math.Max(0, up) * (double)slice / ms);
            result.Add(new(date, rx, tx, slice)); remainingDown -= rx; remainingUp -= tx; cursor = next;
        }
        return result;
    }
}
