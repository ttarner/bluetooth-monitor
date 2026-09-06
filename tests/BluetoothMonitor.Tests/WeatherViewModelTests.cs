using BluetoothMonitor.Models;
using BluetoothMonitor.Services;

namespace BluetoothMonitor.Tests;

[TestClass]
public sealed class WeatherViewModelTests
{
    [TestMethod]
    public void AppSettings_Normalize_ClampsAndDefaultsWeatherConfiguration()
    {
        var settings = new AppSettings
        {
            WeatherLocation = "  ",
            WeatherRefreshMinutes = 1,
            TemperatureRefreshSeconds = 99
        };

        settings.Normalize();

        Assert.AreEqual(AppSettings.DefaultWeatherLocation, settings.WeatherLocation);
        Assert.AreEqual(AppSettings.MinimumWeatherRefreshMinutes, settings.WeatherRefreshMinutes);
        Assert.AreEqual(AppSettings.MaximumTemperatureRefreshSeconds, settings.TemperatureRefreshSeconds);
    }

    [TestMethod]
    public void AppSettings_Normalize_FixesOverlayWidgetOrder()
    {
        var settings = new AppSettings
        {
            OverlayWidgetOrder = ["devices", "weather", "devices", "unknown"]
        };

        settings.Normalize();

        CollectionAssert.AreEqual(
            new[] { "Devices", "Weather", "System", "NowPlaying" },
            settings.OverlayWidgetOrder);
    }

    [TestMethod]
    public void AppSettings_Defaults_SystemWidgetToOptIn()
    {
        var settings = new AppSettings();

        Assert.IsFalse(settings.ShowClockInOverlay);
        Assert.IsFalse(settings.ShowCpuInOverlay);
        Assert.IsFalse(settings.ShowCpuTemperatureInOverlay);
        Assert.IsFalse(settings.ShowGpuTemperatureInOverlay);
        Assert.IsFalse(settings.ShowRamInOverlay);
        Assert.IsFalse(settings.ShowNetworkInOverlay);
        Assert.IsFalse(settings.ShowNetworkOutOverlay);
        Assert.AreEqual(TemperatureDataSourceMode.Auto, settings.TemperatureDataSource);
        Assert.AreEqual(AppSettings.DefaultTemperatureRefreshSeconds, settings.TemperatureRefreshSeconds);
    }

    [TestMethod]
    public async Task RefreshIfStaleAsync_RefreshesAtStartupAndOnlyAfterInterval()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 28, 10, 0, 0, TimeSpan.Zero));
        var service = new FakeWeatherService();
        var viewModel = new WeatherOverlayViewModel(service, clock);
        viewModel.Configure(true, "Rome", 15);

        await viewModel.RefreshIfStaleAsync();
        await viewModel.RefreshIfStaleAsync();
        Assert.AreEqual(1, service.CallCount);

        clock.Advance(TimeSpan.FromMinutes(15));
        await viewModel.RefreshIfStaleAsync();
        Assert.AreEqual(2, service.CallCount);
        Assert.AreEqual("Rome, Italy", viewModel.Location);
        Assert.AreEqual("28°C", viewModel.Temperature);
        Assert.IsFalse(viewModel.IsLoading);
        Assert.IsFalse(viewModel.HasError);
    }

    [TestMethod]
    public async Task RefreshAsync_ExposesErrorState()
    {
        var service = new FakeWeatherService { Error = new WeatherServiceException("Weather is offline.") };
        var viewModel = new WeatherOverlayViewModel(service);
        viewModel.Configure(true, "Rome", 15);

        await viewModel.RefreshAsync(force: true);

        Assert.IsTrue(viewModel.HasError);
        Assert.AreEqual("Weather is offline.", viewModel.ErrorMessage);
        Assert.AreEqual("Unavailable", viewModel.Condition);
        Assert.IsFalse(viewModel.IsLoading);
    }

    [TestMethod]
    public async Task RefreshAsync_DeduplicatesConcurrentRequests()
    {
        var service = new FakeWeatherService { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var viewModel = new WeatherOverlayViewModel(service);
        viewModel.Configure(true, "Rome", 15);

        var first = viewModel.RefreshAsync(force: true);
        var second = viewModel.RefreshAsync(force: true);
        service.Gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.AreEqual(1, service.CallCount);
    }

    private sealed class FakeWeatherService : IWeatherService
    {
        public int CallCount { get; private set; }
        public Exception? Error { get; init; }
        public TaskCompletionSource? Gate { get; init; }

        public async Task<WeatherSnapshot> GetCurrentAsync(string location, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Gate is not null) await Gate.Task.WaitAsync(cancellationToken);
            if (Error is not null) throw Error;
            return new WeatherSnapshot("Rome, Italy", 27.6, "Partly cloudy", "⛅", DateTimeOffset.Now);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
