using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SyncTwoFiles.Core;

// Lectura del historial de robocopy (UTF-16 con o sin BOM; los .old.log de la 1.x pueden ser ANSI).
public static class HistoryLog
{
    public static string ReadTail(string path, long maxBytes = 2 * 1024 * 1024)
    {
        if (!File.Exists(path)) return "";
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var utf16 = PairLog.IsUtf16File(path);
        var start = Math.Max(0, fs.Length - maxBytes);
        if (utf16) start -= start % 2;
        fs.Seek(start, SeekOrigin.Begin);
        var buf = new byte[fs.Length - start];
        var n = fs.Read(buf, 0, buf.Length);
        if (utf16) n -= n % 2;
        var text = utf16 ? Encoding.Unicode.GetString(buf, 0, n) : Encoding.Latin1.GetString(buf, 0, n);
        return text.TrimStart('\uFEFF').Replace("\0", "");
    }

    public static IEnumerable<string> Filter(string text, string? query, bool errorsOnly)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;
            if (errorsOnly && !IsError(line)) continue;
            if (!string.IsNullOrWhiteSpace(query) && line.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
            yield return line;
        }
    }

    public static bool IsError(string line) =>
        line.Contains("ERROR ", StringComparison.Ordinal) || line.Contains("ERROR:", StringComparison.Ordinal);

    // Errores nuevos desde la ultima posicion leida (para avisar sin repetir).
    public static int CountNewErrors(string path, ref long position)
    {
        if (!File.Exists(path)) { position = 0; return 0; }
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (position > fs.Length || position < 0) position = 0;
            var count = fs.Length - position;
            count -= count % 2;
            if (count <= 0) return 0;
            if (count > 4 * 1024 * 1024) { position = fs.Length - fs.Length % 2; return 0; }
            fs.Seek(position, SeekOrigin.Begin);
            var buf = new byte[count];
            var n = fs.Read(buf, 0, (int)count);
            n -= n % 2;
            position += n;
            var text = Encoding.Unicode.GetString(buf, 0, n);
            return Filter(text, null, true).Count();
        }
        catch { return 0; }
    }
}
