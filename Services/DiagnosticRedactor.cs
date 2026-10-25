using System.Text.RegularExpressions;

namespace SplateClientDesktop.Services;

public static class DiagnosticRedactor
{
    public static string Redact(string text, IEnumerable<string?> privateValues)
    {
        foreach (var value in privateValues.Where(v => v?.Length >= 4).Distinct().OrderByDescending(v => v!.Length))
            text = text.Replace(value!, "[скрыто]", StringComparison.OrdinalIgnoreCase);
        var options = RegexOptions.IgnoreCase;
        text = Regex.Replace(text, @"\b(?:vless|vmess|trojan|ss|socks5?|https?)://[^\s<>""']+", "[ссылка скрыта]", options);
        text = Regex.Replace(text, @"[""']?(?:uuid|password|token|secret|authorization|privatekey|publickey|pbk|sid)[""']?\s*[:=]\s*(?:""[^""]*""|'[^']*'|[^\s,;]+)", "[ключ скрыт]", options);
        text = Regex.Replace(text, @"\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b", "[UUID скрыт]", options);
        text = Regex.Replace(text, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", m => m.Value == "127.0.0.1" ? m.Value : "[IP скрыт]");
        text = Regex.Replace(text, @"(?<![\w:])(?:[0-9a-f]{0,4}:){2,}[0-9a-f:.]*(?:%[\w]+)?", m => m.Value.Contains("::") || m.Value.Count(c => c == ':') >= 3 ? "[IPv6 скрыт]" : m.Value, options);
        return Regex.Replace(text, @"[a-z]:\\[^\r\n""<>]+", "[локальный путь скрыт]", options);
    }
}
