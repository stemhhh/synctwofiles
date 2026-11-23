using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App;

public sealed class AppController
{
    private static readonly TimeSpan StatsMaxAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromHours(6);
    private static readonly TimeSpan UpdateEvery = TimeSpan.FromDays(1);

    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _monitorTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _statusBusy;
    private bool _monitorBusy;

    public AppController(AppPaths paths, SyncConfig config, bool imported)
    {
        Paths = paths;
        Config = config;
        Imported = imported;
        State = StateStore.Load(paths.StateFile);
        Sync = new AutoSyncManager(paths.LogDir);
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _monitorTimer.Tick += (_, _) => MonitorTick();
    }

    public AppPaths Paths { get; }
    public SyncConfig Config { get; }
    public AppState State { get; }
    public AutoSyncManager Sync { get; }
    public bool Imported { get; }
    public Dictionary<string, bool> Running { get; private set; } = new();
    public HashSet<string> Measuring { get; } = new();
    public bool StatusKnown { get; private set; }

    public event Action? StatusChanged;
    public event Action<string>? StatsChanged;
    public event Action<Alert>? AlertRaised;
    public event Action<UpdateInfo>? UpdateAvailable;

    public void Start()
    {
        _statusTimer.Start();
        _monitorTimer.Start();
        RefreshStatus();
        _ = InitialStatsAsync();
        MonitorTick();
    }

    public string? SaveConfig()
    {
        try { ConfigStore.Save(Config, Paths.ConfigFile); return null; }
        catch (Exception ex) { return L.T("err.save", ex.Message); }
    }

    public void SaveState() => StateStore.Save(State, Paths.StateFile);

    private SyncConfig Snapshot() => new()
    {
        Settings = Config.Settings,
        Pairs = Config.Pairs.Select(Clone).ToList(),
    };

    public static SyncPair Clone(SyncPair p) => new()
    {
        Id = p.Id, Name = p.Name, Source = p.Source, Backup = p.Backup, Locked = p.Locked,
        AutoSync = p.AutoSync, CreatedAt = p.CreatedAt, Excludes = p.Excludes.ToList(),
    };

    // Tambien reanuda las sincronizaciones que se detuvieron (p. ej. al reconectar un disco).
    public async void RefreshStatus()
    {
        if (_statusBusy) return;
        _statusBusy = true;
        try
        {
            var snap = Snapshot();
            Running = await Task.Run(() =>
            {
                var procs = RoboProcesses.List();
                if (Sync.EnsureAll(snap, procs) > 0)
                {
                    Thread.Sleep(400);
                    procs = RoboProcesses.List();
                }
                return snap.Pairs.ToDictionary(p => p.Id, p => Sync.ProcessesFor(p, procs).Count > 0);
            });
            StatusKnown = true;
        }
        catch { }
        finally { _statusBusy = false; }
        StatusChanged?.Invoke();
    }

    public bool IsRunning(string id) => Running.TryGetValue(id, out var r) && r;

    public async Task RefreshStatsAsync(string pairId)
    {
        var p = Config.Get(pairId);
        if (p is null || !Measuring.Add(pairId)) return;
        StatsChanged?.Invoke(pairId);
        try
        {
            var clone = Clone(p);
            if (AutoSyncManager.GetAvailability(clone) == Availability.Ok)
            {
                var d = await Task.Run(() => TreeCompare.Compare(clone.Source, clone.Backup, clone.Rules));
                State.For(pairId).ApplyDiff(d, DateTime.Now);
                SaveState();
            }
        }
        catch { }
        finally
        {
            Measuring.Remove(pairId);
            StatsChanged?.Invoke(pairId);
        }
    }

    private async Task InitialStatsAsync()
    {
        foreach (var p in Config.Pairs.ToList())
        {
            var st = State.For(p.Id);
            if (st.StatsAt is null || DateTime.Now - st.StatsAt.Value > StatsMaxAge)
                await RefreshStatsAsync(p.Id);
        }
        // The first automatic pass usually catches up on what was pending when the app last closed.
        await Task.Delay(TimeSpan.FromSeconds(90));
        foreach (var p in Config.Pairs.ToList())
            if (State.For(p.Id).InSync == false && !Config.Settings.Paused && p.AutoSync)
                await RefreshStatsAsync(p.Id);
    }

    public void SetPaused(bool paused)
    {
        Config.Settings.Paused = paused;
        SaveConfig();
        var snap = Snapshot();
        if (paused) Task.Run(() => Sync.StopAll(snap)).ContinueWith(_ => RefreshStatus(), TaskScheduler.FromCurrentSynchronizationContext());
        else RefreshStatus();
    }

    private int NewErrors(SyncPair p)
    {
        var st = State.For(p.Id);
        var log = Sync.LogFor(p);
        var pos = st.LogErrorPos;
        if (pos == 0 && File.Exists(log))
        {
            // La primera vez solo se marca el final: no se avisa de errores viejos.
            var len = new FileInfo(log).Length;
            st.LogErrorPos = len - len % 2;
            return 0;
        }
        var n = HistoryLog.CountNewErrors(log, ref pos);
        st.LogErrorPos = pos;
        return n;
    }

    private async void MonitorTick()
    {
        if (_monitorBusy) return;
        _monitorBusy = true;
        try
        {
            var snap = Snapshot();
            var now = DateTime.Now;
            var alerts = await Task.Run(() => HealthMonitor.Check(snap, State, now,
                AutoSyncManager.GetAvailability, p => Disk.Get(p.Backup), NewErrors));
            foreach (var a in alerts) AlertRaised?.Invoke(a);

            if (State.LastCleanup is null || now - State.LastCleanup.Value > CleanupEvery)
            {
                State.LastCleanup = now;
                var days = Config.Settings.VersionRetentionDays;
                await Task.Run(() =>
                {
                    foreach (var p in snap.Pairs.Where(p => AutoSyncManager.GetAvailability(p) == Availability.Ok))
                        VersionStore.Cleanup(p, days, now);
                });
            }

            if (!Native.IsPackaged && !string.IsNullOrWhiteSpace(Config.Settings.UpdateUrl) &&
                (State.LastUpdateCheck is null || now - State.LastUpdateCheck.Value > UpdateEvery))
            {
                State.LastUpdateCheck = now;
                var info = await UpdateChecker.CheckAsync(Config.Settings.UpdateUrl, AppInfo.Version);
                if (info is not null) UpdateAvailable?.Invoke(info);
            }
            SaveState();
        }
        catch { }
        finally { _monitorBusy = false; }
    }
}
