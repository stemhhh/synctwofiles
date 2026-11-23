using System;
using System.IO;
using System.Linq;
using System.Text;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.Tests;

public class ProcessMatchTests
{
    private const string Src = @"C:\Users\Ana\Desktop\PROYECTOS DIGITALES";
    private const string Bkp = @"C:\Almacen\Backup de Proyectos";

    [Fact]
    public void Reconoce_su_robocopy()
    {
        var cmd = $"\"C:\\Windows\\system32\\Robocopy.exe\" \"{Src}\" \"{Bkp}\" /E /MON:1 /MOT:1";
        Assert.True(RoboProcesses.IsAutoSyncCommandLine(cmd, Src, Bkp));
    }

    [Fact]
    public void Requiere_MON() =>
        Assert.False(RoboProcesses.IsAutoSyncCommandLine($"robocopy \"{Src}\" \"{Bkp}\" /MIR", Src, Bkp));

    [Fact]
    public void No_confunde_carpetas_con_prefijo_comun() =>
        Assert.False(RoboProcesses.IsAutoSyncCommandLine($"robocopy \"{Src} 2\" \"{Bkp}\" /E /MON:1", Src, Bkp));

    [Fact]
    public void No_confunde_backup_distinto() =>
        Assert.False(RoboProcesses.IsAutoSyncCommandLine($"robocopy \"{Src}\" \"{Bkp}2\" /E /MON:1", Src, Bkp));

    [Fact]
    public void Acepta_sin_comillas_y_mayusculas_distintas() =>
        Assert.True(RoboProcesses.IsAutoSyncCommandLine(@"robocopy c:\datos e:\backup\datos /E /MON:1", @"C:\Datos", @"E:\Backup\Datos"));

    [Fact]
    public void Acepta_raiz_con_barra_duplicada() =>
        Assert.True(RoboProcesses.IsAutoSyncCommandLine("robocopy \"E:\\Mi Disco\\\\\" \"F:\\Copia\" /E /MON:1", @"E:\Mi Disco", @"F:\Copia"));

    [Fact]
    public void Proceso_de_la_1x_no_es_actual()
    {
        var m = new AutoSyncManager(@"C:\logs");
        var p = SyncPair.Create("x", Src, Bkp);
        var old = new RoboProcess(1, $"robocopy \"{Src}\" \"{Bkp}\" /E /MON:1 /MOT:1 /LOG+:C:\\old.log");
        var cur = new RoboProcess(2, "\"C:\\Windows\\system32\\robocopy.exe\" " + m.ExpectedArguments(p));
        Assert.False(m.IsCurrent(p, old));
        Assert.True(m.IsCurrent(p, cur));
        Assert.Equal(2, m.ProcessesFor(p, new[] { old, cur }).Count);
    }

    [Fact]
    public void Cambiar_exclusiones_cambia_los_argumentos()
    {
        var m = new AutoSyncManager(@"C:\logs");
        var p = SyncPair.Create("x", Src, Bkp);
        var before = m.ExpectedArguments(p);
        p.Excludes.Add("*.tmp");
        Assert.NotEqual(before, m.ExpectedArguments(p));
    }

    [Fact]
    public void En_pausa_no_inicia_nada()
    {
        var c = new SyncConfig();
        c.Add(SyncPair.Create("x", @"C:\Windows", @"C:\nope"));
        c.Settings.Paused = true;
        Assert.Equal(0, new AutoSyncManager(@"C:\logs").EnsureAll(c, Array.Empty<RoboProcess>()));
    }
}

