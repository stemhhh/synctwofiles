using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.Tests;

public class TreeCompareTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Detecta_nuevos_cambiados_y_sobrantes()
    {
        var (src, dst) = _t.Pair();
        var old = DateTime.Now.AddDays(-1);
        _t.File(Path.Combine(src, "igual.txt"), "x", old);
        _t.File(Path.Combine(dst, "igual.txt"), "x", old);
        _t.File(Path.Combine(src, "nuevo.txt"), "n");
        _t.File(Path.Combine(src, "cambio.txt"), "largo", DateTime.Now);
        _t.File(Path.Combine(dst, "cambio.txt"), "c", old);
        _t.File(Path.Combine(dst, "sobra.txt"), "s");
        _t.File(Path.Combine(dst, "CarpetaVieja", "a.txt"), "a");
        _t.File(Path.Combine(dst, "CarpetaVieja", "b.txt"), "b");
        _t.File(Path.Combine(src, "Sub", "s.txt"), "s");

        var d = TreeCompare.Compare(src, dst, new ExcludeRules());
        Assert.Equal(new[] { "nuevo.txt", @"Sub\s.txt" }, d.NewFiles.OrderBy(x => x));
        Assert.Equal("cambio.txt", Assert.Single(d.ChangedFiles).Path);
        Assert.Equal(new[] { "sobra.txt" }, d.ExtraFiles);
        Assert.Equal(new[] { "CarpetaVieja" }, d.ExtraDirs);
        Assert.Equal(3, d.ExtraCount);
        Assert.False(d.InSync);
        Assert.Equal(4, d.SourceFiles);
        Assert.Equal(5, d.DestFiles);
    }

    [Fact]
    public void Destino_inexistente_todo_es_nuevo()
    {
        var src = _t.Dir("s");
        _t.File(@"s\a.txt", "12345");
        var d = TreeCompare.Compare(src, Path.Combine(_t.Root, "no"), new ExcludeRules());
        Assert.Single(d.NewFiles);
        Assert.Equal(5, d.PendingBytes);
    }

    [Fact]
    public void Tolerancia_de_2_segundos()
    {
        var (src, dst) = _t.Pair();
        var t = DateTime.Now.AddHours(-1);
        _t.File(Path.Combine(src, "a.txt"), "x", t);
        _t.File(Path.Combine(dst, "a.txt"), "x", t.AddSeconds(1));
        Assert.True(TreeCompare.Compare(src, dst, new ExcludeRules()).InSync);
    }

    [Fact]
    public void Ignora_versiones_sistema_y_exclusiones()
    {
        var (src, dst) = _t.Pair();
        _t.File(Path.Combine(dst, ExcludeRules.VersionsDirName, "2026-01-01_000000_mirror", "v.txt"));
        _t.File(Path.Combine(src, "$RECYCLE.BIN", "x"));
        _t.File(Path.Combine(src, "b.tmp"));
        _t.File(Path.Combine(src, "node_modules", "c.js"));
        Assert.True(TreeCompare.Compare(src, dst, new ExcludeRules(new[] { "*.tmp", "node_modules" })).InSync);
    }

    [Fact]
    public void Medir_carpeta()
    {
        _t.File(@"m\a.txt", "123");
        _t.File(@"m\s\b.txt", "45");
        Assert.Equal((2, 5L), TreeCompare.Measure(Path.Combine(_t.Root, "m"), new ExcludeRules()));
    }
}

