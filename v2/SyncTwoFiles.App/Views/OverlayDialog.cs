using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SyncTwoFiles.App.Views;

// Dialogo dentro de la ventana (capa oscura + tarjeta). Cada uno lleva su propio contexto y resultado.
public class OverlayDialog : UserControl
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public OverlayDialog()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xA6, 0, 0, 0));
        Focusable = true;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(false); }
        };
    }

    public Task<bool> Result => _tcs.Task;

    public event System.Action<OverlayDialog>? Closed;

    public virtual void OnShown() => Focus();

    public void Close(bool result)
    {
        if (_tcs.TrySetResult(result)) Closed?.Invoke(this);
    }
}