public class LogTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Detecta_utf16_con_y_sin_bom()
    {
        var a = Path.Combine(_t.Root, "a.log"); File.WriteAllBytes(a, new byte[] { 0xFF, 0xFE, 0x41, 0 });
        var b = Path.Combine(_t.Root, "b.log"); File.WriteAllBytes(b, new byte[] { 0x0D, 0, 0x0A, 0 });
        var c = Path.Combine(_t.Root, "c.log"); File.WriteAllText(c, "texto ANSI", Encoding.ASCII);
        Assert.True(PairLog.IsUtf16File(a));
        Assert.True(PairLog.IsUtf16File(b));
        Assert.False(PairLog.IsUtf16File(c));
    }

    [Fact]
    public void Log_nuevo_se_crea_con_bom()
    {
        var log = Path.Combine(_t.Root, "n.log");
        PairLog.ResetIfNeeded(log);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, File.ReadAllBytes(log));
    }

    [Fact]
    public void Archiva_log_ansi()
    {
        var log = _t.File("x.log", "historial viejo en ANSI");
        PairLog.ResetIfNeeded(log);
        Assert.True(File.Exists(Path.Combine(_t.Root, "x.old.log")));
        Assert.Equal(new byte[] { 0xFF, 0xFE }, File.ReadAllBytes(log));
    }

    [Fact]
    public void Conserva_log_utf16_valido()
    {
        var log = Path.Combine(_t.Root, "u.log");
        File.WriteAllBytes(log, Encoding.Unicode.GetBytes("\r\nhola"));
        PairLog.ResetIfNeeded(log);
        Assert.False(File.Exists(Path.Combine(_t.Root, "u.old.log")));
        Assert.Equal(Encoding.Unicode.GetBytes("\r\nhola"), File.ReadAllBytes(log));
    }

    [Fact]
    public void Archiva_log_demasiado_grande()
    {
        var log = Path.Combine(_t.Root, "g.log");
        using (var fs = File.Create(log)) { fs.Write(new byte[] { 0xFF, 0xFE }); fs.SetLength(PairLog.MaxBytes + 10); }
        PairLog.ResetIfNeeded(log);
        Assert.True(File.Exists(Path.Combine(_t.Root, "g.old.log")));
    }

    [Fact]
    public void Ultima_actividad()
    {
        Assert.Null(PairLog.LastActivity(Path.Combine(_t.Root, "no.log")));
        Assert.NotNull(PairLog.LastActivity(_t.File("a.log")));
    }

    [Fact]
    public void Historial_filtra_y_cuenta_errores()
    {
        var text = "Started : hoy\r\n2026/10/01 ERROR 5 (0x00000005) Copying File C:\\a.txt\r\nAcceso denegado.\r\n   Files :  3\r\n";
        Assert.Single(HistoryLog.Filter(text, null, true));
        Assert.Single(HistoryLog.Filter(text, "files", false));
        Assert.Equal(4, HistoryLog.Filter(text, null, false).Count());
    }

    [Fact]
    public void Lee_historial_utf16_y_ansi()
    {
        var u = Path.Combine(_t.Root, "u.log");
        File.WriteAllBytes(u, new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Señal ñ")).ToArray());
        Assert.Equal("Señal ñ", HistoryLog.ReadTail(u));
        var a = Path.Combine(_t.Root, "a.log");
        File.WriteAllBytes(a, Encoding.Latin1.GetBytes("Copia ñ"));
        Assert.Equal("Copia ñ", HistoryLog.ReadTail(a));
    }

    [Fact]
    public void Cuenta_solo_errores_nuevos()
    {
        var log = Path.Combine(_t.Root, "e.log");
        File.WriteAllBytes(log, Encoding.Unicode.GetBytes("x ERROR 5 a\r\n"));
        long pos = 0;
        Assert.Equal(1, HistoryLog.CountNewErrors(log, ref pos));
        Assert.Equal(0, HistoryLog.CountNewErrors(log, ref pos));
        File.AppendAllText(log, "y ERROR 2 b\r\nz ok\r\n", Encoding.Unicode);
        Assert.Equal(1, HistoryLog.CountNewErrors(log, ref pos));
    }
}

