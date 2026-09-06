using System.Diagnostics;

namespace BluetoothMonitor.Services;

public sealed class WindowsThermalZoneTemperatureSource : ITemperatureTelemetrySource
{
    private const string CategoryName = "Thermal Zone Information";
    private const string CounterName = "Temperature";

    public string SourceName => "Windows thermal zones";

    public Task<TemperatureTelemetryProbeResult> ProbeAsync(TemperatureTelemetryOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            var category = new PerformanceCounterCategory(CategoryName);
            var instances = category.GetInstanceNames();
            if (instances.Length == 0)
            {
                return Task.FromResult(new TemperatureTelemetryProbeResult(
                    false,
                    null,
                    "Windows thermal zones are not available on this system."));
            }

            float? maxTemperature = null;
            foreach (var instance in instances)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var counter = new PerformanceCounter(CategoryName, CounterName, instance, readOnly: true);
                var rawValue = counter.NextValue();
                var celsius = ConvertTenthsKelvinToCelsius(rawValue);
                maxTemperature = MaxTemperature(maxTemperature, celsius);
            }

            if (maxTemperature is null)
            {
                return Task.FromResult(new TemperatureTelemetryProbeResult(
                    false,
                    null,
                    "Windows thermal zones did not return a temperature."));
            }

            return Task.FromResult(new TemperatureTelemetryProbeResult(
                true,
                new TemperatureTelemetrySnapshot(
                    maxTemperature,
                    null,
                    SourceName,
                    DateTimeOffset.Now),
                null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Task.FromResult(new TemperatureTelemetryProbeResult(
                false,
                null,
                $"Windows thermal zones are unavailable: {exception.Message}"));
        }
    }

    private static float? ConvertTenthsKelvinToCelsius(float rawValue)
    {
        if (rawValue <= 0)
            return null;

        var celsius = (rawValue / 10f) - 273.15f;
        return celsius is < -50f or > 150f ? null : celsius;
    }

    private static float? MaxTemperature(float? left, float? right)
    {
        if (left is null)
            return right;

        if (right is null)
            return left;

        return Math.Max(left.Value, right.Value);
    }
}
