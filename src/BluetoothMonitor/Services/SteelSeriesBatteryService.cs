using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BluetoothMonitor.Services;

/// <summary>
/// Reads battery state directly from supported SteelSeries Arctis USB dongles.
/// These headsets use proprietary 2.4 GHz wireless and do not publish a Windows
/// Bluetooth battery property. The read-only status packet follows the protocol
/// documented by the open-source HeadsetControl project.
/// </summary>
public sealed class SteelSeriesBatteryService : IDisposable
{
    private const ushort SteelSeriesVendorId = 0x1038;
    private const ushort Arctis7PlusProductId = 0x220E;
    private const ushort Arctis7PlusPs5ProductId = 0x2212;
    private const ushort Arctis7PlusXboxProductId = 0x2216;
    private const ushort Arctis7PlusDestinyProductId = 0x2236;
    private const ushort SteelSeriesUsagePage = 0xFFC0;
    private const ushort SteelSeriesUsage = 0x0001;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);
    private Task? _monitorTask;
    private SteelSeriesHeadsetStatus? _lastPublishedStatus;

    public event EventHandler<SteelSeriesHeadsetStatus>? StatusChanged;
    public event EventHandler<string>? DiagnosticMessage;

    public string LastDiagnostic { get; private set; } = "Not started";

    public void Start() => _monitorTask ??= Task.Run(() => MonitorAsync(_cancellation.Token));

    public void Refresh()
    {
        _lastPublishedStatus = null;
        if (_refreshSignal.CurrentCount == 0)
            _refreshSignal.Release();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var status = await QueryStatusAsync(cancellationToken).ConfigureAwait(false);
                Publish(status);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // SteelSeries GG may briefly have the HID interface busy. Keep
                // the last good reading and retry instead of flickering the UI.
                ReportDiagnostic($"SteelSeries reader: {exception.Message}");
            }

            try
            {
                await _refreshSignal.WaitAsync(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<SteelSeriesHeadsetStatus> QueryStatusAsync(CancellationToken cancellationToken)
    {
        var dongle = EnumerateSupportedDongles().FirstOrDefault();
        if (dongle is null)
            return SteelSeriesHeadsetStatus.Disconnected;

        using var handle = CreateFile(
            dongle.Path,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOverlapped,
            IntPtr.Zero);

        if (handle.IsInvalid)
            throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);

        var bufferSize = Math.Max(dongle.InputReportLength, dongle.OutputReportLength);
        using var stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize, true);

        // Modern SteelSeries status request. The leading zero is the HID
        // report ID; Windows requires the full output-report length.
        var request = new byte[dongle.OutputReportLength];
        request[0] = 0x00;
        request[1] = 0xB0;
        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var response = new byte[dongle.InputReportLength];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var bytesRead = await stream.ReadAsync(response, timeout.Token).ConfigureAwait(false);
        if (bytesRead < 4)
            throw new IOException("The Arctis dongle returned an incomplete status report.");

        var status = ParseStatus(response.AsSpan(0, bytesRead), dongle.ProductId);
        ReportDiagnostic(status.IsConnected
            ? $"Read {status.Name} battery: {status.BatteryLevel}%."
            : $"{status.Name} dongle found; headset is offline.");
        return status;
    }

    internal static SteelSeriesHeadsetStatus ParseStatus(ReadOnlySpan<byte> response, ushort productId)
    {
        // Windows FileStream reads include the leading zero report ID. HIDAPI,
        // used by the protocol reference, strips it. Accept either layout.
        var payloadOffset = response.Length >= 5 && response[0] == 0x00 && response[1] == 0xB0
            ? 1
            : 0;

        if (response.Length < payloadOffset + 4)
            throw new ArgumentException("A SteelSeries status report must contain at least four bytes.", nameof(response));

        var name = GetDeviceName(productId);
        const byte headsetOffline = 0x01;
        var isCharging = response[payloadOffset + 3] == 0x01;
        if (response[payloadOffset + 1] == headsetOffline)
            return new SteelSeriesHeadsetStatus(name, false, null, isCharging);

        var batteryLevel = Math.Clamp(response[payloadOffset + 2], (byte)0, (byte)4) * 25;
        return new SteelSeriesHeadsetStatus(name, true, batteryLevel, isCharging);
    }

    private void Publish(SteelSeriesHeadsetStatus status)
    {
        if (status == _lastPublishedStatus) return;
        _lastPublishedStatus = status;
        StatusChanged?.Invoke(this, status);
    }

    private void ReportDiagnostic(string message)
    {
        LastDiagnostic = message;
        DiagnosticMessage?.Invoke(this, message);
    }

    private static string GetDeviceName(ushort productId) => productId switch
    {
        Arctis7PlusPs5ProductId => "Arctis 7+ PS5",
        Arctis7PlusXboxProductId => "Arctis 7+ Xbox",
        Arctis7PlusDestinyProductId => "Arctis 7+ Destiny",
        _ => "Arctis 7+"
    };

    private static bool IsSupportedProduct(ushort productId) => productId is
        Arctis7PlusProductId or
        Arctis7PlusPs5ProductId or
        Arctis7PlusXboxProductId or
        Arctis7PlusDestinyProductId;

    private static IEnumerable<SteelSeriesDongle> EnumerateSupportedDongles()
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
                    if (path is null || TryGetSupportedDongle(path) is not { } dongle) continue;
                    yield return dongle;
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

    private static SteelSeriesDongle? TryGetSupportedDongle(string path)
    {
        using var handle = CreateFile(
            path,
            0,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid) return null;

        var attributes = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
        if (!HidD_GetAttributes(handle, ref attributes)
            || attributes.VendorId != SteelSeriesVendorId
            || !IsSupportedProduct(attributes.ProductId)
            || !HidD_GetPreparsedData(handle, out var preparsedData))
            return null;

        try
        {
            var capabilities = new HidpCaps { Reserved = new ushort[17] };
            if (HidP_GetCaps(preparsedData, ref capabilities) < 0
                || capabilities.UsagePage != SteelSeriesUsagePage
                || capabilities.Usage != SteelSeriesUsage
                || capabilities.InputReportByteLength < 4
                || capabilities.OutputReportByteLength < 2)
                return null;

            return new SteelSeriesDongle(
                path,
                attributes.ProductId,
                capabilities.InputReportByteLength,
                capabilities.OutputReportByteLength);
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try
        {
            _monitorTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }
        _cancellation.Dispose();
        _refreshSignal.Dispose();
    }

    private sealed record SteelSeriesDongle(string Path, ushort ProductId, int InputReportLength, int OutputReportLength);

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
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
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
    private static extern bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, ref HidpCaps capabilities);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr detailData, uint detailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}

public sealed record SteelSeriesHeadsetStatus(string Name, bool IsConnected, int? BatteryLevel, bool IsCharging)
{
    public static SteelSeriesHeadsetStatus Disconnected { get; } = new("Arctis 7+", false, null, false);
}