public class RealRobocopyTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Automatica_copia_y_no_borra_del_backup()
    {
        var (src, dst) = _t.Pair();
        var p = SyncPair.Create("x", src, dst);
        File.WriteAllText(Path.Combine(src, "a.txt"), "1");
        Directory.CreateDirectory(dst);
        File.WriteAllText(Path.Combine(dst, "viejo.txt"), "se queda");
        var args = RoboArgs.AutoSync(p).ToArray();
        Assert.True(RoboArgs.IsSuccess(TestUtil.Robocopy(args)));
        Assert.True(File.Exists(Path.Combine(dst, "a.txt")));
        Assert.True(File.Exists(Path.Combine(dst, "viejo.txt")));
    }

    [Fact]
    public void Espejo_borra_lo_que_sobra_pero_no_versiones()
    {
        var (src, dst) = _t.Pair();
        var p = SyncPair.Create("x", src, dst);
        File.WriteAllText(Path.Combine(src, "a.txt"), "1");
        Directory.CreateDirectory(Path.Combine(dst, ExcludeRules.VersionsDirName, "2026-01-01_000000_mirror"));
        File.WriteAllText(Path.Combine(dst, ExcludeRules.VersionsDirName, "2026-01-01_000000_mirror", "v.txt"), "v");
        File.WriteAllText(Path.Combine(dst, "sobra.txt"), "x");
        Assert.True(RoboArgs.IsSuccess(TestUtil.Robocopy(RoboArgs.Mirror(p).ToArray())));
        Assert.False(File.Exists(Path.Combine(dst, "sobra.txt")));
        Assert.True(File.Exists(Path.Combine(dst, ExcludeRules.VersionsDirName, "2026-01-01_000000_mirror", "v.txt")));
    }

    [Fact]
    public void Exclusiones_no_se_copian()
    {
        var (src, dst) = _t.Pair();
        var p = SyncPair.Create("x", src, dst, excludes: new[] { "node_modules", "*.tmp" });
        File.WriteAllText(Path.Combine(src, "a.txt"), "1");
        File.WriteAllText(Path.Combine(src, "b.tmp"), "1");
        Directory.CreateDirectory(Path.Combine(src, "node_modules", "lib"));
        File.WriteAllText(Path.Combine(src, "node_modules", "lib", "c.js"), "1");
        TestUtil.Robocopy(RoboArgs.AutoSync(p).ToArray());
        Assert.True(File.Exists(Path.Combine(dst, "a.txt")));
        Assert.False(File.Exists(Path.Combine(dst, "b.tmp")));
        Assert.False(Directory.Exists(Path.Combine(dst, "node_modules")));
        Assert.True(TreeCompare.Compare(src, dst, p.Rules).InSync);
    }

    [Fact]
    public void Restaurar_no_pisa_archivos_mas_nuevos()
    {
        var (src, dst) = _t.Pair();
        var p = SyncPair.Create("x", src, dst);
        Directory.CreateDirectory(dst);
        _t.File(Path.Combine(dst, "falta.txt"), "del backup");
        _t.File(Path.Combine(dst, "nuevo.txt"), "viejo", DateTime.Now.AddDays(-2));
        _t.File(Path.Combine(src, "nuevo.txt"), "nuevo", DateTime.Now);
        Assert.True(RoboArgs.IsSuccess(TestUtil.Robocopy(RoboArgs.Restore(p).ToArray())));
        Assert.Equal("del backup", File.ReadAllText(Path.Combine(src, "falta.txt")));
        Assert.Equal("nuevo", File.ReadAllText(Path.Combine(src, "nuevo.txt")));
    }

    [Fact]
    public void Ruta_raiz_con_espacios_y_barra_final_funciona()
    {
        var src = _t.Dir("Mi Disco") + "\\";
        var dst = Path.Combine(_t.Root, "Destino con espacios") + "\\";
        File.WriteAllText(Path.Combine(src, "a.txt"), "1");
        Assert.True(RoboArgs.IsSuccess(TestUtil.Robocopy(src, dst, "/E")));
        Assert.True(File.Exists(Path.Combine(dst, "a.txt")));
    }

    [Fact]
    public async System.Threading.Tasks.Task RoboRunner_entrega_salida_y_codigo()
    {
        var (src, dst) = _t.Pair();
        File.WriteAllText(Path.Combine(src, "a.txt"), "1");
        var sb = new StringBuilder();
        var code = await RoboRunner.RunAsync(new[] { src, dst, "/E" }, s => { lock (sb) sb.Append(s); });
        Assert.True(RoboArgs.IsSuccess(code));
        Assert.True(File.Exists(Path.Combine(dst, "a.txt")));
        Assert.Contains("a.txt", sb.ToString());
    }

    [Fact]
    public void Pump_no_parte_caracteres()
    {
        var log = Path.Combine(_t.Root, "p.log");
        var bytes = Encoding.Unicode.GetBytes("ñandú");
        File.WriteAllBytes(log, bytes.Take(5).ToArray());
        var got = new StringBuilder();
        var pos = RoboRunner.Pump(log, 0, s => got.Append(s));
        Assert.Equal(4, pos);
        File.WriteAllBytes(log, bytes);
        RoboRunner.Pump(log, pos, s => got.Append(s));
        Assert.Equal("ñandú", got.ToString());
    }
}

