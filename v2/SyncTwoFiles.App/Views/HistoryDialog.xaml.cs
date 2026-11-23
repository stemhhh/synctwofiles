using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App.Views;

public partial class HistoryDialog : OverlayDialog
{
    private readonly string _log;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string _raw = "";

    public HistoryDialog(SyncPair pair, string logFile)
    {
        InitializeComponent();
        _log = logFile;
        Heading.Text = L.T("history.title", pair.Name);
        CloseBtn.Click += (_, _) => Close(false);
        Reload.Click += async (_, _) => await LoadAsync();
        Notepad.Click += (_, _) =>
        {
            if (!File.Exists(_log)) return;
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_log}\"") { UseShellExecute = true }); } catch { }
        };
        Query.TextChanged += (_, _) =>
        {
            QueryHint.Visibility = Query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _debounce.Stop();
            _debounce.Start();
        };
        _debounce.Tick += (_, _) => { _debounce.Stop(); ApplyFilter(); };
        ErrorsOnly.Click += (_, _) => ApplyFilter();
        Closed += _ => _debounce.Stop();
        Loaded += async (_, _) => await LoadAsync();
    }

    public override void OnShown() => Query.Focus();

    private async Task LoadAsync()
    {
        Empty.Text = L.T("common.loading");
        Empty.Visibility = Visibility.Visible;
        try { _raw = await Task.Run(() => HistoryLog.ReadTail(_log)); }
        catch (Exception ex) { _raw = ""; Empty.Text = ex.Message; return; }
        Notepad.IsEnabled = File.Exists(_log);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var lines = HistoryLog.Filter(_raw, Query.Text, ErrorsOnly.IsChecked == true).ToList();
        Text.Text = string.Join(Environment.NewLine, lines);
        Text.ScrollToEnd();
        Count.Text = L.T("history.lines", lines.Count);
        Empty.Text = _raw.Trim().Length == 0 ? L.T("history.none") : L.T("history.noMatch");
        Empty.Visibility = lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
