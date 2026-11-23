using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App;

public sealed class PairItem : INotifyPropertyChanged
{
    private static readonly Brush Green = Frozen(0x30, 0xD1, 0x58);
    private static readonly Brush Orange = Frozen(0xFF, 0x9F, 0x0A);
    private static readonly Brush Gray = Frozen(0x63, 0x63, 0x66);

    public PairItem(string id) => Id = id;

    public string Id { get; }
    public string Name { get; private set; } = "";
    public string Sub { get; private set; } = "";
    public Brush DotBrush { get; private set; } = Gray;
    public Visibility LockVisibility { get; private set; } = Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(SyncPair p, bool running, bool paused)
    {
        Name = p.Name;
        Sub = $"{PathUtil.Label(p.Source)}  →  {PathUtil.Label(p.Backup)}";
        DotBrush = running ? Green : paused && p.AutoSync ? Orange : Gray;
        LockVisibility = p.Locked ? Visibility.Visible : Visibility.Collapsed;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }
}
