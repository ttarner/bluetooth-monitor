using System;
using System.IO;

public static class SimpleLogger
{
    private static string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BluetoothMonitor", "debug.log");
    private static object _lock = new object();
    
    public static void Log(string message)
    {
        try {
            lock (_lock) {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n$");
            }
        } catch { }
    }
}
