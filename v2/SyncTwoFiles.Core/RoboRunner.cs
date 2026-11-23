using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncTwoFiles.Core;

// Ejecuta robocopy oculto y va entregando su salida (UTF-16) mientras trabaja.
public static class RoboRunner
{
    public static async Task<int> RunAsync(IEnumerable<string> args, Action<string>? onOutput, CancellationToken ct = default)
    {
        var log = Path.Combine(Path.GetTempPath(), "stf_" + Guid.NewGuid().ToString("N") + ".log");
        var all = args.Concat(new[] { "/NP", "/NJH", "/UNILOG:" + log }).ToList();
        var psi = new ProcessStartInfo("robocopy.exe", RoboArgs.ToArgumentString(all))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        long pos = 0;
        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("robocopy");
            using var reg = ct.Register(() => { try { p.Kill(); } catch { } });
            while (!p.HasExited)
            {
                await Task.Delay(300, CancellationToken.None).ConfigureAwait(false);
                pos = Pump(log, pos, onOutput);
            }
            p.WaitForExit();
            Pump(log, pos, onOutput);
            ct.ThrowIfCancellationRequested();
            return p.ExitCode;
        }
        finally
        {
            try { File.Delete(log); } catch { }
        }
    }

    // Lee solo bytes completos de UTF-16 para no partir un caracter a la mitad.
    internal static long Pump(string log, long pos, Action<string>? onOutput)
    {
        if (onOutput is null || !File.Exists(log)) return pos;
        try
        {
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var count = fs.Length - pos;
            count -= count % 2;
            if (count <= 0) return pos;
            fs.Seek(pos, SeekOrigin.Begin);
            var buf = new byte[count];
            var read = fs.Read(buf, 0, (int)count);
            read -= read % 2;
            var text = Encoding.Unicode.GetString(buf, 0, read).TrimStart('\uFEFF');
            if (text.Trim().Length > 0) onOutput(text);
            return pos + read;
        }
        catch { return pos; }
    }
}
