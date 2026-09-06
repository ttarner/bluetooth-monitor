using BluetoothMonitor.Models;
using BluetoothMonitor.Services;

namespace BluetoothMonitor.Tests;

[TestClass]
public sealed class TemperatureTelemetryTests
{
    [TestMethod]
    public async Task TemperatureTelemetryService_Auto_FillsMissingPrimaryCpuFromNativeFallback()
    {
        var service = new TemperatureTelemetryService(
            new FakeTemperatureSource(new TemperatureTelemetrySnapshot(null, 71f, "Primary", DateTimeOffset.Now)),
            new FakeTemperatureSource(new TemperatureTelemetrySnapshot(56f, null, "Thermal zones", DateTimeOffset.Now.AddSeconds(1))));

        var snapshot = await service.GetCurrentAsync(new TemperatureTelemetryOptions(
            TemperatureDataSourceMode.Auto,
            TimeSpan.FromSeconds(5)));

        Assert.AreEqual(56f, snapshot.CpuTemperatureCelsius);
        Assert.AreEqual(71f, snapshot.GpuTemperatureCelsius);
        Assert.AreEqual("Primary + Thermal zones", snapshot.SourceName);
    }

    [TestMethod]
    public async Task TemperatureTelemetryService_Auto_ThrowsWhenNoSourceSucceeds()
    {
        var service = new TemperatureTelemetryService(
            new FakeTemperatureSource("Primary failed."),
            new FakeTemperatureSource("Thermal zones failed."));

        await Assert.ThrowsExceptionAsync<TemperatureTelemetryException>(() =>
            service.GetCurrentAsync(new TemperatureTelemetryOptions(
                TemperatureDataSourceMode.Auto,
                TimeSpan.FromSeconds(5))));
    }

    [TestMethod]
    public async Task TemperatureOverlayViewModel_RefreshIfStale_RefreshesAtStartupAndOnlyAfterInterval()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 28, 10, 0, 0, TimeSpan.Zero));
        var service = new FakeTelemetryService();
        var viewModel = new TemperatureOverlayViewModel(service, clock);
        viewModel.Configure(true, new TemperatureTelemetryOptions(
            TemperatureDataSourceMode.Auto,
            TimeSpan.FromSeconds(5)));

        await viewModel.RefreshIfStaleAsync();
        await viewModel.RefreshIfStaleAsync();
        Assert.AreEqual(1, service.CallCount);
        Assert.AreEqual("55°C", viewModel.CpuText);
        Assert.AreEqual("70°C", viewModel.GpuText);

        clock.Advance(TimeSpan.FromSeconds(5));
        await viewModel.RefreshIfStaleAsync();
        Assert.AreEqual(2, service.CallCount);
    }

    [TestMethod]
    public async Task TemperatureOverlayViewModel_RefreshAsync_ExposesErrorState()
    {
        var service = new FakeTelemetryService
        {
            Error = new TemperatureTelemetryException("Windows thermal zones are unavailable.")
        };
        var viewModel = new TemperatureOverlayViewModel(service);
        viewModel.Configure(true, new TemperatureTelemetryOptions(
            TemperatureDataSourceMode.WindowsThermalZones,
            TimeSpan.FromSeconds(5)));

        await viewModel.RefreshAsync(force: true);

        Assert.IsTrue(viewModel.HasError);
        Assert.AreEqual("Windows thermal zones are unavailable.", viewModel.ErrorMessage);
        Assert.AreEqual("--", viewModel.CpuText);
        Assert.AreEqual("--", viewModel.GpuText);
    }

    private sealed class FakeTemperatureSource : ITemperatureTelemetrySource
    {
        private readonly TemperatureTelemetrySnapshot? _snapshot;
        private readonly string? _error;

        public FakeTemperatureSource(TemperatureTelemetrySnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public FakeTemperatureSource(string error)
        {
            _error = error;
        }

        public string SourceName => "Fake";

        public Task<TemperatureTelemetryProbeResult> ProbeAsync(TemperatureTelemetryOptions options, CancellationToken cancellationToken = default)
        {
            if (_error is not null)
            {
                return Task.FromResult(new TemperatureTelemetryProbeResult(
                    false,
                    null,
                    _error));
            }

            return Task.FromResult(new TemperatureTelemetryProbeResult(
                true,
                _snapshot!,
                null));
        }
    }

    private sealed class FakeTelemetryService : ITemperatureTelemetryService
    {
        public int CallCount { get; private set; }
        public Exception? Error { get; init; }

        public Task<TemperatureTelemetrySnapshot> GetCurrentAsync(TemperatureTelemetryOptions options, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Error is not null)
                throw Error;

            return Task.FromResult(new TemperatureTelemetrySnapshot(55f, 70f, "Fake", DateTimeOffset.Now));
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now += amount;
    }
}
