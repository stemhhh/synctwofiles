using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SyncTwoFiles.Core;

public sealed class ConfigCorruptException : Exception
{
    public ConfigCorruptException(string message, string copyPath) : base(message) => CopyPath = copyPath;
    public string CopyPath { get; }
}

public static class ConfigStore
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static SyncConfig? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var cfg = JsonSerializer.Deserialize<SyncConfig>(File.ReadAllText(path, Encoding.UTF8), Json)
                      ?? throw new JsonException("empty");
            cfg.Pairs ??= new List<SyncPair>();
            cfg.Pairs.RemoveAll(p => p is null);
            foreach (var p in cfg.Pairs) p.NormalizeInPlace();
            cfg.Settings ??= new AppSettings();
            cfg.Settings.Clamp();
            cfg.Version = 2;
            return cfg;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            var copy = $"{path}.dañado-{DateTime.Now:yyyyMMdd-HHmmss}";
            try { File.Copy(path, copy, true); } catch { }
            throw new ConfigCorruptException(L.T("err.configCorrupt", copy), copy);
        }
    }

    public static void Save(SyncConfig config, string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Json), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", true);
        else File.Move(tmp, path);
    }

    // Lee la configuracion de la version 1.x (PowerShell) sin modificar el archivo original.
    public static SyncConfig? ImportLegacy(string legacyPath)
    {
        if (!File.Exists(legacyPath)) return null;
        JsonNode? root;
        try
        {
            using var fs = new FileStream(legacyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8, true);
            root = JsonNode.Parse(sr.ReadToEnd(), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch { return null; }
        var cfg = new SyncConfig();
        var pairs = root?["pairs"];
        IEnumerable<JsonNode?> items = pairs switch
        {
            JsonArray a => a,
            JsonObject o => new[] { o },
            _ => Array.Empty<JsonNode?>(),
        };
        foreach (var n in items)
        {
            if (n is not JsonObject o) continue;
            var p = new SyncPair
            {
                Id = Str(o, "id") is { Length: > 0 } id ? id : Guid.NewGuid().ToString(),
                Name = Str(o, "name"),
                Source = Str(o, "source"),
                Backup = Str(o, "backup"),
                Locked = Bool(o, "locked"),
                AutoSync = Bool(o, "autoSync"),
                CreatedAt = Str(o, "createdAt"),
            };
            p.NormalizeInPlace();
            if (p.Source.Length > 0 && p.Backup.Length > 0) cfg.Pairs.Add(p);
        }
        return cfg;
    }

    private static string Str(JsonObject o, string key) =>
        o.TryGetPropertyValue(key, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : "";

    private static bool Bool(JsonObject o, string key) =>
        o.TryGetPropertyValue(key, out var v) && v is JsonValue jv && jv.TryGetValue<bool>(out var b) && b;

    // Carga la configuracion; la primera vez importa la de la 1.x. Devuelve si hubo importacion.
    public static (SyncConfig Config, bool Imported) LoadOrImport(AppPaths paths)
    {
        var cfg = Load(paths.ConfigFile);
        if (cfg is not null) return (cfg, false);
        var legacy = ImportLegacy(paths.LegacyConfigFile);
        if (legacy is null) return (new SyncConfig(), false);
        legacy.Settings.WelcomeShown = legacy.Pairs.Count > 0;
        legacy.Settings.Language = "es";
        Save(legacy, paths.ConfigFile);
        return (legacy, true);
    }
}
