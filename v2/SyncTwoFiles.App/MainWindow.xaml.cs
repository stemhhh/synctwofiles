using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SyncTwoFiles.App.Views;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App;

public partial class MainWindow : Window
{
    private const int MaxOutChars = 150_000;

    private readonly AppController _c;
    private readonly ObservableCollection<PairItem> _items = new();
    private readonly Dictionary<string, StringBuilder> _buffers = new();
    private readonly DoubleAnimation _barAnim;
    private string? _selectedId;
    private bool _busy;
    private bool _forceClose;
    private bool _rendered;
    private int _statusHold;

    public MainWindow(AppController controller)
    {
        InitializeComponent();
        _c = controller;
        VersionText.Text = $"{L.T("app.version", AppInfo.Version)}  ·  © 2026 {AppInfo.Author}";
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);
        PairList.ItemsSource = _items;

        _barAnim = new DoubleAnimation(-140, 900, TimeSpan.FromSeconds(1.4))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };

        SourceInitialized += (_, _) => Native.ApplyDarkFrame(new WindowInteropHelper(this).Handle);
        StateChanged += (_, _) => UpdateMaxButton();
        Closing += (_, e) =>
        {
            if (_forceClose) return;
            e.Cancel = true;
            HideToTrayRequested?.Invoke();
        };
        KeyDown += (_, e) => { if (e.Key == Key.F11) ToggleMaximize(); };
        ContentRendered += (_, _) => OnFirstRender();

        BtnMin.Click += (_, _) => WindowState = WindowState.Minimized;
        BtnMax.Click += (_, _) => ToggleMaximize();
        BtnClose.Click += (_, _) => Close();

        PairList.SelectionChanged += (_, _) =>
        {
            if (PairList.SelectedItem is PairItem it && it.Id != _selectedId) { _selectedId = it.Id; UpdateDetail(); }
        };
        BtnAdd.Click += (_, _) => AddOrEdit(null);
        BtnAddEmpty.Click += (_, _) => AddOrEdit(null);
        BtnEdit.Click += (_, _) => { if (Selected is { Locked: false } p) AddOrEdit(p); };
        BtnLock.Click += (_, _) => ToggleLock();
        BtnDelete.Click += (_, _) => DeletePair();
        BtnOpenSrc.Click += (_, _) => { if (Selected is { } p) OpenFolder(p.Source); };
        BtnOpenDst.Click += (_, _) => { if (Selected is { } p) OpenFolder(p.Backup); };
        AutoSwitch.Click += (_, _) => ToggleAuto();
        BtnMirror.Click += (_, _) => Mirror();
        BtnRestore.Click += (_, _) => RestoreAll();
        BtnRestoreItem.Click += (_, _) => RestoreItem();
        BtnVerify.Click += (_, _) => Verify();
        BtnVersions.Click += (_, _) => ShowVersions();
        BtnLog.Click += (_, _) => { if (Selected is { } p) _ = ShowDialogAsync(new HistoryDialog(p, _c.Sync.LogFor(p))); };
        BtnClear.Click += (_, _) => { if (_selectedId is { } id) _buffers[id] = new StringBuilder(); Out.Clear(); };
        BtnPause.Click += (_, _) => TogglePause();
        BtnSettings.Click += (_, _) => ShowSettings();

        _c.StatusChanged += OnStatus;
        _c.StatsChanged += id => { if (id == _selectedId) UpdateStats(); };
        RebuildList();
        UpdatePauseUi();
    }

    public event Action? HideToTrayRequested;

    private SyncPair? Selected => _c.Config.Get(_selectedId);

    // ------------------------------------------------------------ ventana

    public void ShowAndActivate(string? pairId)
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        if (pairId is not null && _c.Config.Get(pairId) is not null) Select(pairId);
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateMaxButton()
    {
        var max = WindowState == WindowState.Maximized;
        Root.Margin = new Thickness(max ? 8 : 0);
        BtnMax.Content = max ? "\uE923" : "\uE922";
        BtnMax.ToolTip = max ? L.T("win.restore") : L.T("win.max");
    }

    private async void OnFirstRender()
    {
        if (_rendered) return;
        _rendered = true;
        foreach (var p in _c.Config.Pairs) WriteOut(L.T("app.ready", AppInfo.Name, AppInfo.Version), p.Id);
        if (_c.Imported)
            foreach (var p in _c.Config.Pairs) WriteOut(L.T("app.imported"), p.Id);
        if (!_c.Config.Settings.WelcomeShown && _c.Config.Pairs.Count == 0)
        {
            _c.Config.Settings.WelcomeShown = true;
            _c.SaveConfig();
            await ShowWelcome();
        }
    }

    private async Task ShowWelcome()
    {
        if (await ShowDialogAsync(new WelcomeDialog())) AddOrEdit(null);
    }

    public Task<bool> ShowDialogAsync(OverlayDialog dlg)
    {
        OverlayHost.Children.Add(dlg);
        dlg.Opacity = 0;
        dlg.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        dlg.Closed += d =>
        {
            OverlayHost.Children.Remove(d);
            if (OverlayHost.Children.Count > 0) (OverlayHost.Children[^1] as OverlayDialog)?.Focus();
        };
        dlg.Dispatcher.BeginInvoke(dlg.OnShown, System.Windows.Threading.DispatcherPriority.Input);
        return dlg.Result;
    }

    private Task<bool> Confirm(string title, string text, string ok, bool destructive = false, string? typeToConfirm = null, int countdown = 0) =>
        ShowDialogAsync(new ConfirmDialog(title, text, ok, destructive, typeToConfirm, countdown));

    // ------------------------------------------------------------ salida

    private void AddOut(string text, string? pairId)
    {
        if (pairId is null) return;
        if (!_buffers.TryGetValue(pairId, out var sb)) _buffers[pairId] = sb = new StringBuilder();
        sb.Append(text);
        var trimmed = false;
        if (sb.Length > MaxOutChars)
        {
            sb.Remove(0, sb.Length - (int)(MaxOutChars * 0.8));
            trimmed = true;
        }
        if (pairId != _selectedId) return;
        if (trimmed) Out.Text = sb.ToString();
        else Out.AppendText(text);
        Out.ScrollToEnd();
    }

    public void WriteOut(string msg, string? pairId = null) => AddOut(msg + "\r\n", pairId ?? _selectedId);

    // ------------------------------------------------------------ lista y detalle

    private void RebuildList()
    {
        _items.Clear();
        foreach (var p in _c.Config.Pairs)
        {
            var it = new PairItem(p.Id);
            it.Update(p, _c.IsRunning(p.Id), _c.Config.Settings.Paused);
            _items.Add(it);
        }
        if (Selected is null) _selectedId = _c.Config.Pairs.FirstOrDefault()?.Id;
        PairList.SelectedItem = _items.FirstOrDefault(i => i.Id == _selectedId);
        UpdateDetail();
    }

    private void Select(string id)
    {
        _selectedId = id;
        PairList.SelectedItem = _items.FirstOrDefault(i => i.Id == id);
        UpdateDetail();
    }

    private void UpdateDetail()
    {
        var p = Selected;
        EmptyView.Visibility = p is null ? Visibility.Visible : Visibility.Collapsed;
        DetailView.Visibility = p is null ? Visibility.Collapsed : Visibility.Visible;
        if (p is null) return;

        PairName.Text = p.Name;
        SrcPath.Text = p.Source;
        DstPath.Text = p.Backup;
        LockBadge.Visibility = p.Locked ? Visibility.Visible : Visibility.Collapsed;
        PairSub.Text = p.Locked ? L.T("detail.locked") : L.T("detail.unlocked");
        BtnEdit.Visibility = BtnDelete.Visibility = p.Locked ? Visibility.Collapsed : Visibility.Visible;
        BtnLock.Content = p.Locked ? L.T("detail.unlock") : L.T("detail.lock");
        ExcludesRow.Visibility = p.Excludes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ExcludesText.Text = string.Join("   ·   ", p.Excludes);

        Out.Text = _buffers.TryGetValue(p.Id, out var sb) ? sb.ToString() : "";
        Out.ScrollToEnd();
        UpdateSwitch();
        UpdateStats();
        UpdateLastCopy();
        UpdateDisk();
        _ = UpdateVersionsSubAsync(p);
    }

    private void UpdateSwitch()
    {
        if (Selected is not { } p) return;
        var on = _c.IsRunning(p.Id);
        AutoSwitch.IsChecked = p.AutoSync;
        if (_c.Config.Settings.Paused && p.AutoSync) { AutoStatus.Text = L.T("status.paused"); return; }
        if (on) { AutoStatus.Text = L.T("status.active"); return; }
        if (!p.AutoSync) { AutoStatus.Text = L.T("status.off"); return; }
        AutoStatus.Text = AutoSyncManager.GetAvailability(p) switch
        {
            Availability.SourceMissing => L.T("status.sourceMissing"),
            Availability.BackupMissing => L.T("status.backupMissing"),
            _ => _c.StatusKnown ? L.T("status.starting") : L.T("status.checking"),
        };
    }

    private void OnStatus()
    {
        var paused = _c.Config.Settings.Paused;
        foreach (var it in _items)
            if (_c.Config.Get(it.Id) is { } p) it.Update(p, _c.IsRunning(p.Id), paused);
        UpdatePauseUi();
        if (_statusHold > 0) _statusHold--;
        else UpdateSwitch();
        UpdateLastCopy();
        UpdateDisk();
    }

    private void UpdatePauseUi()
    {
        var paused = _c.Config.Settings.Paused;
        PausedBanner.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
        BtnPause.Content = paused ? "\uE768" : "\uE769";
        BtnPause.ToolTip = paused ? L.T("side.resume") : L.T("side.pause");
    }

    private static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return L.T("time.now");
        if (d.TotalMinutes < 60) return L.T("time.min", (int)d.TotalMinutes);
        if (d.TotalHours < 24) return L.T("time.hours", (int)d.TotalHours);
        return t.ToString("g");
    }

    private void UpdateLastCopy()
    {
        if (Selected is not { } p) return;
        var t = PairLog.LastActivity(_c.Sync.LogFor(p));
        LastCopyText.Text = t is null ? L.T("detail.noCopies") : L.T("detail.lastCopyAt", Ago(t.Value), t.Value.ToString("g"));
    }

    private void UpdateDisk()
    {
        if (Selected is not { } p) return;
        if (AutoSyncManager.GetAvailability(p) == Availability.BackupMissing)
        {
            DiskBar.Value = 0;
            DiskText.Text = L.T("disk.disconnected");
            return;
        }
        var d = Disk.Get(p.Backup);
        if (d is null) { DiskRow.Visibility = Visibility.Collapsed; return; }
        DiskRow.Visibility = Visibility.Visible;
        DiskBar.Value = 1 - d.FreeRatio;
        var low = HealthMonitor.IsLowSpace(d, _c.State.For(p.Id).PendingBytes);
        DiskBar.Foreground = new SolidColorBrush(low ? Color.FromRgb(0xFF, 0x45, 0x3A) : Color.FromRgb(0x0A, 0x84, 0xFF));
        DiskText.Text = L.T(low ? "disk.low" : "disk.free", Disk.FormatBytes(d.Free), Disk.FormatBytes(d.Total));
        DiskText.Foreground = new SolidColorBrush(low ? Color.FromRgb(0xFF, 0x69, 0x61) : Color.FromRgb(0x98, 0x98, 0x9D));
    }

    private void UpdateStats()
    {
        if (Selected is not { } p) return;
        var st = _c.State.For(p.Id);
        var measuring = _c.Measuring.Contains(p.Id);
        BtnVerify.IsEnabled = !measuring && !_busy;
        string glyph, color;
        if (measuring)
        {
            (glyph, color) = ("\uE895", "#0A84FF");
            CompareTitle.Text = L.T("verify.running");
            CompareText.Text = L.T("verify.runningSub");
        }
        else if (st.StatsAt is null)
        {
            (glyph, color) = ("\uE9D2", "#636366");
            CompareTitle.Text = L.T("verify.never");
            CompareText.Text = L.T("verify.neverSub");
        }
        else
        {
            var stats = L.T("verify.stats", st.BackupFiles.ToString("N0"), Disk.FormatBytes(st.BackupBytes), Ago(st.StatsAt.Value));
            if (st.InSync == true)
            {
                (glyph, color) = ("\uE73E", "#30D158");
                CompareTitle.Text = L.T("verify.ok");
                CompareText.Text = stats;
            }
            else
            {
                (glyph, color) = ("\uE7BA", "#FF9F0A");
                var parts = new List<string>();
                if (st.PendingCount > 0) parts.Add(L.T("verify.missing", st.PendingCount.ToString("N0"), Disk.FormatBytes(st.PendingBytes)));
                if (st.ExtraCount > 0) parts.Add(L.T("verify.extra", st.ExtraCount.ToString("N0")));
                CompareTitle.Text = string.Join(" · ", parts);
                CompareText.Text = stats;
            }
        }
        CompareGlyph.Text = glyph;
        CompareIcon.Background = (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    private async Task UpdateVersionsSubAsync(SyncPair p)
    {
        var clone = AppController.Clone(p);
        var count = await Task.Run(() =>
        {
            try { return AutoSyncManager.GetAvailability(clone) == Availability.Ok ? VersionStore.List(clone, measure: false).Count : 0; }
            catch { return 0; }
        });
        if (p.Id != _selectedId) return;
        VersionsSub.Text = count == 0
            ? L.T("versions.subNone", _c.Config.Settings.VersionRetentionDays)
            : L.T("versions.subCount", count, _c.Config.Settings.VersionRetentionDays);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var b in new[] { BtnMirror, BtnRestore, BtnRestoreItem, BtnVersions, BtnAdd, BtnAddEmpty, BtnEdit, BtnLock, BtnDelete })
            b.IsEnabled = !busy;
        AutoSwitch.IsEnabled = !busy;
        BtnVerify.IsEnabled = !busy && !(_selectedId is { } id && _c.Measuring.Contains(id));
        if (busy)
        {
            BarTrack.Visibility = Visibility.Visible;
            BarShift.BeginAnimation(TranslateTransform.XProperty, _barAnim);
        }
        else
        {
            BarShift.BeginAnimation(TranslateTransform.XProperty, null);
            BarTrack.Visibility = Visibility.Hidden;
        }
    }

    private static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); } catch { }
    }

    private bool CheckFolders(SyncPair p)
    {
        if (!Directory.Exists(p.Source)) { WriteOut(L.T("err.noSource", p.Source), p.Id); return false; }
        if (!Directory.Exists(p.Backup)) { WriteOut(L.T("err.noBackup", p.Backup), p.Id); return false; }
        return true;
    }

    private bool SaveConfig()
    {
        var err = _c.SaveConfig();
        if (err is null) return true;
        WriteOut(err);
        return false;
    }

    private async Task<int> RunRoboAsync(string pairId, List<string> args)
    {
        try
        {
            return await RoboRunner.RunAsync(args, text => Dispatcher.BeginInvoke(() => AddOut(text, pairId)));
        }
        catch (Exception ex)
        {
            WriteOut(L.T("err.robocopy", ex.Message), pairId);
            return 16;
        }
    }

    private void ShowResult(int code, string pairId)
    {
        WriteOut(RoboArgs.IsSuccess(code) ? L.T("job.ok") : L.T("job.fail", code), pairId);
    }

    private void Header(string titleKey, string pairId)
    {
        WriteOut("", pairId);
        WriteOut($"— {L.T(titleKey)}  {DateTime.Now:HH:mm:ss} —", pairId);
    }

    private static string ListPreview(IEnumerable<string> items, int total, int max = 12)
    {
        var shown = items.Take(max).ToList();
        var text = string.Join("\n", shown);
        if (total > shown.Count) text += "\n" + L.T("common.andMore", total - shown.Count);
        return text;
    }

    // ------------------------------------------------------------ editar, bloquear, eliminar

    private async void AddOrEdit(SyncPair? existing)
    {
        var dlg = new EditorDialog(_c.Config, existing);
        if (!await ShowDialogAsync(dlg)) return;

        SyncPair p;
        var restart = true;
        var remeasure = true;
        if (existing is not null)
        {
            var cur = _c.Config.Get(existing.Id);
            if (cur is null) return;
            var pathsChanged = !PathUtil.SamePath(cur.Source, dlg.Source) || !PathUtil.SamePath(cur.Backup, dlg.Backup);
            var exclChanged = !cur.Excludes.SequenceEqual(dlg.Excludes, StringComparer.OrdinalIgnoreCase);
            restart = pathsChanged || exclChanged || cur.AutoSync != dlg.AutoSync;
            remeasure = pathsChanged || exclChanged;
            if (restart)
            {
                var old = AppController.Clone(cur);
                await Task.Run(() => _c.Sync.Stop(old));
            }
            try { _c.Config.Update(cur.Id, dlg.PairName, dlg.Source, dlg.Backup, dlg.Excludes); }
            catch (Exception ex) { WriteOut(ex.Message); return; }
            cur.AutoSync = dlg.AutoSync;
            cur.Locked = dlg.Locked;
            if (remeasure) _c.State.For(cur.Id).StatsAt = null;
            p = cur;
        }
        else
        {
            p = SyncPair.Create(dlg.PairName, dlg.Source, dlg.Backup, dlg.Locked, dlg.AutoSync, dlg.Excludes);
            _c.Config.Add(p);
        }
        if (!SaveConfig()) return;
        _selectedId = p.Id;
        RebuildList();
        WriteOut(L.T("editor.saved"), p.Id);
        if (p.AutoSync && restart)
        {
            if (_c.Config.Settings.Paused) WriteOut(L.T("editor.pausedNote"), p.Id);
            else
            {
                var clone = AppController.Clone(p);
                var ok = await Task.Run(() => _c.Sync.Start(clone));
                WriteOut(ok ? L.T("editor.autoStarted") : L.T("editor.autoFailed"), p.Id);
                _statusHold = 2;
                _c.RefreshStatus();
            }
        }
        if (remeasure) _ = _c.RefreshStatsAsync(p.Id);
    }

    private async void ToggleLock()
    {
        if (Selected is not { } p) return;
        if (!p.Locked)
        {
            _c.Config.Lock(p.Id);
            if (SaveConfig()) WriteOut(L.T("lock.locked"), p.Id);
            RebuildList();
            return;
        }
        var id = p.Id;
        var name = p.Name;
        if (!await Confirm(L.T("unlock.t1", name), L.T("unlock.x1"), L.T("common.continue"))) { WriteOut(L.T("common.cancelled"), id); return; }
        if (!await Confirm(L.T("unlock.t2"), L.T("unlock.x2"), L.T("common.continue"), typeToConfirm: name)) { WriteOut(L.T("common.cancelled"), id); return; }
        if (!await Confirm(L.T("unlock.t3"), L.T("unlock.x3", name), L.T("unlock.ok"), destructive: true, countdown: 5)) { WriteOut(L.T("common.cancelled"), id); return; }
        _c.Config.Unlock(id);
        if (SaveConfig()) WriteOut(L.T("lock.unlocked"), id);
        RebuildList();
    }

    private async void DeletePair()
    {
        if (Selected is not { Locked: false } p) return;
        var id = p.Id;
        if (!await Confirm(L.T("delete.title", p.Name), L.T("delete.text"), L.T("common.delete"), destructive: true)) { WriteOut(L.T("common.cancelled"), id); return; }
        var cur = _c.Config.Get(id);
        if (cur is null) return;
        try { _c.Config.Remove(id); }
        catch (Exception ex) { WriteOut(ex.Message, id); return; }
        var clone = AppController.Clone(cur);
        await Task.Run(() => _c.Sync.Stop(clone));
        SaveConfig();
        _buffers.Remove(id);
        _c.State.Pairs.Remove(id);
        _c.SaveState();
        _selectedId = null;
        RebuildList();
        _c.RefreshStatus();
    }

    private async void ToggleAuto()
    {
        if (Selected is not { } p) return;
        var id = p.Id;
        var on = AutoSwitch.IsChecked == true;
        p.AutoSync = on;
        SaveConfig();
        var clone = AppController.Clone(p);
        if (on)
        {
            if (_c.Config.Settings.Paused) WriteOut(L.T("editor.pausedNote"), id);
            else if (await Task.Run(() => _c.Sync.Start(clone))) WriteOut(L.T("auto.on"), id);
            else WriteOut(L.T("auto.later"), id);
        }
        else
        {
            await Task.Run(() => _c.Sync.Stop(clone));
            WriteOut(L.T("auto.off"), id);
        }
        _statusHold = 2;
        _c.RefreshStatus();
        UpdateSwitch();
    }

    private void TogglePause()
    {
        var paused = !_c.Config.Settings.Paused;
        _c.SetPaused(paused);
        foreach (var p in _c.Config.Pairs) WriteOut(L.T(paused ? "pause.on" : "pause.off"), p.Id);
        UpdatePauseUi();
        UpdateSwitch();
    }

    // ------------------------------------------------------------ espejo, restaurar, comprobar

    private async void Mirror()
    {
        if (Selected is not { } p || _busy || !CheckFolders(p)) return;
        var id = p.Id;
        var clone = AppController.Clone(p);
        SetBusy(true);
        WriteOut(L.T("mirror.analyzing"), id);
        DiffResult d;
        try { d = await Task.Run(() => TreeCompare.Compare(clone.Source, clone.Backup, clone.Rules)); }
        catch (Exception ex)
        {
            WriteOut(L.T("mirror.analyzeFail", ex.Message), id);
            SetBusy(false);
            return;
        }
        SetBusy(false);
        _c.State.For(id).ApplyDiff(d, DateTime.Now);
        _c.SaveState();
        UpdateStats();
        if (d.InSync)
        {
            WriteOut(L.T("mirror.inSync"), id);
            return;
        }

        var moving = d.ExtraDirs.Concat(d.ExtraFiles).Concat(d.ChangedFiles.Select(c => c.Path)).ToList();
        string title, text, ok;
        if (moving.Count > 0)
        {
            title = L.T("mirror.versionTitle", moving.Count);
            text = L.T("mirror.versionText", ListPreview(moving, moving.Count), _c.Config.Settings.VersionRetentionDays);
            ok = L.T("mirror.versionOk");
        }
        else
        {
            title = L.T("mirror.copyTitle");
            text = L.T("mirror.copyText", d.NewFiles.Count.ToString("N0"), Disk.FormatBytes(d.PendingBytes));
            ok = L.T("sync.mirrorBtn");
        }
        if (!await Confirm(title, text, ok)) { WriteOut(L.T("common.cancelled"), id); return; }
        if (!CheckFolders(p)) return;

        SetBusy(true);
        Header("sync.mirror", id);
        if (moving.Count > 0)
        {
            var r = await Task.Run(() => VersionStore.PreserveBeforeMirror(clone, d, DateTime.Now));
            if (r.Count > 0) WriteOut(L.T("mirror.preserved", r.Count, Path.GetFileName(r.Session ?? "")), id);
            if (r.Errors.Count > 0)
            {
                foreach (var e in r.Errors.Take(10)) WriteOut("  " + e, id);
                WriteOut(L.T("mirror.preserveFail", r.Errors.Count), id);
                SetBusy(false);
                return;
            }
        }
        var code = await RunRoboAsync(id, RoboArgs.Mirror(clone));
        ShowResult(code, id);
        SetBusy(false);
        _ = UpdateVersionsSubAsync(p);
        _ = _c.RefreshStatsAsync(id);
    }

    private async void RestoreAll()
    {
        if (Selected is not { } p || _busy || !CheckFolders(p)) return;
        var id = p.Id;
        if (!await Confirm(L.T("restore.title"), L.T("restore.text", PathUtil.Label(p.Source)), L.T("sync.restoreBtn")))
        {
            WriteOut(L.T("common.cancelled"), id);
            return;
        }
        if (!CheckFolders(p)) return;
        var clone = AppController.Clone(p);
        SetBusy(true);
        Header("sync.restore", id);
        var pre = await Task.Run(() =>
        {
            var d = TreeCompare.Compare(clone.Backup, clone.Source, clone.Rules);
            var over = VersionStore.WouldOverwriteOnRestore(d, skipNewer: true);
            return VersionStore.PreserveBeforeRestore(clone, clone.Source, "", over, VersionStore.KindRestore, DateTime.Now);
        });
        if (!ReportPreserve(pre, id)) { SetBusy(false); return; }
        var code = await RunRoboAsync(id, RoboArgs.Restore(clone));
        ShowResult(code, id);
        SetBusy(false);
        _ = UpdateVersionsSubAsync(p);
    }

    private bool ReportPreserve(PreserveResult r, string id)
    {
        if (r.Count > 0) WriteOut(L.T("restore.preserved", r.Count), id);
        if (r.Errors.Count == 0) return true;
        foreach (var e in r.Errors.Take(10)) WriteOut("  " + e, id);
        WriteOut(L.T("restore.preserveFail", r.Errors.Count), id);
        return false;
    }

    private async void RestoreItem()
    {
        if (Selected is not { } p || _busy || !CheckFolders(p)) return;
        var id = p.Id;
        var dlg = new RestoreItemDialog(p);
        if (!await ShowDialogAsync(dlg) || dlg.RelPath is null) return;
        var rel = dlg.RelPath;
        var skipNewer = dlg.SkipNewerFiles;
        var clone = AppController.Clone(p);
        SetBusy(true);
        WriteOut("", id);
        WriteOut($"— {L.T("restoreItem.header", rel)}  {DateTime.Now:HH:mm:ss} —", id);
        List<string> args;
        PreserveResult pre;
        if (dlg.IsDir)
        {
            var from = Path.Combine(clone.Backup, rel);
            var to = Path.Combine(clone.Source, rel);
            pre = await Task.Run(() =>
            {
                var d = TreeCompare.Compare(from, to, clone.Rules);
                var over = VersionStore.WouldOverwriteOnRestore(d, skipNewer);
                return VersionStore.PreserveBeforeRestore(clone, to, rel, over, VersionStore.KindItem, DateTime.Now);
            });
            args = RoboArgs.RestoreDir(from, to, skipNewer, clone.Rules);
        }
        else
        {
            var dirRel = Path.GetDirectoryName(rel) ?? "";
            var name = Path.GetFileName(rel);
            var from = Path.Combine(clone.Backup, dirRel);
            var to = Path.Combine(clone.Source, dirRel);
            pre = await Task.Run(() =>
            {
                var src = new FileInfo(Path.Combine(from, name));
                var dst = new FileInfo(Path.Combine(to, name));
                var over = new List<string>();
                if (dst.Exists && src.Exists &&
                    (src.Length != dst.Length || (src.LastWriteTimeUtc - dst.LastWriteTimeUtc).Duration() > TreeCompare.TimeTolerance) &&
                    !(skipNewer && src.LastWriteTimeUtc < dst.LastWriteTimeUtc - TreeCompare.TimeTolerance))
                    over.Add(name);
                return VersionStore.PreserveBeforeRestore(clone, to, dirRel, over, VersionStore.KindItem, DateTime.Now);
            });
            args = RoboArgs.RestoreFile(from, to, name, skipNewer);
        }
        if (!ReportPreserve(pre, id)) { SetBusy(false); return; }
        var code = await RunRoboAsync(id, args);
        ShowResult(code, id);
        SetBusy(false);
        _ = UpdateVersionsSubAsync(p);
    }

    private async void Verify()
    {
        if (Selected is not { } p) return;
        var id = p.Id;
        if (AutoSyncManager.GetAvailability(p) != Availability.Ok) { CheckFolders(p); return; }
        WriteOut(L.T("verify.start"), id);
        await _c.RefreshStatsAsync(id);
        var st = _c.State.For(id);
        if (st.StatsAt is null) return;
        if (st.InSync == true)
        {
            WriteOut(L.T("verify.okOut", st.SourceFiles.ToString("N0")), id);
            return;
        }
        if (st.PendingCount > 0)
        {
            WriteOut(L.T("verify.missingOut", st.PendingCount.ToString("N0"), Disk.FormatBytes(st.PendingBytes)), id);
            if (_c.IsRunning(id)) WriteOut(L.T("verify.autoWillCopy"), id);
        }
        if (st.ExtraCount > 0) WriteOut(L.T("verify.extraOut", st.ExtraCount.ToString("N0")), id);
    }

    private async void ShowVersions()
    {
        if (Selected is not { } p || _busy) return;
        if (AutoSyncManager.GetAvailability(p) != Availability.Ok) { CheckFolders(p); return; }
        var id = p.Id;
        var dlg = new VersionsDialog(p, _c.Config.Settings.VersionRetentionDays, ShowDialogAsync);
        var picked = await ShowDialogAsync(dlg);
        _ = UpdateVersionsSubAsync(p);
        if (!picked || dlg.Selected is not { } s) return;
        var replace = dlg.ReplaceCurrent;
        var text = replace ? L.T("recover.textReplace", PathUtil.Label(p.Source)) : L.T("recover.text", PathUtil.Label(p.Source));
        if (!await Confirm(L.T("recover.title", s.Date.ToString("f")), text, L.T("versions.recover"))) { WriteOut(L.T("common.cancelled"), id); return; }
        if (!CheckFolders(p)) return;
        var clone = AppController.Clone(p);
        SetBusy(true);
        WriteOut("", id);
        WriteOut($"— {L.T("recover.header", s.Date.ToString("g"))}  {DateTime.Now:HH:mm:ss} —", id);
        if (replace)
        {
            var pre = await Task.Run(() =>
            {
                var d = TreeCompare.Compare(s.Path, clone.Source, new ExcludeRules());
                var over = VersionStore.WouldOverwriteOnRestore(d, skipNewer: false);
                return VersionStore.PreserveBeforeRestore(clone, clone.Source, "", over, VersionStore.KindRestore, DateTime.Now);
            });
            if (!ReportPreserve(pre, id)) { SetBusy(false); return; }
        }
        var code = await RunRoboAsync(id, RoboArgs.RecoverVersion(s.Path, clone.Source, skipNewer: !replace));
        ShowResult(code, id);
        SetBusy(false);
        _ = UpdateVersionsSubAsync(p);
    }

    // ------------------------------------------------------------ ajustes

    private async void ShowSettings()
    {
        var dlg = new SettingsDialog(_c.Config.Settings, _c.Paths.DataDir);
        var saved = await ShowDialogAsync(dlg);
        if (dlg.WelcomeRequested) { await ShowWelcome(); return; }
        if (!saved) return;
        SaveConfig();
        if (dlg.StartupChanged) await StartupHelper.ApplyAsync(_c.Config.Settings.StartWithWindows);
        UpdateDetail();
        if (dlg.LanguageChanged &&
            await Confirm(L.T("settings.restartTitle"), L.T("settings.restartText"), L.T("settings.restartOk")))
            ((App)Application.Current).Restart();
    }
}
