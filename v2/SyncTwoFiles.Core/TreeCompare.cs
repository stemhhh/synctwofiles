using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SyncTwoFiles.Core;

public sealed record FileChange(string Path, DateTime SourceTime, DateTime DestTime, long SourceSize, long DestSize)
{
    public bool SourceIsOlder => SourceTime < DestTime - TreeCompare.TimeTolerance;
}

public sealed class DiffResult
{
    public List<string> NewFiles { get; } = new();
    public List<FileChange> ChangedFiles { get; } = new();
    public List<string> ExtraFiles { get; } = new();
    public List<string> ExtraDirs { get; } = new();
    public List<string> Errors { get; } = new();
    public long PendingBytes { get; set; }
    public int SourceFiles { get; set; }
    public long SourceBytes { get; set; }
    public int DestFiles { get; set; }
    public long DestBytes { get; set; }
    public int ExtraCount { get; set; }

    public bool InSync => NewFiles.Count == 0 && ChangedFiles.Count == 0 && ExtraCount == 0;
    public int MissingCount => NewFiles.Count + ChangedFiles.Count;
}

// Compara dos arboles igual que robocopy (tamano + fecha de modificacion) sin depender del idioma de su salida.
// Rutas relativas a la raiz; ExtraDirs son carpetas que solo existen en el destino (se cuentan una vez, no su contenido).
public static class TreeCompare
{
    public static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2);

    private static readonly EnumerationOptions EnumOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public static DiffResult Compare(string source, string dest, ExcludeRules rules, CancellationToken ct = default)
    {
        var r = new DiffResult();
        Walk(source, Directory.Exists(dest) ? dest : null, "", rules, r, ct);
        return r;
    }

    private static void Walk(string srcDir, string? dstDir, string rel, ExcludeRules rules, DiffResult r, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (srcFiles, srcDirs) = List(srcDir, rules, r);
        var (dstFiles, dstDirs) = dstDir is null
            ? (new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, DirectoryInfo>(StringComparer.OrdinalIgnoreCase))
            : List(dstDir, rules, r);

        foreach (var (name, sf) in srcFiles)
        {
            r.SourceFiles++;
            r.SourceBytes += sf.Length;
            var path = Join(rel, name);
            if (!dstFiles.TryGetValue(name, out var df))
            {
                r.NewFiles.Add(path);
                r.PendingBytes += sf.Length;
            }
            else if (df.Length != sf.Length || (df.LastWriteTimeUtc - sf.LastWriteTimeUtc).Duration() > TimeTolerance)
            {
                r.ChangedFiles.Add(new FileChange(path, sf.LastWriteTimeUtc, df.LastWriteTimeUtc, sf.Length, df.Length));
                r.PendingBytes += sf.Length;
            }
        }
        foreach (var (name, df) in dstFiles)
        {
            r.DestFiles++;
            r.DestBytes += df.Length;
            if (!srcFiles.ContainsKey(name))
            {
                r.ExtraFiles.Add(Join(rel, name));
                r.ExtraCount++;
            }
        }
        foreach (var (name, sd) in srcDirs)
        {
            dstDirs.TryGetValue(name, out var dd);
            Walk(sd.FullName, dd?.FullName, Join(rel, name), rules, r, ct);
        }
        foreach (var (name, dd) in dstDirs)
        {
            if (srcDirs.ContainsKey(name)) continue;
            r.ExtraDirs.Add(Join(rel, name));
            var (files, bytes) = Measure(dd.FullName, rules, ct);
            r.DestFiles += files;
            r.DestBytes += bytes;
            r.ExtraCount += Math.Max(1, files);
        }
    }

    private static (Dictionary<string, FileInfo>, Dictionary<string, DirectoryInfo>) List(string dir, ExcludeRules rules, DiffResult r)
    {
        var files = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        var dirs = new Dictionary<string, DirectoryInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", EnumOptions))
            {
                if (e is DirectoryInfo d)
                {
                    if (IsLink(d) || rules.IsDirExcluded(d.Name)) continue;
                    dirs[d.Name] = d;
                }
                else if (e is FileInfo f)
                {
                    if (IsLink(f) || rules.IsFileExcluded(f.Name)) continue;
                    files[f.Name] = f;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            r.Errors.Add($"{dir}: {ex.Message}");
        }
        return (files, dirs);
    }

    public static (int Files, long Bytes) Measure(string dir, ExcludeRules rules, CancellationToken ct = default)
    {
        int files = 0;
        long bytes = 0;
        var stack = new Stack<string>();
        stack.Push(dir);
        var scratch = new DiffResult();
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (fs, ds) = List(stack.Pop(), rules, scratch);
            foreach (var f in fs.Values) { files++; bytes += f.Length; }
            foreach (var d in ds.Values) stack.Push(d.FullName);
        }
        return (files, bytes);
    }

    // /XJ: robocopy solo omite uniones y enlaces simbolicos. Otros puntos de reanalisis,
    // como los archivos en la nube de OneDrive, son archivos normales que si copia.
    public static bool IsLink(FileSystemInfo i) =>
        (i.Attributes & FileAttributes.ReparsePoint) != 0 && i.LinkTarget != null;

    private static string Join(string rel, string name) => rel.Length == 0 ? name : rel + "\\" + name;
}
