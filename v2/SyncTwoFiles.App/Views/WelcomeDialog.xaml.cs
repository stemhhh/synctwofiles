using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App.Views;

// Bienvenida: explica en 4 pasos como funciona y termina abriendo el editor de la primera sincronizacion.
public partial class WelcomeDialog : OverlayDialog
{
    private static readonly (string Glyph, string Color, string Title, string Body)[] Pages =
    {
        ("", "", "welcome.p1.title", "welcome.p1.body"),
        ("\uE895", "#30D158", "welcome.p2.title", "welcome.p2.body"),
        ("\uE81C", "#FF9F0A", "welcome.p3.title", "welcome.p3.body"),
        ("\uE72E", "#5E5CE6", "welcome.p4.title", "welcome.p4.body"),
    };

    private int _page;

    public WelcomeDialog()
    {
        InitializeComponent();
        for (var i = 0; i < Pages.Length; i++)
            Dots.Children.Add(new Ellipse { Width = 7, Height = 7, Margin = new Thickness(4, 0, 4, 0) });
        Back.Click += (_, _) =>
        {
            if (_page == 0) Close(false);
            else Show(_page - 1);
        };
        Next.Click += (_, _) =>
        {
            if (_page == Pages.Length - 1) Close(true);
            else Show(_page + 1);
        };
        Show(0);
    }

    public override void OnShown() => Next.Focus();

    private void Show(int page)
    {
        _page = page;
        var p = Pages[page];
        var first = page == 0;
        Logo.Visibility = first ? Visibility.Visible : Visibility.Collapsed;
        GlyphBox.Visibility = first ? Visibility.Collapsed : Visibility.Visible;
        if (!first)
        {
            Glyph.Text = p.Glyph;
            GlyphBox.Background = (Brush)new BrushConverter().ConvertFromString(p.Color)!;
        }
        Title.Text = L.T(p.Title);
        Body.Text = L.T(p.Body);
        Back.Content = first ? L.T("welcome.skip") : L.T("welcome.back");
        Next.Content = page == Pages.Length - 1 ? L.T("welcome.create") : L.T("welcome.next");
        for (var i = 0; i < Dots.Children.Count; i++)
            ((Ellipse)Dots.Children[i]).Fill = new SolidColorBrush(i == page ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x48, 0x48, 0x4A));
    }
}
