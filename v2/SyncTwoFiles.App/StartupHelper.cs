using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using SyncTwoFiles.Core;

namespace SyncTwoFiles.App;

// Inicio con Windows: StartupTask dentro de MSIX; clave Run del usuario si se instalo sin paquete.
internal static class StartupHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "SyncTwoFiles.v2";

    public static bool LaunchedByStartupTask()
    {
        if (!Native.IsPackaged) return false;
        try
        {
            var args = Windows.ApplicationModel.AppInstance.GetActivatedEventArgs();
            return args?.Kind == Windows.ApplicationModel.Activation.ActivationKind.StartupTask;
        }
        catch { return false; }
    }

    public static async Task ApplyAsync(bool enable)
    {
        try
        {
            if (Native.IsPackaged)
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync(AppInfo.StartupTaskId);
                if (enable && task.State == Windows.ApplicationModel.StartupTaskState.Disabled) await task.RequestEnableAsync();
                else if (!enable && task.State == Windows.ApplicationModel.StartupTaskState.Enabled) task.Disable();
                return;
            }
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable && Environment.ProcessPath is { } exe) key.SetValue(RunValue, $"\"{exe}\" --background");
            else key.DeleteValue(RunValue, false);
        }
        catch { }
    }
}
