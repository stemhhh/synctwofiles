using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SyncTwoFiles.Core;

public static class PathUtil
{
    private static readonly Regex BareDrive = new(@"^[A-Za-z]:$", RegexOptions.Compiled);

    // "E:" significa "carpeta actual del disco E", no la raiz: la raiz siempre conserva su barra.
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var p = path.Trim();
        if (BareDrive.IsMatch(p)) return p + "\\";
        try { p = Path.GetFullPath(p); } catch { }
        string? root = null;
        try { root = Path.GetPathRoot(p); } catch { }
        if (!string.IsNullOrEmpty(root) && p.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            if (BareDrive.IsMatch(root)) root += "\\";
            return root;
        }
        return p.TrimEnd('\\');
    }

    public static bool IsDriveRoot(string? path)
    {
        var p = Normalize(path);
        if (p.Length == 0) return false;
        string? root = null;
        try { root = Path.GetPathRoot(p); } catch { }
        return !string.IsNullOrEmpty(root) && p.Equals(root, StringComparison.OrdinalIgnoreCase);
    }

    public static string Label(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var p = path.TrimEnd('\\');
        var leaf = Path.GetFileName(p);
        if (string.IsNullOrEmpty(leaf) || leaf == p) return Normalize(path);
        return leaf;
    }

    public static bool IsInside(string? child, string? parent)
    {
        var c = Normalize(child).TrimEnd('\\') + "\\";
        var p = Normalize(parent).TrimEnd('\\') + "\\";
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Overlap(string? a, string? b) => IsInside(a, b) || IsInside(b, a);

    public static bool SamePath(string? a, string? b) =>
        Normalize(a).Equals(Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static string? DriveRoot(string? path)
    {
        try { return Path.GetPathRoot(Normalize(path)); } catch { return null; }
    }
}
