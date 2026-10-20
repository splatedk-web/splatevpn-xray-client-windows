using System.IO;

namespace SplateClientDesktop.Services;

// Stage every file before changing the first destination; restore committed files on failure.
internal static class AtomicFileSet
{
    public static void Write(IReadOnlyDictionary<string, byte[]> files)
    {
        var staged = new Dictionary<string, string>();
        var originals = new Dictionary<string, byte[]?>();
        var committed = new List<string>();
        try
        {
            foreach (var (path, bytes) in files)
            {
                originals[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                staged[path] = temp;
                File.WriteAllBytes(temp, bytes);
            }
            foreach (var (path, temp) in staged)
            {
                File.Move(temp, path, true);
                committed.Add(path);
            }
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            foreach (var path in committed.AsEnumerable().Reverse())
            {
                try
                {
                    if (originals[path] is { } bytes)
                    {
                        File.WriteAllBytes(staged[path], bytes);
                        File.Move(staged[path], path, true);
                    }
                    else File.Delete(path);
                }
                catch (Exception rollback) { failures.Add(rollback); }
            }
            if (failures.Count > 1) throw new AggregateException("Не удалось полностью восстановить файлы после ошибки записи.", failures);
            throw;
        }
        finally
        {
            foreach (var temp in staged.Values)
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
