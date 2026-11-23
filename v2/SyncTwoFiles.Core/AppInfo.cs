using System;
using System.IO;

namespace SyncTwoFiles.Core;

public static class AppInfo
{
    public const string Name = "Sync two File";
    public const string Author = "STEM";
    public const string Id = "SyncTwoFiles";
    public const string AppUserModelId = "STEM.SyncTwoFiles";
    public const string StartupTaskId = "SyncTwoFilesStartup";

    public static string Version =>
        (typeof(AppInfo).Assembly.GetName().Version ?? new Version(2, 0, 0)).ToString(3);
}

public sealed class AppPaths
{
    public AppPaths(string dataDir, string legacyConfigFile)
    {
        DataDir = dataDir;
        LegacyConfigFile = legacyConfigFile;
    }

    public string DataDir { get; }
    public string LegacyConfigFile { get; }
    public string ConfigFile => Path.Combine(DataDir, "config.json");
    public string StateFile => Path.Combine(DataDir, "state.json");
    public string LogDir => Path.Combine(DataDir, "logs");

    public const string DataDirVariable = "SYNCTWOFILES_DATA";

    // En MSIX, %LOCALAPPDATA% se redirige a la carpeta privada del paquete de forma transparente.
    // SYNCTWOFILES_DATA permite probar la app con datos aparte, sin tocar la configuracion real.
    public static AppPaths Default()
    {
        var custom = Environment.GetEnvironmentVariable(DataDirVariable);
        if (!string.IsNullOrWhiteSpace(custom))
            return new AppPaths(custom, Path.Combine(custom, "legacy-config.json"));
        return new AppPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Id, "v2"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Id, "config.json"));
    }
}
