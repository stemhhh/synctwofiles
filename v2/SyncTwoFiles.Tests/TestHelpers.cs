using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.Tests;

// Carpeta temporal propia de cada prueba; al terminar detiene los robocopy que la usen y la borra.
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "stf_tests", Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Dir(string rel)
    {
        var p = Path.Combine(Root, rel);
        Directory.CreateDirectory(p);
        return p;
    }

    public string File(string rel, string content = "x", DateTime? time = null)
    {
        var p = Path.Combine(Root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        if (time is not null) System.IO.File.SetLastWriteTime(p, time.Value);
        return p;
    }

    public (string Src, string Dst) Pair(string name = "par")
    {
        return (Dir(name + "\\Origen con espacios"), Path.Combine(Root, name, "Backup con espacios"));
    }

    public void Dispose()
    {
        foreach (var p in RoboProcesses.List().Where(p => p.CommandLine.Contains(Root, StringComparison.OrdinalIgnoreCase)))
            RoboProcesses.Kill(p.Id);
        for (var i = 0; i < 10; i++)
        {
            try { Directory.Delete(Root, true); return; }
            catch { Thread.Sleep(300); }
        }
    }
}

public static class TestUtil
{
    public static bool WaitUntil(Func<bool> cond, int timeoutMs = 20000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try { if (cond()) return true; } catch { }
            Thread.Sleep(250);
        }
        return false;
    }

    public static int Robocopy(params string[] args)
    {
        var psi = new ProcessStartInfo("robocopy.exe", RoboArgs.ToArgumentString(args))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    public static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !System.IO.File.Exists(Path.Combine(d.FullName, "Directory.Build.props"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("No se encontró la solución.");
    }
}