public class VersionStoreTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    private SyncPair NewPair()
    {
        var (src, dst) = _t.Pair();
        Directory.CreateDirectory(dst);
        return SyncPair.Create("x", src, dst);
    }

    [Fact]
    public void Antes_del_espejo_mueve_lo_que_se_borraria_o_reemplazaria()
    {
        var p = NewPair();
        _t.File(Path.Combine(p.Source, "a.txt"), "nuevo", DateTime.Now);
        _t.File(Path.Combine(p.Backup, "a.txt"), "viejo", DateTime.Now.AddDays(-1));
        _t.File(Path.Combine(p.Backup, "sobra.txt"), "s");
        _t.File(Path.Combine(p.Backup, "Vieja", "x.txt"), "x");
        var d = TreeCompare.Compare(p.Source, p.Backup, p.Rules);
        var now = new DateTime(2026, 10, 1, 16, 30, 0);
        var r = VersionStore.PreserveBeforeMirror(p, d, now);
        Assert.Equal(3, r.Count);
        Assert.Empty(r.Errors);
        Assert.Equal(Path.Combine(VersionStore.Root(p), "2026-10-01_163000_mirror"), r.Session);
        Assert.Equal("viejo", File.ReadAllText(Path.Combine(r.Session!, "a.txt")));
        Assert.True(File.Exists(Path.Combine(r.Session!, "sobra.txt")));
        Assert.True(File.Exists(Path.Combine(r.Session!, "Vieja", "x.txt")));
        Assert.False(File.Exists(Path.Combine(p.Backup, "sobra.txt")));
    }

    [Fact]
    public void Espejo_completo_con_versiones_no_pierde_nada()
    {
        var p = NewPair();
        _t.File(Path.Combine(p.Source, "a.txt"), "a");
        _t.File(Path.Combine(p.Backup, "borrado_en_origen.txt"), "importante");
        var d = TreeCompare.Compare(p.Source, p.Backup, p.Rules);
        var r = VersionStore.PreserveBeforeMirror(p, d, DateTime.Now);
        Assert.True(RoboArgs.IsSuccess(TestUtil.Robocopy(RoboArgs.Mirror(p).ToArray())));
        Assert.True(TreeCompare.Compare(p.Source, p.Backup, p.Rules).InSync);
        Assert.Equal("importante", File.ReadAllText(Path.Combine(r.Session!, "borrado_en_origen.txt")));
    }

    [Fact]
    public void Sin_cambios_no_crea_sesion()
    {
        var p = NewPair();
        var r = VersionStore.PreserveBeforeMirror(p, new DiffResult(), DateTime.Now);
        Assert.Null(r.Session);
        Assert.False(Directory.Exists(VersionStore.Root(p)));
    }

    [Fact]
    public void Sesiones_con_la_misma_hora_no_se_pisan()
    {
        var p = NewPair();
        var now = DateTime.Now;
        _t.File(Path.Combine(p.Backup, "a.txt"));
        var d1 = TreeCompare.Compare(p.Source, p.Backup, p.Rules);
        var r1 = VersionStore.PreserveBeforeMirror(p, d1, now);
        _t.File(Path.Combine(p.Backup, "b.txt"));
        var d2 = TreeCompare.Compare(p.Source, p.Backup, p.Rules);
        var r2 = VersionStore.PreserveBeforeMirror(p, d2, now);
        Assert.NotEqual(r1.Session, r2.Session);
        Assert.Equal(2, VersionStore.List(p).Count);
    }

    [Fact]
    public void Antes_de_restaurar_copia_lo_que_se_reemplazaria()
    {
        var p = NewPair();
        _t.File(Path.Combine(p.Backup, "a.txt"), "del backup", DateTime.Now);
        _t.File(Path.Combine(p.Source, "a.txt"), "actual", DateTime.Now.AddDays(-1));
        _t.File(Path.Combine(p.Backup, "b.txt"), "viejo", DateTime.Now.AddDays(-3));
        _t.File(Path.Combine(p.Source, "b.txt"), "mas nuevo", DateTime.Now);
        var d = TreeCompare.Compare(p.Backup, p.Source, p.Rules);
        var over = VersionStore.WouldOverwriteOnRestore(d, skipNewer: true);
        Assert.Equal(new[] { "a.txt" }, over);
        Assert.Equal(2, VersionStore.WouldOverwriteOnRestore(d, skipNewer: false).Count);
        var r = VersionStore.PreserveBeforeRestore(p, p.Source, "", over, VersionStore.KindRestore, DateTime.Now);
        Assert.Equal(1, r.Count);
        Assert.Equal("actual", File.ReadAllText(Path.Combine(r.Session!, "a.txt")));
        Assert.Equal("actual", File.ReadAllText(Path.Combine(p.Source, "a.txt")));
    }

    [Fact]
    public void Lista_y_limpia_por_antiguedad()
    {
        var p = NewPair();
        var root = VersionStore.Root(p);
        _t.File(Path.Combine(root, "2026-01-01_120000_mirror", "a.txt"), "12");
        _t.File(Path.Combine(root, "2026-09-30_120000_restore", "b.txt"), "3");
        Directory.CreateDirectory(Path.Combine(root, "no-es-sesion"));
        var list = VersionStore.List(p);
        Assert.Equal(2, list.Count);
        Assert.Equal(VersionStore.KindRestore, list[0].Kind);
        Assert.Equal(2, list[1].Bytes);
        Assert.Equal(1, VersionStore.Cleanup(p, 30, new DateTime(2026, 10, 1)));
        Assert.Single(VersionStore.List(p));
        Assert.True(Directory.Exists(Path.Combine(root, "no-es-sesion")));
    }

    [Fact]
    public void Borrar_solo_acepta_sesiones_dentro_de_versiones()
    {
        var p = NewPair();
        var fake = _t.Dir("2026-01-01_120000_mirror");
        Assert.False(VersionStore.Delete(fake));
        Assert.True(Directory.Exists(fake));
        Assert.False(VersionStore.Delete(p.Backup));
        Assert.True(Directory.Exists(p.Backup));
        var real = Path.Combine(VersionStore.Root(p), "2026-01-01_120000_mirror");
        File.SetAttributes(_t.File(Path.Combine(real, "r.txt")), FileAttributes.ReadOnly);
        Assert.True(VersionStore.Delete(real));
        Assert.False(Directory.Exists(real));
    }

    [Theory]
    [InlineData("2026-10-01_163000_mirror", true)]
    [InlineData("2026-10-01_163000_restore-2", true)]
    [InlineData("2026-13-01_163000_mirror", false)]
    [InlineData("algo", false)]
    public void Nombres_de_sesion(string name, bool ok) => Assert.Equal(ok, VersionStore.TryParseSession(name, out _, out _));
}

