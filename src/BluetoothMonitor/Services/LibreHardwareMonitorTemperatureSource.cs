using LibreHardwareMonitor.Hardware;

namespace BluetoothMonitor.Services;

public sealed class LibreHardwareMonitorTemperatureSource : ITemperatureTelemetrySource, IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMotherboardEnabled = true,
        IsControllerEnabled = true
    };

    private bool _hardwareMonitorOpen;

    public string SourceName => "LibreHardwareMonitor";

    public Task<TemperatureTelemetryProbeResult> ProbeAsync(TemperatureTelemetryOptions options, CancellationToken cancellationToken = default)
    {
        if (!TryOpenHardwareMonitor())
        {
            return Task.FromResult(new TemperatureTelemetryProbeResult(
                false,
                null,
                "LibreHardwareMonitor could not open the local sensor stack."));
        }

        float? cpuTemperature = null;
        float? gpuTemperature = null;
        foreach (var hardware in _computer.Hardware)
        {
            UpdateHardwareTree(hardware);
            foreach (var sensorReading in EnumerateTemperatureReadings(hardware))
            {
                if (IsCpuTemperatureSensor(sensorReading.Hardware, sensorReading.Sensor))
                    cpuTemperature = MaxTemperature(cpuTemperature, sensorReading.Sensor.Value);

                if (IsGpuCoreTemperatureSensor(sensorReading.Hardware, sensorReading.Sensor))
                    gpuTemperature = MaxTemperature(gpuTemperature, sensorReading.Sensor.Value);
            }
        }

        return Task.FromResult(new TemperatureTelemetryProbeResult(
            true,
            new TemperatureTelemetrySnapshot(
                cpuTemperature,
                gpuTemperature,
                SourceName,
                DateTimeOffset.Now),
            null));
    }

    public void Dispose()
    {
        _computer.Close();
        GC.SuppressFinalize(this);
    }

    private bool TryOpenHardwareMonitor()
    {
        if (_hardwareMonitorOpen)
            return true;

        try
        {
            _computer.Open();
            _hardwareMonitorOpen = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void UpdateHardwareTree(IHardware hardware)
    {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware)
            UpdateHardwareTree(subHardware);
    }

    private static IEnumerable<(IHardware Hardware, ISensor Sensor)> EnumerateTemperatureReadings(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Temperature && sensor.Value is float)
                yield return (hardware, sensor);
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            foreach (var reading in EnumerateTemperatureReadings(subHardware))
                yield return reading;
        }
    }

    private static bool IsCpuTemperatureSensor(IHardware hardware, ISensor sensor)
    {
        if (hardware.HardwareType == HardwareType.Cpu)
            return !sensor.Name.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase);

        return hardware.HardwareType == HardwareType.Motherboard
            && sensor.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGpuCoreTemperatureSensor(IHardware hardware, ISensor sensor) =>
        hardware.HardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia
        && sensor.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase);

    private static float? MaxTemperature(float? left, float? right)
    {
        if (left is null)
            return right;

        if (right is null)
            return left;

        return Math.Max(left.Value, right.Value);
    }
}
