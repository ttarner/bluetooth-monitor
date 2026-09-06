using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BluetoothMonitor.Services;

/// <summary>
/// Reads battery state directly from Sony controller HID input reports. Windows
/// does not reliably expose battery information for Bluetooth PlayStation
/// controllers, so this service falls back to their HID status reports.
/// </summary>
public sealed class PlayStationBatteryService : IDisposable
{
    private const ushort SonyVendorId = 0x054C;
    private const ushort DualShock4V1ProductId = 0x05C4;
    private const ushort DualShock4V2ProductId = 0x09CC;
    private const ushort DualSenseProductId = 0x0CE6;
    private const ushort DualSenseEdgeProductId = 0x0DF2;

    private readonly CancellationTokenSource _cancellation = new();
    private Task? _monitorTask;
    private readonly Dictionary<string, PlayStationBatteryStatus> _lastStatusByPath = [];

    public event EventHandler<PlayStationBatteryStatus>? BatteryStatusChanged;
    public event EventHandler<string>? DiagnosticMessage;
    public string LastDiagnostic { get; private set; } = "Not started";
    public int? LastBatteryLevel => _lastStatusByPath.Values.LastOrDefault()?.BatteryLevel;

    public void Start() => _monitorTask ??= Task.Run(() => MonitorAsync(_cancellation.Token));

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var foundController = false;
            foreach (var path in EnumerateSonyControllerPaths())
            {
                foundController = true;
                try
                {
                    await ReadControllerAsync(path, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException exception)
                {
                    // The controller disconnected or another application has it
                    // open exclusively. Enumerate again after the retry delay.
                    ReportDiagnostic($"PlayStation reader: {exception.Message}");
                }
                catch (UnauthorizedAccessException exception)
                {
                    // Some controller mappers intentionally hide/exclusively own
                    // the physical HID interface. Keep the generic path working.
                    ReportDiagnostic($"PlayStation reader: {exception.Message}");
                }
                catch (Exception exception)
                {
                    ReportDiagnostic($"PlayStation reader: {exception.Message}");
                }
            }

            try
            {
                await Task.Delay(foundController ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReadControllerAsync(string path, CancellationToken cancellationToken)
    {
        var handle = CreateFile(
            path,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOverlapped,
            IntPtr.Zero);

        var canWrite = !handle.IsInvalid;
        if (handle.IsInvalid)
        {
            handle.Dispose();
            handle = CreateFile(
                path,
                GenericRead,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOverlapped,
                IntPtr.Zero);
        }

        using (handle)
        {
            if (handle.IsInvalid)
            {
                ReportDiagnostic($"PlayStation reader: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                return;
            }

            if (!TryGetInputReportLength(handle, out var reportLength, out var capabilityError))
            {
                ReportDiagnostic($"PlayStation reader: {capabilityError}");
                return;
            }

            var controllerKind = GetControllerKind(path);
            ReportDiagnostic($"Opened {controllerKind} HID: input={reportLength}, writable={canWrite}.");
            using var stream = new FileStream(handle, canWrite ? FileAccess.ReadWrite : FileAccess.Read, reportLength, true);
            var report = new byte[reportLength];

            if (controllerKind == PlayStationControllerKind.DualShock4 && reportLength > 64)
            {
                var calibration = new byte[41];
                calibration[0] = 0x05;
                var featureRead = HidD_GetFeature(handle, calibration, calibration.Length);
                ReportDiagnostic($"DS4 calibration feature read={featureRead}, error={Marshal.GetLastWin32Error()}.");
            }

            if (controllerKind == PlayStationControllerKind.DualShock4 && canWrite && reportLength > 64)
            {
                try
                {
                    await EnableFullBluetoothReportsAsync(stream, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException exception)
                {
                    ReportDiagnostic($"PlayStation reader: {exception.Message}");
                    return;
                }
            }

            // Ask the HID stack for its current cached report. For battery
            // monitoring a periodic snapshot is enough and avoids a hot read
            // loop against the controller.
            report[0] = controllerKind switch
            {
                PlayStationControllerKind.DualShock4 => reportLength == 64 ? (byte)0x01 : (byte)0x11,
                PlayStationControllerKind.DualSense => reportLength <= 64 ? (byte)0x01 : (byte)0x31,
                _ => report[0]
            };

            if (!HidD_GetInputReport(handle, report, report.Length))
            {
                ReportDiagnostic($"PlayStation reader: Windows could not read the current {controllerKind} report.");
                return;
            }

            PublishBattery(path, controllerKind, report);
        }
    }

    private static async Task EnableFullBluetoothReportsAsync(FileStream stream, CancellationToken cancellationToken)
    {
        var output = new byte[78];
        output[0] = 0x11; // DS4 Bluetooth output report
        output[1] = 0xC4; // HID + CRC flags, 4 ms input polling

        var crc = ComputeBluetoothOutputCrc(output.AsSpan(0, 74));
        output[74] = (byte)crc;
        output[75] = (byte)(crc >> 8);
        output[76] = (byte)(crc >> 16);
        output[77] = (byte)(crc >> 24);

        await stream.WriteAsync(output, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static uint ComputeBluetoothOutputCrc(ReadOnlySpan<byte> report)
    {
        var crc = UpdateCrc32(0xFFFFFFFF, 0xA2); // DS4 output-report seed
        foreach (var value in report)
            crc = UpdateCrc32(crc, value);
        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0u);
        return crc;
    }

    private void PublishBattery(string path, PlayStationControllerKind controllerKind, ReadOnlySpan<byte> report)
    {
        var status = ParseStatus(controllerKind, report);
        if (status is null || (_lastStatusByPath.TryGetValue(path, out var previous) && previous == status))
            return;

        _lastStatusByPath[path] = status;
        ReportDiagnostic($"Published {status.ControllerKind} battery {status.BatteryLevel}% charging={status.IsCharging} from '{path}'.");
        BatteryStatusChanged?.Invoke(this, status);
    }

    private void ReportDiagnostic(string message)
    {
        LastDiagnostic = message;
        DiagnosticMessage?.Invoke(this, message);
    }

    internal static int? ParseDualShock4Battery(ReadOnlySpan<byte> report) =>
        ParseDualShock4Status(report)?.BatteryLevel;

    internal static int? ParseDualSenseBattery(ReadOnlySpan<byte> report) =>
        ParseDualSenseStatus(report)?.BatteryLevel;

    internal static PlayStationBatteryStatus? ParseStatus(PlayStationControllerKind controllerKind, ReadOnlySpan<byte> report) =>
        controllerKind switch
        {
            PlayStationControllerKind.DualShock4 => ParseDualShock4Status(report),
            PlayStationControllerKind.DualSense => ParseDualSenseStatus(report),
            _ => null
        };

    internal static PlayStationBatteryStatus? ParseDualShock4Status(ReadOnlySpan<byte> report)
    {
        // The common DS4 report starts at byte 3 over Bluetooth (report 0x11)
        // and byte 1 over USB (report 0x01). status[0] is byte 29 in that block.
        var statusOffset = report.Length >= 33 && report[0] == 0x11
            ? 32
            : report.Length == 64 && report[0] == 0x01
                ? 30
                : -1;

        if (statusOffset < 0) return null;

        var status = report[statusOffset];
        var batteryData = status & 0x0F;
        var cableConnected = (status & 0x10) != 0;

        if (cableConnected)
        {
            if (batteryData == 11) return new PlayStationBatteryStatus(PlayStationControllerKind.DualShock4, 100, false);
            if (batteryData > 10) return null; // charging/voltage error states
        }

        // Sony reports 0..9 as ten-percent buckets. Match the Linux
        // hid-playstation driver by displaying each bucket's midpoint.
        var batteryLevel = batteryData < 10 ? batteryData * 10 + 5 : 100;
        return new PlayStationBatteryStatus(PlayStationControllerKind.DualShock4, batteryLevel, cableConnected);
    }

    internal static PlayStationBatteryStatus? ParseDualSenseStatus(ReadOnlySpan<byte> report)
    {
        // DualSense uses report 0x01 over USB and 0x31 over Bluetooth. Windows
        // adapters/drivers do not always hand back the Bluetooth payload at the
        // exact same byte alignment, so we try the known adjacent offsets.
        if (report.Length >= 64 && report[0] == 0x01)
            return ParseDualSenseStatusAtOffset(report, 53);

        if (report.Length >= 78 && report[0] == 0x31)
        {
            var aligned = ParseDualSenseStatusAtOffset(report, 54);
            var shifted = ParseDualSenseStatusAtOffset(report, 55);
            return ChoosePreferredDualSenseStatus(aligned, shifted);
        }

        return null;
    }

    private static PlayStationBatteryStatus? ParseDualSenseStatusAtOffset(ReadOnlySpan<byte> report, int statusOffset)
    {
        if ((uint)statusOffset >= (uint)report.Length) return null;

        var status = report[statusOffset];
        var batteryData = status & 0x0F;
        if (batteryData > 10)
            return null;

        var cableConnected = (status & 0x20) != 0;
        var batteryLevel = batteryData switch
        {
            0 => 0,
            < 10 => batteryData * 10 + 5,
            _ => 100
        };

        return new PlayStationBatteryStatus(PlayStationControllerKind.DualSense, batteryLevel, cableConnected);
    }

    private static PlayStationBatteryStatus? ChoosePreferredDualSenseStatus(
        PlayStationBatteryStatus? primary,
        PlayStationBatteryStatus? secondary)
    {
        if (primary is null) return secondary;
        if (secondary is null) return primary;

        if (primary.BatteryLevel == 0 && secondary.BatteryLevel > 0)
            return secondary;

        if (!primary.IsCharging && secondary.IsCharging)
            return secondary;

        return primary;
    }

    private static IEnumerable<string> EnumerateSonyControllerPaths()
    {
        HidD_GetHidGuid(out var hidGuid);
        var deviceInfoSet = SetupDiGetClassDevs(
            ref hidGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);

        if (deviceInfoSet == InvalidHandleValue) yield break;

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SpDeviceInterfaceData { Size = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                {
                    if (Marshal.GetLastWin32Error() == ErrorNoMoreItems) yield break;
                    continue;
                }

                SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
                if (requiredSize == 0) continue;

                var detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailBuffer, requiredSize, out _, IntPtr.Zero))
                        continue;

                    var path = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4));
                    if (path is null || !IsSupportedController(path)) continue;
                    yield return path;
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static bool IsSupportedController(string path)
    {
        using var handle = CreateFile(
            path,
            0,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid) return false;
        var attributes = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
        return HidD_GetAttributes(handle, ref attributes)
               && attributes.VendorId == SonyVendorId
               && attributes.ProductId is DualShock4V1ProductId or DualShock4V2ProductId or DualSenseProductId or DualSenseEdgeProductId;
    }

    private static PlayStationControllerKind GetControllerKind(string path)
    {
        using var handle = CreateFile(
            path,
            0,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid) return PlayStationControllerKind.Unknown;

        var attributes = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
        if (!HidD_GetAttributes(handle, ref attributes))
            return PlayStationControllerKind.Unknown;

        return attributes.ProductId switch
        {
            DualShock4V1ProductId or DualShock4V2ProductId => PlayStationControllerKind.DualShock4,
            DualSenseProductId or DualSenseEdgeProductId => PlayStationControllerKind.DualSense,
            _ => PlayStationControllerKind.Unknown
        };
    }

    private static bool TryGetInputReportLength(SafeFileHandle handle, out int reportLength, out string? error)
    {
        if (!HidD_GetPreparsedData(handle, out var preparsedData))
        {
            reportLength = 0;
            error = "Windows could not read the controller HID capabilities.";
            return false;
        }

        try
        {
            var caps = new HidpCaps { Reserved = new ushort[17] };
            var status = HidP_GetCaps(preparsedData, ref caps);
            if (status < 0 || caps.InputReportByteLength == 0)
            {
                reportLength = 0;
                error = $"Windows returned invalid controller HID capabilities (0x{status:X8}).";
                return false;
            }

            reportLength = caps.InputReportByteLength;
            error = null;
            return true;
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const int ErrorNoMoreItems = 259;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetInputReport(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, ref HidpCaps capabilities);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}

public enum PlayStationControllerKind
{
    Unknown,
    DualShock4,
    DualSense
}

public sealed record PlayStationBatteryStatus(PlayStationControllerKind ControllerKind, int BatteryLevel, bool IsCharging);
