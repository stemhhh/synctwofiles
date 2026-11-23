using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App.Views;

public sealed record VersionRow(VersionSession Session, string Title, string Detail, string Glyph, string Color);

public partial class VersionsDialog : OverlayDialog
{
    private readonly SyncPair _pair;
    private readonly Func<OverlayDialog, Task<bool>> _show;

    public VersionsDialog(SyncPair pair, int retentionDays, Func<OverlayDialog, Task<bool>> show)
    {
        InitializeComponent();
        _pair = pair;
        _show = show;
        Heading.Text = L.T("versions.title", pair.Name);
        Sub.Text = L.T("versions.sub", retentionDays);
        CloseBtn.Click += (_, _) => Close(false);
        OpenRoot.Click += (_, _) => OpenFolder(VersionStore.Root(_pair));
        Loaded += async (_, _) => await LoadAsync();
    }

    public VersionSession? Selected { get; private set; }
    public bool ReplaceCurrent => Replace.IsChecked == true;

    private async Task LoadAsync()
    {
        Empty.Text = L.T("common.loading");
        Empty.Visibility = Visibility.Visible;
        var sessions = await Task.Run(() => VersionStore.List(_pair));
        Rows.ItemsSource = sessions.Select(ToRow).ToList();
        Empty.Text = L.T("versions.empty");
        Empty.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OpenRoot.IsEnabled = Directory.Exists(VersionStore.Root(_pair));
    }

    private static VersionRow ToRow(VersionSession s)
    {
        var (glyph, color, kind) = s.Kind switch
        {
            VersionStore.KindMirror => ("\uE8C8", "#FF9F0A", L.T("versions.kindMirror")),
            VersionStore.KindRestore => ("\uE777", "#5E5CE6", L.T("versions.kindRestore")),
            _ => ("\uE8E5", "#0A84FF", L.T("versions.kindItem")),
        };
        return new VersionRow(s, s.Date.ToString("f"), $"{kind}  ·  {L.T("versions.files", s.Files)}  ·  {Disk.FormatBytes(s.Bytes)}", glyph, color);
    }

    private static void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); } catch { }
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is VersionRow r) OpenFolder(r.Session.Path);
    }

    private void OnRecover(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not VersionRow r) return;
        Selected = r.Session;
        Close(true);
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not VersionRow r) return;
        var ok = await _show(new ConfirmDialog(L.T("versions.deleteTitle"), L.T("versions.deleteText", r.Title, r.Session.Files),
            L.T("common.delete"), destructive: true));
        if (!ok) return;
        await Task.Run(() => VersionStore.Delete(r.Session.Path));
        await LoadAsync();
        Focus();
    }
}
