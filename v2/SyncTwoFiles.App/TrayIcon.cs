using System;
using System.Drawing;
using System.Windows.Forms;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App;

// Icono junto al reloj: abre la ventana, pausa o reanuda todo y muestra los avisos.
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AppController _c;
    private readonly ContextMenuStrip _menu = new();
    private string? _lastPairId;

    public TrayIcon(AppController controller)
    {
        _c = controller;
        _menu.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
        _menu.BackColor = Color.FromArgb(0x2C, 0x2C, 0x2E);
        _menu.ForeColor = Color.FromArgb(0xF5, 0xF5, 0xF7);
        _menu.ShowImageMargin = false;
        _menu.Font = new Font("Segoe UI", 9.5f);
        _menu.Opening += (_, _) => BuildMenu();
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = AppInfo.Name,
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenRequested?.Invoke(null); };
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke(_lastPairId);
        BuildMenu();
    }

    public event Action<string?>? OpenRequested;
    public event Action? ExitRequested;

    private static Icon LoadIcon()
    {
        try
        {
            var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/SyncTwoFiles.ico"));
            if (res is not null) return new Icon(res.Stream, SystemInformation.SmallIconSize);
        }
        catch { }
        return SystemIcons.Application;
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();
        var open = new ToolStripMenuItem(L.T("tray.open")) { Font = new Font(_menu.Font, FontStyle.Bold) };
        open.Click += (_, _) => OpenRequested?.Invoke(null);
        _menu.Items.Add(open);
        _menu.Items.Add(new ToolStripSeparator());
        foreach (var p in _c.Config.Pairs)
        {
            var status = _c.Config.Settings.Paused ? L.T("status.paused")
                : _c.IsRunning(p.Id) ? L.T("tray.active")
                : p.AutoSync ? L.T("tray.waiting") : L.T("tray.off");
            var id = p.Id;
            var item = new ToolStripMenuItem($"{(_c.IsRunning(p.Id) ? "●" : "○")}  {p.Name} — {status}");
            item.Click += (_, _) => OpenRequested?.Invoke(id);
            _menu.Items.Add(item);
        }
        if (_c.Config.Pairs.Count > 0) _menu.Items.Add(new ToolStripSeparator());
        var pause = new ToolStripMenuItem(_c.Config.Settings.Paused ? L.T("tray.resume") : L.T("tray.pause"));
        pause.Click += (_, _) => _c.SetPaused(!_c.Config.Settings.Paused);
        _menu.Items.Add(pause);
        var exit = new ToolStripMenuItem(L.T("tray.exit"));
        exit.Click += (_, _) => ExitRequested?.Invoke();
        _menu.Items.Add(exit);
    }

    public void Notify(string title, string text, string? pairId = null, bool warning = true)
    {
        _lastPairId = pairId;
        _icon.ShowBalloonTip(8000, title, text, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        private static readonly Color Bg = Color.FromArgb(0x2C, 0x2C, 0x2E);
        private static readonly Color Hover = Color.FromArgb(0x3A, 0x3A, 0x3C);
        public override Color ToolStripDropDownBackground => Bg;
        public override Color MenuBorder => Color.FromArgb(0x3A, 0x3A, 0x3C);
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color SeparatorDark => Color.FromArgb(0x48, 0x48, 0x4A);
        public override Color SeparatorLight => Bg;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
    }
}
