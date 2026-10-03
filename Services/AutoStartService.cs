using Microsoft.Win32;

namespace MiniPlayer.Services;

/// <summary>
/// "Iniciar com o Windows". Installer build: the classic Run registry key. Store build: the
/// package's StartupTask (registry writes are virtualized inside MSIX, so Run would not work).
/// </summary>
public static class AutoStartService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "MiniPlayer";
    /// <summary>Must match the StartupTask TaskId in the Store package manifest.</summary>
    public const string StoreTaskId = "MiniPlayerStartup";

    public static async Task<bool> IsEnabledAsync()
    {
#if STORE
        var task = await Windows.ApplicationModel.StartupTask.GetAsync(StoreTaskId);
        return task.State is Windows.ApplicationModel.StartupTaskState.Enabled
            or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
#else
        await Task.CompletedTask;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
#endif
    }

    /// <returns>The resulting state (Windows may refuse, e.g. if the user turned it off in Task Manager).</returns>
    public static async Task<bool> SetAsync(bool enabled)
    {
#if STORE
        var task = await Windows.ApplicationModel.StartupTask.GetAsync(StoreTaskId);
        if (enabled) await task.RequestEnableAsync();
        else task.Disable();
        return await IsEnabledAsync();
#else
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enabled) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        return await IsEnabledAsync();
#endif
    }
}