public class HealthTests
{
    private static (SyncConfig, SyncPair) Make()
    {
        var c = new SyncConfig();
        var p = SyncPair.Create("Fotos", @"C:\a", @"E:\b");
        c.Add(p);
        c.Settings.DisconnectedAlertHours = 24;
        return (c, p);
    }

    private static List<Alert> Check(SyncConfig c, AppState s, DateTime now, Availability a, DiskSpace? d = null, int errors = 0) =>
        HealthMonitor.Check(c, s, now, _ => a, _ => d, _ => errors);

    [Fact]
    public void Disco_desconectado_avisa_despues_del_limite_una_vez_al_dia()
    {
        var (c, p) = Make();
        var s = new AppState();
        var t0 = new DateTime(2026, 10, 1, 8, 0, 0);
        Assert.Empty(Check(c, s, t0, Availability.Ok));
        Assert.Empty(Check(c, s, t0.AddHours(10), Availability.BackupMissing));
        var a = Assert.Single(Check(c, s, t0.AddHours(25), Availability.BackupMissing));
        Assert.Equal(AlertKind.Disconnected, a.Kind);
        Assert.Equal(p.Id, a.PairId);
        Assert.Empty(Check(c, s, t0.AddHours(30), Availability.BackupMissing));
        Assert.Single(Check(c, s, t0.AddHours(50), Availability.BackupMissing));
    }

    [Fact]
    public void Reconectar_reinicia_la_cuenta()
    {
        var (c, _) = Make();
        var s = new AppState();
        var t0 = new DateTime(2026, 10, 1);
        Check(c, s, t0, Availability.Ok);
        Check(c, s, t0.AddHours(20), Availability.Ok);
        Assert.Empty(Check(c, s, t0.AddHours(40), Availability.BackupMissing));
    }

