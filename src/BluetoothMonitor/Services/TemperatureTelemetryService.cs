using BluetoothMonitor.Models;

namespace BluetoothMonitor.Services;

public sealed record TemperatureTelemetrySnapshot(
    float? CpuTemperatureCelsius,
    float? GpuTemperatureCelsius,
    string SourceName,
    DateTimeOffset ObservedAt);

public sealed record TemperatureTelemetryProbeResult(
    bool Success,
    TemperatureTelemetrySnapshot? Snapshot,
    string? ErrorMessage);

public sealed record TemperatureTelemetryOptions(
    TemperatureDataSourceMode SourceMode,
    TimeSpan RefreshInterval)
{
    public static TemperatureTelemetryOptions Default =>
        new(TemperatureDataSourceMode.Auto, TimeSpan.FromSeconds(AppSettings.DefaultTemperatureRefreshSeconds));
}

public interface ITemperatureTelemetryService
{
    Task<TemperatureTelemetrySnapshot> GetCurrentAsync(TemperatureTelemetryOptions options, CancellationToken cancellationToken = default);
}

public interface ITemperatureTelemetrySource
{
    string SourceName { get; }
    Task<TemperatureTelemetryProbeResult> ProbeAsync(TemperatureTelemetryOptions options, CancellationToken cancellationToken = default);
}

public sealed class TemperatureTelemetryService(
    ITemperatureTelemetrySource primarySource,
    ITemperatureTelemetrySource thermalZoneSource) : ITemperatureTelemetryService
{
    public async Task<TemperatureTelemetrySnapshot> GetCurrentAsync(TemperatureTelemetryOptions options, CancellationToken cancellationToken = default)
    {
        var result = options.SourceMode switch
        {
            TemperatureDataSourceMode.LibreHardwareMonitor => await primarySource.ProbeAsync(options, cancellationToken),
            TemperatureDataSourceMode.WindowsThermalZones => await thermalZoneSource.ProbeAsync(options, cancellationToken),
            _ => await GetAutoAsync(options, cancellationToken)
        };

        if (result.Success && result.Snapshot is not null)
            return result.Snapshot;

        throw new TemperatureTelemetryException(result.ErrorMessage ?? "Temperature refresh failed.");
    }

    private async Task<TemperatureTelemetryProbeResult> GetAutoAsync(TemperatureTelemetryOptions options, CancellationToken cancellationToken)
    {
        var primaryResult = await primarySource.ProbeAsync(options, cancellationToken);
        var primarySnapshot = primaryResult.Snapshot;

        if (primarySnapshot is not null
            && primarySnapshot.CpuTemperatureCelsius is not null
            && primarySnapshot.GpuTemperatureCelsius is not null)
        {
            return new TemperatureTelemetryProbeResult(true, primarySnapshot, null);
        }

        var thermalZoneResult = await thermalZoneSource.ProbeAsync(options, cancellationToken);
        var thermalZoneSnapshot = thermalZoneResult.Snapshot;
        if (thermalZoneSnapshot is not null)
        {
            if (primarySnapshot is null)
                return new TemperatureTelemetryProbeResult(true, thermalZoneSnapshot, null);

            return new TemperatureTelemetryProbeResult(true, primarySnapshot with
            {
                CpuTemperatureCelsius = primarySnapshot.CpuTemperatureCelsius ?? thermalZoneSnapshot.CpuTemperatureCelsius,
                SourceName = primarySnapshot.CpuTemperatureCelsius is null || primarySnapshot.GpuTemperatureCelsius is null
                    ? $"{primarySnapshot.SourceName} + {thermalZoneSnapshot.SourceName}"
                    : primarySnapshot.SourceName,
                ObservedAt = thermalZoneSnapshot.ObservedAt > primarySnapshot.ObservedAt ? thermalZoneSnapshot.ObservedAt : primarySnapshot.ObservedAt
            }, null);
        }

        if (primarySnapshot is not null)
            return new TemperatureTelemetryProbeResult(true, primarySnapshot, null);

        return new TemperatureTelemetryProbeResult(
            false,
            null,
            JoinErrors(primaryResult.ErrorMessage, thermalZoneResult.ErrorMessage));
    }

    private static string JoinErrors(string? first, string? second) =>
        string.Join(" ", new[] { first, second }.Where(message => !string.IsNullOrWhiteSpace(message)));
}

public sealed class TemperatureTelemetryException(string message) : Exception(message);
