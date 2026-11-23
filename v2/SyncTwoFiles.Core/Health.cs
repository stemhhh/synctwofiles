using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SyncTwoFiles.Core;

public sealed class PairState
{
    public DateTime? LastAvailable { get; set; }
    public Dictionary<string, DateTime> LastAlerts { get; set; } = new();
    public long LogErrorPos { get; set; }
    public DateTime? StatsAt { get; set; }
    public int BackupFiles { get; set; }
    public long BackupBytes { get; set; }
    public int SourceFiles { get; set; }
    public long PendingBytes { get; set; }
    public int PendingCount { get; set; }
    public int ExtraCount { get; set; }
    public bool? InSync { get; set; }

    public void ApplyDiff(DiffResult d, DateTime now)
    {
        StatsAt = now;
        BackupFiles = d.DestFiles;
        BackupBytes = d.DestBytes;
        SourceFiles = d.SourceFiles;
        PendingBytes = d.PendingBytes;
        PendingCount = d.MissingCount;
        ExtraCount = d.ExtraCount;
        InSync = d.InSync;
    }
}

public sealed class AppState
{
    public Dictionary<string, PairState> Pairs { get; set; } = new();
    public DateTime? LastCleanup { get; set; }
    public DateTime? LastUpdateCheck { get; set; }
    public bool TrayHintShown { get; set; }

    public PairState For(string pairId)
    {
        if (!Pairs.TryGetValue(pairId, out var s)) Pairs[pairId] = s = new PairState();
        return s;
    }
}

public static class StateStore
{
    public static AppState Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppState>(File.ReadAllText(path, Encoding.UTF8), ConfigStore.Json) ?? new AppState();
        }
        catch { }
        return new AppState();
    }

    public static void Save(AppState state, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, ConfigStore.Json), new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }
        catch { }
    }
}

public sealed record DiskSpace(long Free, long Total)
{
    public double FreeRatio => Total > 0 ? (double)Free / Total : 0;
}

public static class Disk
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceEx(string dir, out ulong freeForUser, out ulong total, out ulong totalFree);

    public static DiskSpace? Get(string path)
    {
        var dir = PathUtil.Normalize(path);
        while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir) ?? "";
        if (string.IsNullOrEmpty(dir)) return null;
        if (!dir.EndsWith("\\")) dir += "\\";
        return GetDiskFreeSpaceEx(dir, out var free, out var total, out _) ? new DiskSpace((long)free, (long)total) : null;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{v:0.#} {units[i]}";
    }
}

public enum AlertKind { Disconnected, SourceMissing, LowSpace, CopyErrors }

public sealed record Alert(string PairId, AlertKind Kind, string Title, string Message);

public static class HealthMonitor
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromHours(24);
    public static readonly TimeSpan ErrorCooldown = TimeSpan.FromHours(1);

    public static bool IsLowSpace(DiskSpace d, long pendingBytes) =>
        d.Total > 0 && (d.FreeRatio < 0.10 || d.Free < pendingBytes);

    public static List<Alert> Check(SyncConfig config, AppState state, DateTime now,
        Func<SyncPair, Availability> availability, Func<SyncPair, DiskSpace?> disk, Func<SyncPair, int> newErrors)
    {
        var alerts = new List<Alert>();
        if (config.Settings.Paused) return alerts;
        var limit = TimeSpan.FromHours(config.Settings.DisconnectedAlertHours);
        foreach (var p in config.Pairs)
        {
            if (!p.AutoSync) continue;
            var st = state.For(p.Id);
            var a = availability(p);
            if (a == Availability.Ok)
            {
                st.LastAvailable = now;
                var d = disk(p);
                if (d is not null && IsLowSpace(d, st.PendingBytes) && Due(st, AlertKind.LowSpace, now, Cooldown))
                    alerts.Add(new Alert(p.Id, AlertKind.LowSpace, L.T("alert.lowSpace.title", p.Name),
                        L.T("alert.lowSpace.text", Disk.FormatBytes(d.Free), PathUtil.DriveRoot(p.Backup) ?? p.Backup)));
                var errs = newErrors(p);
                if (errs > 0 && Due(st, AlertKind.CopyErrors, now, ErrorCooldown))
                    alerts.Add(new Alert(p.Id, AlertKind.CopyErrors, L.T("alert.errors.title", p.Name), L.T("alert.errors.text", errs)));
                continue;
            }
            st.LastAvailable ??= now;
            if (now - st.LastAvailable.Value < limit) continue;
            var kind = a == Availability.BackupMissing ? AlertKind.Disconnected : AlertKind.SourceMissing;
            if (!Due(st, kind, now, Cooldown)) continue;
            var hours = (int)(now - st.LastAvailable.Value).TotalHours;
            alerts.Add(kind == AlertKind.Disconnected
                ? new Alert(p.Id, kind, L.T("alert.disconnected.title", p.Name), L.T("alert.disconnected.text", hours))
                : new Alert(p.Id, kind, L.T("alert.sourceMissing.title", p.Name), L.T("alert.sourceMissing.text", hours)));
        }
        return alerts;
    }

    private static bool Due(PairState st, AlertKind kind, DateTime now, TimeSpan cooldown)
    {
        var key = kind.ToString();
        if (st.LastAlerts.TryGetValue(key, out var last) && now - last < cooldown) return false;
        st.LastAlerts[key] = now;
        return true;
    }
}

public sealed record UpdateInfo(string Version, string Url);

// Solo para instalaciones fuera de la Store: la Store actualiza la app por su cuenta.
public static class UpdateChecker
{
    public static bool IsNewer(string? remote, string current) =>
        System.Version.TryParse(remote, out var r) && System.Version.TryParse(current, out var c) && r > c;

    public static UpdateInfo? Parse(string json)
    {
        try
        {
            var n = JsonNode.Parse(json);
            var v = n?["version"]?.GetValue<string>();
            var u = n?["url"]?.GetValue<string>() ?? "";
            return string.IsNullOrWhiteSpace(v) ? null : new UpdateInfo(v, u);
        }
        catch { return null; }
    }

    public static async Task<UpdateInfo?> CheckAsync(string url, string currentVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var info = Parse(await http.GetStringAsync(uri, ct).ConfigureAwait(false));
            return info is not null && IsNewer(info.Version, currentVersion) ? info : null;
        }
        catch { return null; }
    }
}