    [Fact]
    public void Origen_ausente_tiene_su_propio_aviso()
    {
        var (c, _) = Make();
        var s = new AppState();
        var t0 = new DateTime(2026, 10, 1);
        Check(c, s, t0, Availability.Ok);
        Assert.Equal(AlertKind.SourceMissing, Assert.Single(Check(c, s, t0.AddHours(25), Availability.SourceMissing)).Kind);
    }

    [Fact]
    public void Poco_espacio()
    {
        Assert.True(HealthMonitor.IsLowSpace(new DiskSpace(5, 100), 0));
        Assert.False(HealthMonitor.IsLowSpace(new DiskSpace(50, 100), 0));
        Assert.True(HealthMonitor.IsLowSpace(new DiskSpace(50, 100), 60));
        var (c, _) = Make();
        Assert.Equal(AlertKind.LowSpace, Assert.Single(Check(c, new AppState(), DateTime.Now, Availability.Ok, new DiskSpace(1, 100))).Kind);
    }

    [Fact]
    public void Errores_de_copia_avisan_con_pausa_de_una_hora()
    {
        var (c, _) = Make();
        var s = new AppState();
        var t0 = DateTime.Now;
        Assert.Equal(AlertKind.CopyErrors, Assert.Single(Check(c, s, t0, Availability.Ok, errors: 2)).Kind);
        Assert.Empty(Check(c, s, t0.AddMinutes(30), Availability.Ok, errors: 1));
        Assert.Single(Check(c, s, t0.AddMinutes(61), Availability.Ok, errors: 1));
    }

    [Fact]
    public void En_pausa_o_sin_automatica_no_avisa()
    {
        var (c, p) = Make();
        var s = new AppState();
        var t0 = new DateTime(2026, 10, 1);
        c.Settings.Paused = true;
        Assert.Empty(Check(c, s, t0.AddDays(5), Availability.BackupMissing, errors: 3));
        c.Settings.Paused = false;
        p.AutoSync = false;
        Assert.Empty(Check(c, s, t0.AddDays(5), Availability.BackupMissing, errors: 3));
    }

    [Fact]
    public void Estado_guarda_y_lee()
    {
        var path = Path.Combine(Path.GetTempPath(), "stf_tests", Guid.NewGuid().ToString("N"), "state.json");
        var s = new AppState();
        s.For("a").BackupFiles = 7;
        s.TrayHintShown = true;
        StateStore.Save(s, path);
        var r = StateStore.Load(path);
        Assert.Equal(7, r.For("a").BackupFiles);
        Assert.True(r.TrayHintShown);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact] public void Estado_danado_empieza_vacio() => Assert.Empty(StateStore.Load(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".json")).Pairs);

    [Fact]
    public void Formato_de_tamano()
    {
        Assert.Equal("512 B", Disk.FormatBytes(512));
        Assert.Contains("KB", Disk.FormatBytes(2048));
        Assert.Contains("GB", Disk.FormatBytes(3L * 1024 * 1024 * 1024));
    }

    [Fact] public void Espacio_de_disco_real() => Assert.True(Disk.Get(Path.GetTempPath())!.Total > 0);
}

public class UpdateTests
{
    [Theory]
    [InlineData("2.0.1", "2.0.0", true)]
    [InlineData("2.0.0", "2.0.0", false)]
    [InlineData("1.9.9", "2.0.0", false)]
    [InlineData("basura", "2.0.0", false)]
    public void Compara_versiones(string remote, string current, bool newer) => Assert.Equal(newer, UpdateChecker.IsNewer(remote, current));

    [Fact]
    public void Lee_json()
    {
        var u = UpdateChecker.Parse("{\"version\":\"2.1.0\",\"url\":\"https://example.com/d\"}")!;
        Assert.Equal("2.1.0", u.Version);
        Assert.Null(UpdateChecker.Parse("{}"));
        Assert.Null(UpdateChecker.Parse("no json"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Solo_https()
    {
        Assert.Null(await UpdateChecker.CheckAsync("http://example.com/v.json", "2.0.0"));
        Assert.Null(await UpdateChecker.CheckAsync("", "2.0.0"));
    }
}
