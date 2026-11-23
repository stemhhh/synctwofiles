using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.Tests;

public class LocalizationTests
{
    private static string[] SourceFiles() =>
        new[] { "SyncTwoFiles.Core", "SyncTwoFiles.App" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(TestUtil.RepoRoot(), d), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();

    [Fact]
    public void Todas_las_claves_usadas_existen()
    {
        var used = SourceFiles().SelectMany(f =>
        {
            var text = File.ReadAllText(f);
            return Regex.Matches(text, @"L\.T\(""([^""]+)""").Select(m => m.Groups[1].Value)
                .Concat(Regex.Matches(text, @"\{l:T (\w+\.[\w\.]+)\}").Select(m => m.Groups[1].Value))
                .Concat(Regex.Matches(text, @"""((?:welcome|disk)\.[\w\.]+)""").Select(m => m.Groups[1].Value));
        }).Distinct().ToList();
        Assert.True(used.Count > 150, $"Solo se encontraron {used.Count} claves");
        var missing = used.Where(k => !L.All.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Faltan: " + string.Join(", ", missing));
    }

    [Fact]
    public void Cada_texto_tiene_espanol_e_ingles_con_los_mismos_marcadores()
    {
        foreach (var (key, (es, en)) in L.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(es), key);
            Assert.False(string.IsNullOrWhiteSpace(en), key);
            var a = Regex.Matches(es, @"\{\d\}").Select(m => m.Value).Distinct().OrderBy(x => x);
            var b = Regex.Matches(en, @"\{\d\}").Select(m => m.Value).Distinct().OrderBy(x => x);
            Assert.True(a.SequenceEqual(b), key);
        }
    }

    [Fact]
    public void Cambia_de_idioma()
    {
        var before = L.Language;
        try
        {
            L.Language = "es";
            Assert.Equal("Cancelar", L.T("common.cancel"));
            L.Language = "en";
            Assert.Equal("Cancel", L.T("common.cancel"));
            Assert.Equal("Version 2", L.T("app.version", 2));
            Assert.Equal("clave.inexistente", L.T("clave.inexistente"));
        }
        finally { L.Language = before; }
    }

    [Fact] public void Automatico_resuelve_a_es_o_en() => Assert.Contains(L.Resolve(""), new[] { "es", "en" });
}

public class AppSourceTests
{
    [Fact]
    public void No_contiene_rutas_personales()
    {
        var root = TestUtil.RepoRoot();
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml") || f.EndsWith(".ps1") || f.EndsWith(".xml") || f.EndsWith(".md"))
                        && !f.Contains(@"\obj\") && !f.Contains(@"\bin\") && !f.Contains(@"\dist\") && !f.Contains("SyncTwoFiles.Tests"));
        foreach (var f in files)
            Assert.DoesNotMatch(@"\\enman\\|Almacen 1|PROYECTOS DIGITALES", File.ReadAllText(f));
    }

    [Fact]
    public void La_version_del_proyecto_es_la_de_la_app()
    {
        var props = File.ReadAllText(Path.Combine(TestUtil.RepoRoot(), "Directory.Build.props"));
        Assert.Contains($"<Version>{AppInfo.Version}</Version>", props);
    }

    [Fact]
    public void El_icono_existe()
    {
        var assets = Path.Combine(TestUtil.RepoRoot(), "..", "assets");
        Assert.True(File.Exists(Path.Combine(assets, "SyncTwoFiles.ico")));
        Assert.True(File.Exists(Path.Combine(assets, "SyncTwoFiles.png")));
    }
}
