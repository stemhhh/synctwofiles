using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App.Views;

public partial class EditorDialog : OverlayDialog
{
    private readonly SyncConfig _config;
    private readonly string? _editingId;

    public EditorDialog(SyncConfig config, SyncPair? pair)
    {
        InitializeComponent();
        _config = config;
        _editingId = pair?.Id;
        EdTitle.Text = pair is null ? L.T("editor.newTitle") : L.T("editor.editTitle");
        EdName.Text = pair?.Name ?? "";
        EdSrc.Text = pair?.Source ?? "";
        EdDst.Text = pair?.Backup ?? "";
        EdExcludes.Text = pair is null ? "" : string.Join(Environment.NewLine, pair.Excludes);
        EdAuto.IsChecked = pair?.AutoSync ?? true;
        EdLock.IsChecked = pair?.Locked ?? true;

        EdPickSrc.Click += (_, _) =>
        {
            var r = FolderPicker.Pick(Owner, L.T("editor.pickSource"), EdSrc.Text);
            if (r is null) return;
            EdSrc.Text = PathUtil.Normalize(r);
            if (EdName.Text.Trim().Length == 0) EdName.Text = PathUtil.Label(r);
        };
        EdPickDst.Click += (_, _) =>
        {
            var r = FolderPicker.Pick(Owner, L.T("editor.pickBackup"), EdDst.Text);
            if (r is not null) EdDst.Text = PathUtil.Normalize(r);
        };
        EdCancel.Click += (_, _) => Close(false);
        EdSave.Click += (_, _) => Save();
    }

    public string PairName => EdName.Text.Trim();
    public string Source => PathUtil.Normalize(EdSrc.Text);
    public string Backup => PathUtil.Normalize(EdDst.Text);
    public List<string> Excludes => ExcludeRules.Parse(EdExcludes.Text);
    public bool AutoSync => EdAuto.IsChecked == true;
    public bool Locked => EdLock.IsChecked == true;

    private IntPtr Owner => Window.GetWindow(this) is { } w ? new WindowInteropHelper(w).Handle : IntPtr.Zero;

    public override void OnShown() => EdName.Focus();

    private void Save()
    {
        var err = Validation.ValidatePair(EdName.Text, EdSrc.Text, EdDst.Text, _config.Pairs, _editingId, Excludes);
        if (err is null)
        {
            try { Directory.CreateDirectory(Backup); }
            catch (Exception ex) { err = L.T("err.createBackup", ex.Message); }
        }
        if (err is not null)
        {
            EdError.Text = err;
            EdError.Visibility = Visibility.Visible;
            return;
        }
        Close(true);
    }
}
