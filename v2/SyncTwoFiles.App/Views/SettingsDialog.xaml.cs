using System;
using System.Diagnostics;
using System.Windows;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App.Views;

public partial class SettingsDialog : OverlayDialog
{
    private readonly AppSettings _settings;
    private readonly string _dataDir;

    public SettingsDialog(AppSettings settings, string dataDir)
    {
        InitializeComponent();
        _settings = settings;
        _dataDir = dataDir;
        (settings.Language switch { "es" => LangEs, "en" => LangEn, _ => LangAuto }).IsChecked = true;
        Retention.Text = settings.VersionRetentionDays.ToString();
        AlertHours.Text = settings.DisconnectedAlertHours.ToString();
        StartWithWindows.IsChecked = settings.StartWithWindows;
        UpdateUrl.Text = settings.UpdateUrl;
        var packaged = Native.IsPackaged;
        UpdatePanel.Visibility = packaged ? Visibility.Collapsed : Visibility.Visible;
        StoreNote.Visibility = packaged ? Visibility.Visible : Visibility.Collapsed;
        AboutTitle.Text = $"{AppInfo.Name} {AppInfo.Version}  ·  © 2026 {AppInfo.Author}";

        Cancel.Click += (_, _) => Close(false);
        Save.Click += (_, _) => DoSave();
        ShowWelcome.Click += (_, _) => { WelcomeRequested = true; Close(false); };
        OpenData.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_dataDir}\"") { UseShellExecute = true }); } catch { }
        };
        CheckNow.Click += async (_, _) =>
        {
            CheckNow.IsEnabled = false;
            UpdateResult.Text = L.T("settings.checking");
            var info = await UpdateChecker.CheckAsync(UpdateUrl.Text.Trim(), AppInfo.Version);
            UpdateResult.Text = info is null ? L.T("settings.upToDate") : L.T("update.text", info.Version);
            CheckNow.IsEnabled = true;
        };
    }

    public bool WelcomeRequested { get; private set; }
    public bool LanguageChanged { get; private set; }
    public bool StartupChanged { get; private set; }

    private void DoSave()
    {
        if (!int.TryParse(Retention.Text.Trim(), out var days) || days < 1 || days > 3650)
        {
            ShowError(L.T("settings.errRetention"));
            return;
        }
        if (!int.TryParse(AlertHours.Text.Trim(), out var hours) || hours < 1 || hours > 24 * 90)
        {
            ShowError(L.T("settings.errHours"));
            return;
        }
        var url = UpdateUrl.Text.Trim();
        if (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps))
        {
            ShowError(L.T("settings.errUrl"));
            return;
        }
        var lang = LangEs.IsChecked == true ? "es" : LangEn.IsChecked == true ? "en" : "";
        LanguageChanged = lang != _settings.Language;
        StartupChanged = (StartWithWindows.IsChecked == true) != _settings.StartWithWindows;
        _settings.Language = lang;
        _settings.VersionRetentionDays = days;
        _settings.DisconnectedAlertHours = hours;
        _settings.StartWithWindows = StartWithWindows.IsChecked == true;
        _settings.UpdateUrl = url;
        Close(true);
    }

    private void ShowError(string msg)
    {
        Error.Text = msg;
        Error.Visibility = Visibility.Visible;
    }
}
