using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App.Views;

public partial class ConfirmDialog : OverlayDialog
{
    private readonly string _okText;
    private readonly string? _typeToConfirm;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _countdown;

    public ConfirmDialog(string title, string text, string okText, bool destructive = false,
        string? typeToConfirm = null, int countdown = 0)
    {
        InitializeComponent();
        _okText = okText;
        _typeToConfirm = typeToConfirm;
        _countdown = countdown;
        DlgTitle.Text = title;
        DlgText.Text = text;
        DlgText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        if (destructive) DlgOk.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        var typed = !string.IsNullOrEmpty(typeToConfirm);
        DlgInputHint.Visibility = DlgInput.Visibility = typed ? Visibility.Visible : Visibility.Collapsed;
        DlgInputHint.Text = L.T("confirm.typeHint", typeToConfirm ?? "");
        DlgInput.TextChanged += (_, _) => UpdateOk();
        DlgCancel.Click += (_, _) => Close(false);
        DlgOk.Click += (_, _) => { if (DlgOk.IsEnabled) Close(true); };
        _timer.Tick += (_, _) =>
        {
            _countdown--;
            if (_countdown <= 0) _timer.Stop();
            UpdateOk();
        };
        Closed += _ => _timer.Stop();
        UpdateOk();
        if (_countdown > 0) _timer.Start();
    }

    public override void OnShown()
    {
        if (!string.IsNullOrEmpty(_typeToConfirm)) DlgInput.Focus();
        else DlgOk.Focus();
    }

    private void UpdateOk()
    {
        var typedOk = string.IsNullOrEmpty(_typeToConfirm) || SyncConfig.UnlockConfirmation(_typeToConfirm, DlgInput.Text);
        DlgOk.IsEnabled = typedOk && _countdown <= 0;
        DlgOk.Content = _countdown > 0 ? $"{_okText} ({_countdown})" : _okText;
    }
}
