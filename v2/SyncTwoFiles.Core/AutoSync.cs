using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace SyncTwoFiles.Core;

public sealed record RoboProcess(int Id, string CommandLine);

public enum Availability { Ok, SourceMissing, BackupMissing }

public static class PairLog
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public static string PathFor(SyncPair pair, string logDir) => Path.Combine(logDir, pair.Id + ".log");

    public static bool IsUtf16File(string path)
    {
        byte[] b = new byte[4];
        int n;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            n = fs.Read(b, 0, 4);
        }
        catch { return true; }
        if (n < 2) return true;
        if (b[0] == 0xFF && b[1] == 0xFE) return true;
        return n >= 4 && b[1] == 0 && b[3] == 0;
    }

    // robocopy /UNILOG+ escribe UTF-16 sin BOM y anade al final: un log viejo en otro formato o
    // demasiado grande se archiva, y uno nuevo se crea con BOM para que el Bloc de notas lo lea bien.
    public static void ResetIfNeeded(string logFile)
    {
        if (File.Exists(logFile))
        {
            var len = new FileInfo(logFile).Length;
            if (IsUtf16File(logFile) && len <= MaxBytes) return;
            try { File.Move(logFile, Path.ChangeExtension(logFile, ".old.log"), true); }
            catch { return; }
        }
        try { File.WriteAllBytes(logFile, new byte[] { 0xFF, 0xFE }); } catch { }
    }

    public static DateTime? LastActivity(string logFile)
    {
        try { return File.Exists(logFile) ? File.GetLastWriteTime(logFile) : null; }
        catch { return null; }
    }
}

public static class RoboProcesses
{
    public static List<RoboProcess> List()
    {
        var result = new List<RoboProcess>();
        if (Process.GetProcessesByName("robocopy").Length == 0) return result;
        try
        {
            using var s = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='Robocopy.exe'");
            foreach (ManagementObject o in s.Get())
            {
                using (o)
                {
                    var cmd = o["CommandLine"] as string;
                    if (cmd is not null) result.Add(new RoboProcess(Convert.ToInt32(o["ProcessId"]), cmd));
                }
            }
        }
        catch { }
        return result;
    }

    public static bool IsAutoSyncCommandLine(string? commandLine, string source, string backup)
    {
        if (string.IsNullOrEmpty(commandLine) || !commandLine.Contains("/MON:", StringComparison.OrdinalIgnoreCase)) return false;
        var s = Regex.Escape(PathUtil.Normalize(source).TrimEnd('\\'));
        var b = Regex.Escape(PathUtil.Normalize(backup).TrimEnd('\\'));
        return Regex.IsMatch(commandLine, $"(^|\\s)\"?{s}\\\\*\"?\\s+\"?{b}\\\\*\"?(\\s|$)", RegexOptions.IgnoreCase);
    }

    public static void Kill(int id)
    {
        try { using var p = Process.GetProcessById(id); p.Kill(); p.WaitForExit(3000); } catch { }
    }
}

public sealed class AutoSyncManager
{
    private readonly string _logDir;

    public AutoSyncManager(string logDir) => _logDir = logDir;

    public string LogFor(SyncPair pair) => PairLog.PathFor(pair, _logDir);

    public string ExpectedArguments(SyncPair pair) => RoboArgs.ToArgumentString(RoboArgs.Monitor(pair, LogFor(pair)));

    public static Availability GetAvailability(SyncPair pair)
    {
        if (!Directory.Exists(pair.Source)) return Availability.SourceMissing;
        var root = PathUtil.DriveRoot(pair.Backup);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return Availability.BackupMissing;
        return Availability.Ok;
    }

    public List<RoboProcess> ProcessesFor(SyncPair pair, IEnumerable<RoboProcess>? processes = null) =>
        (processes ?? RoboProcesses.List()).Where(p => RoboProcesses.IsAutoSyncCommandLine(p.CommandLine, pair.Source, pair.Backup)).ToList();

    // Un proceso de otra version (otros argumentos, otro historial) no cuenta como actual.
    public bool IsCurrent(SyncPair pair, RoboProcess process) =>
        process.CommandLine.Contains(ExpectedArguments(pair), StringComparison.OrdinalIgnoreCase);

    public bool Start(SyncPair pair)
    {
        if (GetAvailability(pair) != Availability.Ok) return false;
        try
        {
            Directory.CreateDirectory(_logDir);
            Directory.CreateDirectory(pair.Backup);
            var log = LogFor(pair);
            PairLog.ResetIfNeeded(log);
            var psi = new ProcessStartInfo("robocopy.exe", ExpectedArguments(pair))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            return p is not null;
        }
        catch { return false; }
    }

    public void Stop(SyncPair pair, IEnumerable<RoboProcess>? processes = null)
    {
        foreach (var p in ProcessesFor(pair, processes)) RoboProcesses.Kill(p.Id);
    }

    public void StopAll(SyncConfig config, IEnumerable<RoboProcess>? processes = null)
    {
        var list = (processes ?? RoboProcesses.List()).ToList();
        foreach (var pair in config.Pairs) Stop(pair, list);
    }

    // Inicia las que faltan, reemplaza las de argumentos viejos y no hace nada si todo esta en pausa.
    public int EnsureAll(SyncConfig config, IEnumerable<RoboProcess>? processes = null)
    {
        if (config.Settings.Paused) return 0;
        var list = (processes ?? RoboProcesses.List()).ToList();
        var started = 0;
        foreach (var pair in config.Pairs.Where(p => p.AutoSync))
        {
            try
            {
                var mine = ProcessesFor(pair, list);
                if (mine.Count > 0 && mine.All(p => IsCurrent(pair, p))) continue;
                if (mine.Count > 0 && GetAvailability(pair) != Availability.Ok) continue;
                foreach (var p in mine) RoboProcesses.Kill(p.Id);
                if (Start(pair)) started++;
            }
            catch { }
        }
        return started;
    }
}
