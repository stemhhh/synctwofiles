using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SyncTwoFiles.Core;

// Cada regla es un nombre o comodin (node_modules, .git, *.tmp) que se aplica a carpetas y archivos,
// igual que robocopy /XD y /XF.
public sealed class ExcludeRules
{
    public static readonly string[] SystemDirs = { "$RECYCLE.BIN", "System Volume Information" };
    public const string VersionsDirName = ".SyncTwoFiles-versiones";

    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly char[] Forbidden = { '\\', '/', ':', '"', '<', '>', '|' };

    public ExcludeRules(IEnumerable<string>? user = null) => User = Clean(user);

    public IReadOnlyList<string> User { get; }

    public IEnumerable<string> DirNames => SystemDirs.Append(VersionsDirName).Concat(User);
    public IEnumerable<string> FileNames => User;

    public static List<string> Clean(IEnumerable<string>? items) =>
        (items ?? Array.Empty<string>())
            .Select(i => (i ?? "").Trim().TrimEnd('\\', '/').Trim())
            .Where(i => i.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static List<string> Parse(string? text) =>
        Clean((text ?? "").Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries));

    public static string? Validate(IEnumerable<string>? items)
    {
        foreach (var i in Clean(items))
        {
            if (i == "." || i == ".." || i.IndexOfAny(Forbidden) >= 0)
                return L.T("val.exclude", i);
        }
        return null;
    }

    public bool IsDirExcluded(string name) => DirNames.Any(p => Matches(name, p));
    public bool IsFileExcluded(string name) => User.Any(p => Matches(name, p));

    public static bool Matches(string name, string pattern)
    {
        if (pattern.IndexOfAny(new[] { '*', '?' }) < 0)
            return name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        var rx = Cache.GetOrAdd(pattern, p => new Regex(
            "^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        return rx.IsMatch(name);
    }
}
