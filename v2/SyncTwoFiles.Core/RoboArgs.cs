using System.Collections.Generic;
using System.Linq;

namespace SyncTwoFiles.Core;

public static class RoboArgs
{
    public static List<string> Excludes(ExcludeRules rules)
    {
        var list = new List<string> { "/XD" };
        list.AddRange(rules.DirNames);
        var files = rules.FileNames.ToList();
        if (files.Count > 0)
        {
            list.Add("/XF");
            list.AddRange(files);
        }
        return list;
    }

    public static List<string> Common(ExcludeRules rules) =>
        new List<string> { "/COPY:DAT", "/DCOPY:T", "/XJ", "/R:2", "/W:5" }.Concat(Excludes(rules)).ToList();

    public static List<string> AutoSync(SyncPair p) =>
        new List<string> { p.Source, p.Backup, "/E" }.Concat(Common(p.Rules)).ToList();

    public static List<string> Monitor(SyncPair p, string logFile) =>
        AutoSync(p).Concat(new[] { "/NP", "/NFL", "/NDL", "/MON:1", "/MOT:1", "/UNILOG+:" + logFile }).ToList();

    public static List<string> Mirror(SyncPair p) =>
        new List<string> { p.Source, p.Backup, "/MIR" }.Concat(Common(p.Rules)).ToList();

    public static List<string> Restore(SyncPair p) =>
        new List<string> { p.Backup, p.Source, "/E", "/XO" }.Concat(Common(p.Rules)).ToList();

    public static List<string> RestoreFile(string fromDir, string toDir, string fileName, bool skipNewer)
    {
        var a = new List<string> { fromDir, toDir, fileName, "/COPY:DAT", "/XJ", "/R:2", "/W:5" };
        if (skipNewer) a.Add("/XO");
        return a;
    }

    public static List<string> RestoreDir(string fromDir, string toDir, bool skipNewer, ExcludeRules rules)
    {
        var a = new List<string> { fromDir, toDir, "/E" };
        if (skipNewer) a.Add("/XO");
        a.AddRange(Common(rules));
        return a;
    }

    public static List<string> RecoverVersion(string sessionDir, string source, bool skipNewer = true)
    {
        var a = new List<string> { sessionDir, source, "/E" };
        if (skipNewer) a.Add("/XO");
        a.AddRange(new[] { "/COPY:DAT", "/DCOPY:T", "/XJ", "/R:2", "/W:5" });
        return a;
    }

    public static bool IsSuccess(int exitCode) => exitCode >= 0 && exitCode < 8;

    // Una barra final antes de la comilla de cierre ("E:\") la escaparia; se duplica ("E:\\").
    public static string ToArgumentString(IEnumerable<string> args) =>
        string.Join(" ", args.Select(a =>
        {
            if (a.Length == 0) return "\"\"";
            if (!a.Any(char.IsWhiteSpace)) return a;
            return "\"" + (a.EndsWith("\\") ? a + "\\" : a) + "\"";
        }));
}
