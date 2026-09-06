using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace BluetoothMonitor.Services;

public sealed class SystemStatsService
{
    private ulong _lastIdleTime;
    private ulong _lastKernelTime;
    private ulong _lastUserTime;
    private bool _hasPreviousCpuSample;
    private long _lastBytesReceived;
    private long _lastBytesSent;
    private DateTime _lastSampleUtc;
    private bool _hasPreviousNetworkSample;

    public SystemStatsSnapshot Capture()
    {
        var now = DateTime.Now;
        var nowUtc = DateTime.UtcNow;
        var cpuPercent = CaptureCpuUsagePercent();
        var (usedMemoryPercent, _, _) = CaptureMemoryUsagePercent();
        var (downloadRate, uploadRate) = CaptureNetworkRates(nowUtc);

        return new SystemStatsSnapshot(
            now.ToString("HH:mm"),
            cpuPercent,
            usedMemoryPercent,
            downloadRate,
            uploadRate);
    }

    private int? CaptureCpuUsagePercent()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return null;

        var idle = ToUInt64(idleTime);
        var kernel = ToUInt64(kernelTime);
        var user = ToUInt64(userTime);

        if (!_hasPreviousCpuSample)
        {
            _lastIdleTime = idle;
            _lastKernelTime = kernel;
            _lastUserTime = user;
            _hasPreviousCpuSample = true;
            return null;
        }

        var idleDelta = idle - _lastIdleTime;
        var kernelDelta = kernel - _lastKernelTime;
        var userDelta = user - _lastUserTime;
        var totalDelta = kernelDelta + userDelta;

        _lastIdleTime = idle;
        _lastKernelTime = kernel;
        _lastUserTime = user;

        if (totalDelta == 0) return null;

        var usage = 100d * (1d - idleDelta / (double)totalDelta);
        return (int)Math.Round(Math.Clamp(usage, 0, 100));
    }

    private static (int? UsedPercent, ulong TotalBytes, ulong AvailableBytes) CaptureMemoryUsagePercent()
    {
        var memoryStatus = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(memoryStatus))
            return (null, 0, 0);

        return ((int)memoryStatus.dwMemoryLoad, memoryStatus.ullTotalPhys, memoryStatus.ullAvailPhys);
    }

    private (double? DownloadBytesPerSecond, double? UploadBytesPerSecond) CaptureNetworkRates(DateTime nowUtc)
    {
        long totalReceived = 0;
        long totalSent = 0;

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                continue;

            switch (networkInterface.NetworkInterfaceType)
            {
                case NetworkInterfaceType.Loopback:
                case NetworkInterfaceType.Tunnel:
                    continue;
            }

            try
            {
                var statistics = networkInterface.GetIPv4Statistics();
                totalReceived += statistics.BytesReceived;
                totalSent += statistics.BytesSent;
            }
            catch
            {
                // Skip interfaces that do not expose IPv4 counters.
            }
        }

        if (!_hasPreviousNetworkSample)
        {
            _lastBytesReceived = totalReceived;
            _lastBytesSent = totalSent;
            _lastSampleUtc = nowUtc;
            _hasPreviousNetworkSample = true;
            return (null, null);
        }

        var elapsedSeconds = Math.Max((nowUtc - _lastSampleUtc).TotalSeconds, 0.001d);
        var downloadRate = Math.Max(0, totalReceived - _lastBytesReceived) / elapsedSeconds;
        var uploadRate = Math.Max(0, totalSent - _lastBytesSent) / elapsedSeconds;

        _lastBytesReceived = totalReceived;
        _lastBytesSent = totalSent;
        _lastSampleUtc = nowUtc;

        return (downloadRate, uploadRate);
    }

    private static ulong ToUInt64(FileTime fileTime) =>
        ((ulong)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime lpIdleTime, out FileTime lpKernelTime, out FileTime lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}

public readonly record struct SystemStatsSnapshot(
    string TimeText,
    int? CpuUsagePercent,
    int? RamUsagePercent,
    double? DownloadBytesPerSecond,
    double? UploadBytesPerSecond);
