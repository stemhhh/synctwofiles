using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.Tests;

public class ValidationTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    [Fact] public void Pide_nombre() => Assert.Equal(L.T("val.name"), Validation.ValidatePair("", _t.Root, @"D:\b"));
    [Fact] public void Pide_origen() => Assert.Equal(L.T("val.source"), Validation.ValidatePair("x", "", @"D:\b"));
    [Fact] public void Pide_backup() => Assert.Equal(L.T("val.backup"), Validation.ValidatePair("x", _t.Root, ""));
    [Fact] public void Origen_debe_existir() => Assert.Equal(L.T("val.sourceMissing"), Validation.ValidatePair("x", Path.Combine(_t.Root, "no"), @"D:\b"));

    [Fact]
    public void Rechaza_disco_entero()
    {
        var src = _t.Dir("src");
        Assert.Equal(L.T("val.backupRoot", @"E:\"), Validation.ValidatePair("x", src, "E:"));
        Assert.Equal(L.T("val.backupRoot", @"E:\"), Validation.ValidatePair("x", src, @"E:\"));
    }

    [Fact] public void Rechaza_misma_carpeta() { var s = _t.Dir("s"); Assert.Equal(L.T("val.same"), Validation.ValidatePair("x", s, s + "\\")); }
    [Fact] public void Rechaza_backup_dentro() { var s = _t.Dir("s"); Assert.Equal(L.T("val.backupInside"), Validation.ValidatePair("x", s, s + "\\bk")); }
    [Fact] public void Rechaza_origen_dentro() { var s = _t.Dir("bk\\s"); Assert.Equal(L.T("val.sourceInside"), Validation.ValidatePair("x", s, Path.Combine(_t.Root, "bk"))); }

    [Fact]
    public void Rechaza_nombre_repetido_sin_mayusculas()
    {
        var s = _t.Dir("s");
        var other = SyncPair.Create("Fotos", _t.Dir("o"), Path.Combine(_t.Root, "ob"));
        Assert.Equal(L.T("val.dupName", "Fotos"), Validation.ValidatePair(" fotos ", s, Path.Combine(_t.Root, "b"), new[] { other }));
    }

    [Fact]
    public void Rechaza_backup_compartido_o_anidado()
    {
        var other = SyncPair.Create("A", _t.Dir("a"), Path.Combine(_t.Root, "Backups"));
        var s = _t.Dir("s");
        Assert.Equal(L.T("val.backupShared", "A"), Validation.ValidatePair("B", s, Path.Combine(_t.Root, "Backups"), new[] { other }));
        Assert.Equal(L.T("val.backupShared", "A"), Validation.ValidatePair("B", s, Path.Combine(_t.Root, "Backups", "B"), new[] { other }));
    }

    [Fact]
    public void Al_editar_se_ignora_a_si_misma()
    {
        var s = _t.Dir("s");
        var me = SyncPair.Create("Fotos", s, Path.Combine(_t.Root, "b"));
        Assert.Null(Validation.ValidatePair("Fotos", s, me.Backup, new[] { me }, me.Id));
    }

    [Fact]
    public void Valida_exclusiones()
    {
        var s = _t.Dir("s");
        Assert.NotNull(Validation.ValidatePair("x", s, Path.Combine(_t.Root, "b"), excludes: new[] { @"C:\x" }));
        Assert.Null(Validation.ValidatePair("x", s, Path.Combine(_t.Root, "b"), excludes: new[] { "*.tmp" }));
    }

    [Fact] public void Valido() => Assert.Null(Validation.ValidatePair("Fotos", _t.Dir("s"), Path.Combine(_t.Root, "b")));
}

public class ConfigTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Par_nuevo_bloqueado_y_automatico_por_defecto()
    {
        var p = SyncPair.Create(" Fotos ", @"C:\a\", @"D:\b\");
        Assert.True(p.Locked);
        Assert.True(p.AutoSync);
        Assert.Equal("Fotos", p.Name);
        Assert.Equal(@"C:\a", p.Source);
        Assert.Equal(@"D:\b", p.Backup);
        Assert.True(Guid.TryParse(p.Id, out _));
    }

    [Fact] public void Sin_archivo_devuelve_null() => Assert.Null(ConfigStore.Load(Path.Combine(_t.Root, "no.json")));

    [Fact]
    public void Guarda_y_lee_igual()
    {
        var path = Path.Combine(_t.Root, "c", "config.json");
        var c = new SyncConfig();
        c.Add(SyncPair.Create("A", @"C:\a", @"D:\a", excludes: new[] { "*.tmp" }));
        c.Settings.VersionRetentionDays = 12;
        c.Settings.Paused = true;
        ConfigStore.Save(c, path);
        var r = ConfigStore.Load(path)!;
        Assert.Single(r.Pairs);
        Assert.Equal("A", r.Pairs[0].Name);
        Assert.Equal(new[] { "*.tmp" }, r.Pairs[0].Excludes);
        Assert.Equal(12, r.Settings.VersionRetentionDays);
        Assert.True(r.Settings.Paused);
    }

    [Fact]
    public void Guardar_deja_copia_bak_y_no_deja_tmp()
    {
        var path = Path.Combine(_t.Root, "config.json");
        var c = new SyncConfig();
        ConfigStore.Save(c, path);
        c.Add(SyncPair.Create("A", @"C:\a", @"D:\a"));
        ConfigStore.Save(c, path);
        Assert.True(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Empty(ConfigStore.Load(path + ".bak")!.Pairs);
    }

    [Fact]
    public void Archivo_danado_lanza_y_guarda_copia()
    {
        var path = _t.File("config.json", "{ esto no es json");
        var ex = Assert.Throws<ConfigCorruptException>(() => ConfigStore.Load(path));
        Assert.True(File.Exists(ex.CopyPath));
        Assert.Contains("dañado", ex.CopyPath);
    }

    [Fact]
    public void Ajustes_fuera_de_rango_se_corrigen()
    {
        var path = _t.File("config.json", "{\"settings\":{\"versionRetentionDays\":0,\"disconnectedAlertHours\":999999}}");
        var c = ConfigStore.Load(path)!;
        Assert.Equal(1, c.Settings.VersionRetentionDays);
        Assert.Equal(24 * 90, c.Settings.DisconnectedAlertHours);
    }

    private const string Legacy = "{\r\n    \"version\":  1,\r\n    \"pairs\":  [\r\n {\r\n \"id\":  \"037b9a0f-8931-4f8b-a630-f7795dee7189\",\r\n \"name\":  \"Proyectos Digitales\",\r\n \"source\":  \"C:\\\\Users\\\\Ana\\\\Desktop\\\\PROYECTOS\",\r\n \"backup\":  \"D:\\\\Backup\\\\Proyectos\\\\\",\r\n \"locked\":  true,\r\n \"autoSync\":  true,\r\n \"createdAt\":  \"2026-10-01T15:33:52\"\r\n }\r\n ]\r\n}";

    [Fact]
    public void Importa_la_configuracion_1x()
    {
        var path = _t.File("legacy.json", Legacy);
        var c = ConfigStore.ImportLegacy(path)!;
        var p = Assert.Single(c.Pairs);
        Assert.Equal("037b9a0f-8931-4f8b-a630-f7795dee7189", p.Id);
        Assert.Equal("Proyectos Digitales", p.Name);
        Assert.Equal(@"C:\Users\Ana\Desktop\PROYECTOS", p.Source);
        Assert.Equal(@"D:\Backup\Proyectos", p.Backup);
        Assert.True(p.Locked);
        Assert.True(p.AutoSync);
        Assert.Empty(p.Excludes);
    }

    [Fact]
    public void Importa_un_solo_par_como_objeto()
    {
        var path = _t.File("legacy.json", "{\"pairs\":{\"id\":\"a\",\"name\":\"X\",\"source\":\"C:\\\\a\",\"backup\":\"D:\\\\b\",\"locked\":false,\"autoSync\":false}}");
        var p = Assert.Single(ConfigStore.ImportLegacy(path)!.Pairs);
        Assert.False(p.Locked);
        Assert.False(p.AutoSync);
    }

    [Fact]
    public void La_importacion_no_modifica_el_archivo_original()
    {
        var legacy = _t.File("legacy\\config.json", Legacy);
        var before = SHA256.HashData(File.ReadAllBytes(legacy));
        var stamp = File.GetLastWriteTimeUtc(legacy);
        var paths = new AppPaths(Path.Combine(_t.Root, "v2"), legacy);
        var (cfg, imported) = ConfigStore.LoadOrImport(paths);
        Assert.True(imported);
        Assert.Single(cfg.Pairs);
        Assert.Equal("es", cfg.Settings.Language);
        Assert.True(cfg.Settings.WelcomeShown);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(legacy)));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(legacy));
        Assert.Equal(new[] { "config.json" }, Directory.GetFiles(Path.GetDirectoryName(legacy)!).Select(Path.GetFileName));
        Assert.True(File.Exists(paths.ConfigFile));
    }

    [Fact]
    public void Despues_de_importar_usa_su_propia_configuracion()
    {
        var legacy = _t.File("legacy\\config.json", Legacy);
        var paths = new AppPaths(Path.Combine(_t.Root, "v2"), legacy);
        var (cfg, _) = ConfigStore.LoadOrImport(paths);
        cfg.Pairs[0].Name = "Cambiado";
        ConfigStore.Save(cfg, paths.ConfigFile);
        var (again, imported) = ConfigStore.LoadOrImport(paths);
        Assert.False(imported);
        Assert.Equal("Cambiado", again.Pairs[0].Name);
        Assert.Contains("Proyectos Digitales", File.ReadAllText(legacy, Encoding.UTF8));
    }

    [Fact]
    public void Sin_nada_empieza_vacia()
    {
        var (cfg, imported) = ConfigStore.LoadOrImport(new AppPaths(Path.Combine(_t.Root, "v2"), Path.Combine(_t.Root, "no.json")));
        Assert.False(imported);
        Assert.Empty(cfg.Pairs);
        Assert.False(cfg.Settings.WelcomeShown);
    }

    [Fact] public void Legacy_danado_no_importa() => Assert.Null(ConfigStore.ImportLegacy(_t.File("l.json", "nope{")));
}

public class LockTests
{
    private static (SyncConfig, SyncPair) Make(bool locked)
    {
        var c = new SyncConfig();
        var p = SyncPair.Create("Fotos", @"C:\a", @"D:\b", locked: locked);
        c.Add(p);
        return (c, p);
    }

    [Fact] public void Bloqueada_no_se_edita() { var (c, p) = Make(true); Assert.Throws<PairLockedException>(() => c.Update(p.Id, "X", @"C:\x", @"D:\x")); Assert.Equal("Fotos", p.Name); }
    [Fact] public void Bloqueada_no_se_elimina() { var (c, p) = Make(true); Assert.Throws<PairLockedException>(() => c.Remove(p.Id)); Assert.Single(c.Pairs); }

    [Fact]
    public void Desbloqueada_se_edita_y_elimina()
    {
        var (c, p) = Make(false);
        c.Update(p.Id, "Nuevo", @"C:\n\", @"D:\n\", new[] { "*.tmp" });
        Assert.Equal("Nuevo", p.Name);
        Assert.Equal(@"C:\n", p.Source);
        Assert.Equal(new[] { "*.tmp" }, p.Excludes);
        c.Remove(p.Id);
        Assert.Empty(c.Pairs);
    }

    [Fact] public void Bloquear_y_desbloquear() { var (c, p) = Make(false); c.Lock(p.Id); Assert.True(p.Locked); c.Unlock(p.Id); Assert.False(p.Locked); }
    [Fact] public void Inexistente_lanza() => Assert.Throws<InvalidOperationException>(() => new SyncConfig().Remove("nope"));

    [Theory]
    [InlineData("Fotos", "Fotos", true)]
    [InlineData("Fotos", "  Fotos ", true)]
    [InlineData("Fotos", "fotos", false)]
    [InlineData("Fotos", "Foto", false)]
    [InlineData("Fotos", null, false)]
    [InlineData("", "", false)]
    public void Confirmacion_exacta(string expected, string? typed, bool ok) => Assert.Equal(ok, SyncConfig.UnlockConfirmation(expected, typed));
}
