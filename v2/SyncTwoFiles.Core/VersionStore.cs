using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SyncTwoFiles.Core;

public sealed record VersionSession(string Path, DateTime Date, string Kind, int Files, long Bytes)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

public sealed record PreserveResult(string? Session, int Count, List<string> Errors);

// Papelera de versiones: <backup>\.SyncTwoFiles-versiones\aaaa-MM-dd_HHmmss_<tipo>\<ruta relativa>.
// La carpeta se excluye en todos los comandos de robocopy, asi el espejo nunca la borra.
public static class VersionStore
{
    public const string KindMirror = "mirror";
    public const string KindRestore = "restore";
    public const string KindItem = "item";

    private static readonly Regex SessionName = new(@"^(\d{4}-\d{2}-\d{2}_\d{6})_([a-z]+)(-\d+)?$", RegexOptions.Compiled);

    public static string Root(SyncPair pair) => Path.Combine(pair.Backup, ExcludeRules.VersionsDirName);

    private static string NewSessionDir(SyncPair pair, string kind, DateTime now)
    {
        var baseName = Path.Combine(Root(pair), $"{now:yyyy-MM-dd_HHmmss}_{kind}");
        var dir = baseName;
        for (var i = 2; Directory.Exists(dir); i++) dir = $"{baseName}-{i}";
        return dir;
    }

    public static bool TryParseSession(string name, out DateTime date, out string kind)
    {
        date = default;
        kind = "";
        var m = SessionName.Match(name);
        if (!m.Success) return false;
        if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return false;
        kind = m.Groups[2].Value;
        return true;
    }

    // Mueve dentro del backup lo que el espejo borraria o sobrescribiria (mismo disco: es instantaneo).
    public static PreserveResult PreserveBeforeMirror(SyncPair pair, DiffResult diff, DateTime now)
    {
        var items = diff.ExtraDirs
            .Concat(diff.ExtraFiles)
            .Concat(diff.ChangedFiles.Select(c => c.Path))
            .ToList();
        string? session = null;
        var count = 0;
        var errors = new List<string>();
        foreach (var rel in items)
        {
            var src = Path.Combine(pair.Backup, rel);
            try
            {
                var isDir = Directory.Exists(src);
                if (!isDir && !File.Exists(src)) continue;
                session ??= NewSessionDir(pair, KindMirror, now);
                var dst = Path.Combine(session, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                if (isDir) Directory.Move(src, dst);
                else File.Move(src, dst);
                count++;
            }
            catch (Exception ex) { errors.Add($"{rel}: {ex.Message}"); }
        }
        return new PreserveResult(session, count, errors);
    }

    // Archivos de la carpeta original que una restauracion sobrescribiria. Con /XO se saltan los mas nuevos.
    public static List<string> WouldOverwriteOnRestore(DiffResult backupToSource, bool skipNewer) =>
        backupToSource.ChangedFiles.Where(c => !(skipNewer && c.SourceIsOlder)).Select(c => c.Path).ToList();

    // Copia al backup los archivos de la carpeta original antes de que una restauracion los sobrescriba.
    public static PreserveResult PreserveBeforeRestore(SyncPair pair, string fromRoot, string relBase,
        IEnumerable<string> relPaths, string kind, DateTime now)
    {
        string? session = null;
        var count = 0;
        var errors = new List<string>();
        foreach (var rel in relPaths)
        {
            var src = Path.Combine(fromRoot, rel);
            try
            {
                if (!File.Exists(src)) continue;
                session ??= NewSessionDir(pair, kind, now);
                var dst = Path.Combine(session, relBase, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, true);
                count++;
            }
            catch (Exception ex) { errors.Add($"{rel}: {ex.Message}"); }
        }
        return new PreserveResult(session, count, errors);
    }

    public static List<VersionSession> List(SyncPair pair, bool measure = true)
    {
        var root = Root(pair);
        var result = new List<VersionSession>();
        if (!Directory.Exists(root)) return result;
        try
        {
            foreach (var d in new DirectoryInfo(root).EnumerateDirectories())
            {
                if (!TryParseSession(d.Name, out var date, out var kind)) continue;
                var (files, bytes) = measure ? TreeCompare.Measure(d.FullName, new ExcludeRules()) : (0, 0L);
                result.Add(new VersionSession(d.FullName, date, kind, files, bytes));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return result.OrderByDescending(s => s.Date).ToList();
    }

    public static int Cleanup(SyncPair pair, int retentionDays, DateTime now)
    {
        var removed = 0;
        foreach (var s in List(pair, measure: false).Where(s => s.Date < now.AddDays(-retentionDays)))
        {
            if (Delete(s.Path)) removed++;
        }
        return removed;
    }

    public static bool Delete(string sessionDir)
    {
        var full = Path.GetFullPath(sessionDir).TrimEnd('\\');
        if (!TryParseSession(Path.GetFileName(full), out _, out _) ||
            !string.Equals(Path.GetFileName(Path.GetDirectoryName(full)), ExcludeRules.VersionsDirName, StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            foreach (var f in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                var a = File.GetAttributes(f);
                if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, a & ~FileAttributes.ReadOnly);
            }
            Directory.Delete(full, true);
            return true;
        }
        catch { return false; }
    }
}
