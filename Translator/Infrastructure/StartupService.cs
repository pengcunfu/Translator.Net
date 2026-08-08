using Microsoft.Win32;

namespace LavaTranslator.Infrastructure;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = "LavaTranslator";
    private const string StartupArgument = "--startup";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(RegistryValueName) is string;
    }

    public static bool SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (key is null)
            return false;

        if (enabled)
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                return false;

            key.SetValue(RegistryValueName, $"\"{exePath}\" {StartupArgument}");
        }
        else
        {
            key.DeleteValue(RegistryValueName, throwOnMissingValue: false);
        }

        return true;
    }

    public static void SyncWithConfig(bool runAtStartup)
    {
        if (IsEnabled() != runAtStartup)
            SetEnabled(runAtStartup);
    }

    public static bool IsStartupLaunch(string[] args) =>
        args.Any(a => string.Equals(a, StartupArgument, StringComparison.OrdinalIgnoreCase));
}
