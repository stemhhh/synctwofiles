using System.IO;
using System.Linq;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.Tests;

public class AppInfoTests
{
    [Fact] public void Nombre_y_autor() { Assert.Equal("Sync two File", AppInfo.Name); Assert.Equal("STEM", AppInfo.Author); }
    [Fact] public void Version_2() => Assert.StartsWith("2.", AppInfo.Version);

    [Fact]
    public void Rutas_de_datos_separadas_de_la_1x()
    {
        var p = AppPaths.Default();
        Assert.EndsWith(".json", p.LegacyConfigFile);
        Assert.NotEqual(p.ConfigFile, p.LegacyConfigFile);
        Assert.StartsWith(p.DataDir, p.LogDir);
        Assert.StartsWith(p.DataDir, p.StateFile);
    }
}

public class PathTests
{
    [Theory]
    [InlineData("E:", @"E:\")]
    [InlineData(@"E:\", @"E:\")]
    [InlineData(@"C:\Datos\", @"C:\Datos")]
    [InlineData(@"  C:\Datos\Sub\\  ", @"C:\Datos\Sub")]
    [InlineData("", "")]
    public void Normaliza(string input, string expected) => Assert.Equal(expected, PathUtil.Normalize(input));

    [Fact] public void Normaliza_separadores() => Assert.Equal(@"C:\a\b", PathUtil.Normalize("C:/a/b/"));

    [Theory]
    [InlineData(@"E:\", true)]
    [InlineData("E:", true)]
    [InlineData(@"E:\Backup", false)]
    [InlineData(@"\\servidor\compartida", true)]
    [InlineData(@"\\servidor\compartida\Backup", false)]
    public void Detecta_raiz(string path, bool root) => Assert.Equal(root, PathUtil.IsDriveRoot(path));

    [Theory]
    [InlineData(@"C:\Users\Ana\Fotos", "Fotos")]
    [InlineData(@"C:\Users\Ana\Fotos\", "Fotos")]
    [InlineData(@"E:\", @"E:\")]
    [InlineData("E:", @"E:\")]
    public void Etiqueta(string path, string label) => Assert.Equal(label, PathUtil.Label(path));

    [Fact] public void Dentro() => Assert.True(PathUtil.IsInside(@"C:\a\b", @"C:\A"));
    [Fact] public void No_confunde_prefijos() => Assert.False(PathUtil.IsInside(@"C:\ab", @"C:\a"));
    [Fact] public void Dentro_de_raiz() => Assert.True(PathUtil.IsInside(@"E:\Backup", @"E:\"));
    [Fact] public void Solapamiento_en_ambos_sentidos() { Assert.True(PathUtil.Overlap(@"C:\a", @"C:\a\b")); Assert.True(PathUtil.Overlap(@"C:\a\b", @"C:\a")); }
    [Fact] public void Raiz_de_unidad() => Assert.Equal(@"E:\", PathUtil.DriveRoot(@"E:\Backup\x"));
}

public class ArgsTests
{
    private static readonly SyncPair P = SyncPair.Create("Fotos", @"C:\Mis Fotos", @"E:\Backup Fotos", excludes: new[] { "node_modules", "*.tmp" });

    [Fact]
    public void Automatica_es_aditiva()
    {
        var a = RoboArgs.AutoSync(P);
        Assert.Equal(new[] { P.Source, P.Backup, "/E" }, a.Take(3));
        Assert.DoesNotContain("/MIR", a);
        Assert.DoesNotContain("/PURGE", a);
    }

    [Fact]
    public void Monitor_vigila_y_escribe_unicode()
    {
        var a = RoboArgs.Monitor(P, @"C:\logs\x.log");
        Assert.Contains("/MON:1", a);
        Assert.Contains("/MOT:1", a);
        Assert.Contains(@"/UNILOG+:C:\logs\x.log", a);
    }

    [Fact] public void Espejo_usa_MIR() => Assert.Contains("/MIR", RoboArgs.Mirror(P));

    [Fact]
    public void Restaurar_va_del_backup_al_origen_sin_pisar_lo_nuevo()
    {
        var a = RoboArgs.Restore(P);
        Assert.Equal(P.Backup, a[0]);
        Assert.Equal(P.Source, a[1]);
        Assert.Contains("/XO", a);
        Assert.DoesNotContain("/MIR", a);
    }

    [Fact]
    public void Todas_excluyen_sistema_versiones_y_reglas_del_usuario()
    {
        foreach (var a in new[] { RoboArgs.AutoSync(P), RoboArgs.Mirror(P), RoboArgs.Restore(P), RoboArgs.RestoreDir("a", "b", true, P.Rules) })
        {
            var xd = a.IndexOf("/XD");
            Assert.True(xd > 0);
            Assert.Contains("$RECYCLE.BIN", a);
            Assert.Contains("System Volume Information", a);
            Assert.Contains(ExcludeRules.VersionsDirName, a);
            Assert.Contains("node_modules", a);
            Assert.Contains("/XF", a);
            Assert.Contains("*.tmp", a);
        }
    }

    [Fact]
    public void Sin_reglas_no_agrega_XF()
    {
        var p = SyncPair.Create("x", @"C:\a", @"D:\b");
        Assert.DoesNotContain("/XF", RoboArgs.AutoSync(p));
    }

    [Fact]
    public void Restaurar_archivo_opcional_XO()
    {
        Assert.Contains("/XO", RoboArgs.RestoreFile("a", "b", "c.txt", true));
        Assert.DoesNotContain("/XO", RoboArgs.RestoreFile("a", "b", "c.txt", false));
        Assert.Equal("c.txt", RoboArgs.RestoreFile("a", "b", "c.txt", false)[2]);
    }

    [Fact]
    public void Recuperar_version_opcional_XO()
    {
        Assert.Contains("/XO", RoboArgs.RecoverVersion("v", "s"));
        Assert.DoesNotContain("/XO", RoboArgs.RecoverVersion("v", "s", skipNewer: false));
    }

    [Theory]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(3, true)] [InlineData(7, true)]
    [InlineData(8, false)] [InlineData(16, false)] [InlineData(-1, false)]
    public void Codigos_de_salida(int code, bool ok) => Assert.Equal(ok, RoboArgs.IsSuccess(code));

    [Fact] public void Comillas_con_espacios() => Assert.Equal("\"C:\\Mis Fotos\" /E", RoboArgs.ToArgumentString(new[] { @"C:\Mis Fotos", "/E" }));
    [Fact] public void Barra_final_entre_comillas_se_duplica() => Assert.Equal("\"E:\\Mi Disco\\\\\"", RoboArgs.ToArgumentString(new[] { @"E:\Mi Disco\" }));
    [Fact] public void Sin_espacios_sin_comillas() => Assert.Equal(@"E:\ /E", RoboArgs.ToArgumentString(new[] { @"E:\", "/E" }));
    [Fact] public void Argumento_vacio() => Assert.Equal("\"\"", RoboArgs.ToArgumentString(new[] { "" }));
}

public class ExcludeTests
{
    [Fact] public void Limpia_duplicados_y_barras() => Assert.Equal(new[] { "node_modules", ".git" }, ExcludeRules.Clean(new[] { " node_modules\\ ", "NODE_MODULES", ".git/", "", "  " }));
    [Fact] public void Parse_por_lineas_y_punto_y_coma() => Assert.Equal(new[] { "a", "b", "*.tmp" }, ExcludeRules.Parse("a\r\nb; *.tmp\n"));

    [Theory]
    [InlineData("node_modules", "node_modules", true)]
    [InlineData("NODE_MODULES", "node_modules", true)]
    [InlineData("foto.tmp", "*.tmp", true)]
    [InlineData("foto.tmpx", "*.tmp", false)]
    [InlineData("a1.log", "a?.log", true)]
    [InlineData("a12.log", "a?.log", false)]
    [InlineData("foto.jpg", "*.tmp", false)]
    public void Comodines(string name, string pattern, bool match) => Assert.Equal(match, ExcludeRules.Matches(name, pattern));

    [Fact]
    public void Siempre_excluye_sistema_y_versiones()
    {
        var r = new ExcludeRules();
        Assert.True(r.IsDirExcluded("$RECYCLE.BIN"));
        Assert.True(r.IsDirExcluded("System Volume Information"));
        Assert.True(r.IsDirExcluded(ExcludeRules.VersionsDirName));
        Assert.False(r.IsFileExcluded("foto.jpg"));
    }

    [Theory]
    [InlineData(@"C:\carpeta")] [InlineData("a/b")] [InlineData("..")] [InlineData("x:y")]
    public void Rechaza_rutas(string bad) => Assert.NotNull(ExcludeRules.Validate(new[] { bad }));

    [Fact] public void Acepta_nombres() => Assert.Null(ExcludeRules.Validate(new[] { "node_modules", "*.tmp", ".git", "Thumbs.db" }));
}
