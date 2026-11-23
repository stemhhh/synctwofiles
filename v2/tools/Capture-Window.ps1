# Captura la ventana principal de un proceso a PNG con PrintWindow (para revisar la interfaz).
param(
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][string]$OutFile
)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CapWin {
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
[void][CapWin]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
$p = Get-Process -Id $ProcessId
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { throw "La ventana no esta visible." }
$r = New-Object CapWin+RECT
[void][CapWin]::GetWindowRect($h, [ref]$r)
$bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[void][CapWin]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($OutFile)
$bmp.Dispose()
$OutFile
