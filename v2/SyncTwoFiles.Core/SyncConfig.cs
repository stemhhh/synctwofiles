using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SyncTwoFiles.Core;

public sealed class SyncPair
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string Backup { get; set; } = "";
    public bool Locked { get; set; } = true;
    public bool AutoSync { get; set; } = true;
    public string CreatedAt { get; set; } = "";
    public List<string> Excludes { get; set; } = new();

    [JsonIgnore] public ExcludeRules Rules => new(Excludes);

    public static SyncPair Create(string name, string source, string backup, bool locked = true,
        bool autoSync = true, IEnumerable<string>? excludes = null) => new()
    {
        Name = name.Trim(),
        Source = PathUtil.Normalize(source),
        Backup = PathUtil.Normalize(backup),
        Locked = locked,
        AutoSync = autoSync,
        CreatedAt = DateTime.Now.ToString("s"),
        Excludes = ExcludeRules.Clean(excludes),
    };

    public void NormalizeInPlace()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString();
        Name = (Name ?? "").Trim();
        Source = PathUtil.Normalize(Source);
        Backup = PathUtil.Normalize(Backup);
        CreatedAt ??= "";
        Excludes = ExcludeRules.Clean(Excludes);
    }
}

public sealed class AppSettings
{
    public string Language { get; set; } = "";
    public int VersionRetentionDays { get; set; } = 30;
    public int DisconnectedAlertHours { get; set; } = 24;
    public bool StartWithWindows { get; set; } = true;
    public bool WelcomeShown { get; set; }
    public bool Paused { get; set; }
    public string UpdateUrl { get; set; } = "";

    public void Clamp()
    {
        VersionRetentionDays = Math.Clamp(VersionRetentionDays, 1, 3650);
        DisconnectedAlertHours = Math.Clamp(DisconnectedAlertHours, 1, 24 * 90);
        Language ??= "";
        UpdateUrl ??= "";
    }
}

public sealed class PairLockedException : InvalidOperationException
{
    public PairLockedException(string message) : base(message) { }
}

public sealed class SyncConfig
{
    public int Version { get; set; } = 2;
    public List<SyncPair> Pairs { get; set; } = new();
    public AppSettings Settings { get; set; } = new();

    public SyncPair? Get(string? id) => id is null ? null : Pairs.FirstOrDefault(p => p.Id == id);

    public void Add(SyncPair pair) => Pairs.Add(pair);

    public static void AssertUnlocked(SyncPair? pair)
    {
        if (pair is null) throw new InvalidOperationException(L.T("err.pairNotFound"));
        if (pair.Locked) throw new PairLockedException(L.T("err.locked", pair.Name));
    }

    public void Update(string id, string name, string source, string backup, IEnumerable<string>? excludes = null)
    {
        var p = Get(id);
        AssertUnlocked(p);
        p!.Name = name.Trim();
        p.Source = PathUtil.Normalize(source);
        p.Backup = PathUtil.Normalize(backup);
        if (excludes is not null) p.Excludes = ExcludeRules.Clean(excludes);
    }

    public void Remove(string id)
    {
        AssertUnlocked(Get(id));
        Pairs.RemoveAll(p => p.Id == id);
    }

    public void Lock(string id) { var p = Get(id); if (p is not null) p.Locked = true; }
    public void Unlock(string id) { var p = Get(id); if (p is not null) p.Locked = false; }

    public static bool UnlockConfirmation(string? expected, string? typed)
    {
        if (string.IsNullOrEmpty(expected) || typed is null) return false;
        return string.Equals(typed.Trim(), expected.Trim(), StringComparison.Ordinal);
    }
}
