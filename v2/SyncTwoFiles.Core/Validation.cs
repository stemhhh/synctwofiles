using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SyncTwoFiles.Core;

public static class Validation
{
    public static string? ValidatePair(string? name, string? source, string? backup,
        IEnumerable<SyncPair>? pairs = null, string? excludeId = null, IEnumerable<string>? excludes = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return L.T("val.name");
        if (string.IsNullOrWhiteSpace(source)) return L.T("val.source");
        if (string.IsNullOrWhiteSpace(backup)) return L.T("val.backup");
        if (!Directory.Exists(source)) return L.T("val.sourceMissing");

        var s = PathUtil.Normalize(source);
        var b = PathUtil.Normalize(backup);
        if (PathUtil.IsDriveRoot(b)) return L.T("val.backupRoot", b);
        if (s.Equals(b, StringComparison.OrdinalIgnoreCase)) return L.T("val.same");
        if (PathUtil.IsInside(b, s)) return L.T("val.backupInside");
        if (PathUtil.IsInside(s, b)) return L.T("val.sourceInside");

        foreach (var p in (pairs ?? Enumerable.Empty<SyncPair>()).Where(p => p is not null && p.Id != excludeId))
        {
            if (!string.IsNullOrWhiteSpace(p.Name) && p.Name.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                return L.T("val.dupName", p.Name);
            if (PathUtil.SamePath(p.Source, s) && PathUtil.SamePath(p.Backup, b))
                return L.T("val.dupPair", p.Name);
            if (PathUtil.Overlap(b, p.Backup))
                return L.T("val.backupShared", p.Name);
        }
        return ExcludeRules.Validate(excludes);
    }
}
