using System;
using System.Linq;
using System.Threading;
using System.Windows;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App;

public partial class App : Application
{
    private const string MutexName = @"Local\SyncTwoFiles.v2";
    private const string ShowEventName = @"Local\SyncTwoFiles.v2.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private TrayIcon? _tray;
    private MainWindow? _main;
    private AppController? _controller;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var background = e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase) || StartupHelper.LaunchedByStartupTask();

        _mutex = new Mutex(true, MutexName, out var created);
        if (!created && e.Args.Contains("--restarted", StringComparer.OrdinalIgnoreCase))
        {
            try { created = _mutex.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { created = true; }
        }
        if (!created)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }
        if (!Native.IsPackaged) Native.SetCurrentProcessExplicitAppUserModelID(AppInfo.AppUserModelId);

        var paths = AppPaths.Default();
        SyncConfig config;
        bool imported;
        try { (config, imported) = ConfigStore.LoadOrImport(paths); }
        catch (Exception ex)
        {
            L.Language = "";
            MessageBox.Show(ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        L.Language = config.Settings.Language;

        DispatcherUnhandledException += (_, ev) =>
        {
            ev.Handled = true;
            _main?.WriteOut(L.T("err.unexpected", ev.Exception.Message));
        };

        _controller = new AppController(paths, config, imported);
        _tray = new TrayIcon(_controller);
        _tray.OpenRequested += id => ShowMain(id);
        _tray.ExitRequested += ExitApp;
        _controller.AlertRaised += a =>
        {
            _tray.Notify(a.Title, a.Message, a.PairId);
            _main?.WriteOut($"⚠ {a.Title}: {a.Message}", a.PairId);
        };
        _controller.UpdateAvailable += u => _tray.Notify(L.T("update.title"), L.T("update.text", u.Version), null, false);

        _main = new MainWindow(_controller);
        _main.HideToTrayRequested += HideMain;

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                if (_exiting) return;
                Dispatcher.BeginInvoke(() => ShowMain(null));
            }
        }) { IsBackground = true };
        listener.Start();

        _ = StartupHelper.ApplyAsync(config.Settings.StartWithWindows);
        _controller.Start();
        if (!background) ShowMain(null);
    }

    private void ShowMain(string? pairId)
    {
        if (_main is null) return;
        _main.ShowAndActivate(pairId);
    }

    private void HideMain()
    {
        if (_main is null || _controller is null) return;
        _main.Hide();
        if (!_controller.State.TrayHintShown)
        {
            _controller.State.TrayHintShown = true;
            _controller.SaveState();
            _tray?.Notify(AppInfo.Name, L.T("tray.hint"), null, false);
        }
    }

    public void Restart()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--restarted") { UseShellExecute = false });
        }
        catch { return; }
        ExitApp();
    }

    private void ExitApp()
    {
        _exiting = true;
        _controller?.SaveState();
        _tray?.Dispose();
        _tray = null;
        _main?.ForceClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        _tray?.Dispose();
        try { _showEvent?.Set(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