public class AutoSyncRealTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Inicia_copia_cambios_y_se_detiene()
    {
        var (src, dst) = _t.Pair();
        var p = SyncPair.Create("x", src, dst);
        var m = new AutoSyncManager(_t.Dir("logs"));
        File.WriteAllText(Path.Combine(src, "a.txt"), "1");
        Assert.True(m.Start(p));
        Assert.True(TestUtil.WaitUntil(() => File.Exists(Path.Combine(dst, "a.txt"))));
        Assert.True(TestUtil.WaitUntil(() => m.ProcessesFor(p).Count == 1));
        Assert.True(m.IsCurrent(p, m.ProcessesFor(p)[0]));
        Assert.True(PairLog.IsUtf16File(m.LogFor(p)));
        m.Stop(p);
        Assert.True(TestUtil.WaitUntil(() => m.ProcessesFor(p).Count == 0));
    }

    [Fact]
    public void EnsureAll_no_duplica_y_reemplaza_argumentos_viejos()
    {
        var (src, dst) = _t.Pair();
        var p = SyncPair.Create("x", src, dst);
        var c = new SyncConfig();
        c.Add(p);
        var m = new AutoSyncManager(_t.Dir("logs"));
        Assert.Equal(1, m.EnsureAll(c));
        Assert.True(TestUtil.WaitUntil(() => m.ProcessesFor(p).Count == 1));
        Assert.Equal(0, m.EnsureAll(c));
        p.Excludes.Add("*.tmp");
        Assert.Equal(1, m.EnsureAll(c));
        Assert.True(TestUtil.WaitUntil(() => m.ProcessesFor(p).Count == 1 && m.IsCurrent(p, m.ProcessesFor(p)[0])));
    }

    [Fact]
    public void Disco_del_backup_ausente_no_inicia()
    {
        var p = SyncPair.Create("x", _t.Dir("s"), @"Q:\NoExiste\Backup");
        Assert.Equal(Availability.BackupMissing, AutoSyncManager.GetAvailability(p));
        Assert.False(new AutoSyncManager(_t.Dir("logs")).Start(p));
    }

    [Fact]
    public void Origen_ausente_se_detecta()
    {
        var p = SyncPair.Create("x", Path.Combine(_t.Root, "no"), Path.Combine(_t.Root, "b"));
        Assert.Equal(Availability.SourceMissing, AutoSyncManager.GetAvailability(p));
    }

    [Fact]
    public void Un_par_que_falla_no_bloquea_a_los_demas()
    {
        var c = new SyncConfig();
        c.Add(SyncPair.Create("malo", _t.Dir("s1"), @"Q:\NoExiste\B"));
        var (src, dst) = _t.Pair("bueno");
        var good = SyncPair.Create("bueno", src, dst);
        c.Add(good);
        var m = new AutoSyncManager(_t.Dir("logs"));
        Assert.Equal(1, m.EnsureAll(c));
        Assert.True(TestUtil.WaitUntil(() => m.ProcessesFor(good).Count == 1));
    }
}
