using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App.Views;

public sealed record EntryRow(string Name, string RelPath, bool IsDir, string Detail)
{
    public string Glyph => IsDir ? "\uE8B7" : "\uE8A5";
    public string Color => IsDir ? "#409CFF" : "#AEAEB2";
}

// Explorador del backup para restaurar un solo archivo o carpeta.
public partial class RestoreItemDialog : OverlayDialog
{
    private readonly SyncPair _pair;
    private readonly ExcludeRules _rules;
    private string _rel = "";

    public RestoreItemDialog(SyncPair pair)
    {
        InitializeComponent();
        _pair = pair;
        _rules = pair.Rules;
        Up.Click += (_, _) => Navigate(Path.GetDirectoryName(_rel) ?? "");
        Cancel.Click += (_, _) => Close(false);
        Ok.Click += (_, _) => Accept();
        Entries.SelectionChanged += (_, _) => Ok.IsEnabled = Entries.SelectedItem is EntryRow;
        Entries.MouseDoubleClick += (_, e) =>
        {
            if (Entries.SelectedItem is EntryRow { IsDir: true } r) { Navigate(r.RelPath); e.Handled = true; }
        };
        Entries.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Entries.SelectedItem is EntryRow { IsDir: true } r) { Navigate(r.RelPath); e.Handled = true; }
            else if (e.Key == Key.Back) { Navigate(Path.GetDirectoryName(_rel) ?? ""); e.Handled = true; }
        };
        Navigate("");
    }

    public string? RelPath { get; private set; }
    public bool IsDir { get; private set; }
    public bool SkipNewerFiles => SkipNewer.IsChecked == true;

    public override void OnShown() => Entries.Focus();

    private void Navigate(string rel)
    {
        _rel = rel;
        Up.IsEnabled = rel.Length > 0;
        Crumb.Text = rel.Length == 0 ? PathUtil.Label(_pair.Backup) : PathUtil.Label(_pair.Backup) + "  ›  " + rel.Replace("\\", "  ›  ");
        var rows = new List<EntryRow>();
        try
        {
            var dir = new DirectoryInfo(Path.Combine(_pair.Backup, rel));
            var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
            rows.AddRange(dir.EnumerateDirectories("*", opts)
                .Where(d => !TreeCompare.IsLink(d) && !_rules.IsDirExcluded(d.Name))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(d => new EntryRow(d.Name, Join(rel, d.Name), true, d.LastWriteTime.ToString("g"))));
            rows.AddRange(dir.EnumerateFiles("*", opts)
                .Where(f => !TreeCompare.IsLink(f) && !_rules.IsFileExcluded(f.Name))
                .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(f => new EntryRow(f.Name, Join(rel, f.Name), false, $"{Disk.FormatBytes(f.Length)}   {f.LastWriteTime:g}")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Entries.ItemsSource = rows;
        Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Ok.IsEnabled = false;
    }

    private static string Join(string rel, string name) => rel.Length == 0 ? name : rel + "\\" + name;

    private void Accept()
    {
        if (Entries.SelectedItem is not EntryRow r) return;
        RelPath = r.RelPath;
        IsDir = r.IsDir;
        Close(true);
    }
}
