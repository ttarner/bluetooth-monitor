using Microsoft.Win32;

namespace BluetoothMonitor.Services;

public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BluetoothBatteryMonitor";

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (enabled && Environment.ProcessPath is string executablePath)
                key.SetValue(ValueName, $"\"{executablePath}\" --startup", RegistryValueKind.String);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Keep the app usable if registry access is restricted by policy.
        }
    }
}
