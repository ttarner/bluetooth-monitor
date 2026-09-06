using System.Net;
using System.Text;
using BluetoothMonitor.Services;

namespace BluetoothMonitor.Tests;

[TestClass]
public sealed class WeatherServiceTests
{
    [TestMethod]
    public async Task GetCurrentAsync_MapsGeocodeAndWeatherResponses()
    {
        using var client = new HttpClient(new QueueHttpHandler(
            Json("""{"results":[{"name":"Rome","country":"Italy","latitude":41.89,"longitude":12.51}]}"""),
            Json("""{"current":{"time":"2026-06-28T12:00","temperature_2m":27.6,"weather_code":2}}""")));

        var result = await new WeatherService(client).GetCurrentAsync("Rome, Italy");

        Assert.AreEqual("Rome, Italy", result.Location);
        Assert.AreEqual(27.6, result.TemperatureCelsius);
        Assert.AreEqual("Partly cloudy", result.Condition);
        Assert.AreEqual("⛅", result.Icon);
    }

    [TestMethod]
    public async Task GetCurrentAsync_ReportsUnknownLocation()
    {
        using var client = new HttpClient(new QueueHttpHandler(Json("""{"results":[]}""")));

        var exception = await Assert.ThrowsExceptionAsync<WeatherServiceException>(
            () => new WeatherService(client).GetCurrentAsync("Atlantis"));

        StringAssert.Contains(exception.Message, "not found");
    }

    [TestMethod]
    public async Task GetCurrentAsync_RejectsBlankLocationWithoutCallingProvider()
    {
        using var client = new HttpClient(new QueueHttpHandler());

        var exception = await Assert.ThrowsExceptionAsync<WeatherServiceException>(
            () => new WeatherService(client).GetCurrentAsync("  "));

        Assert.AreEqual("Enter a weather location in Settings.", exception.Message);
    }

    [TestMethod]
    public async Task GetCurrentAsync_MapsHttpFailureToFriendlyError()
    {
        using var client = new HttpClient(new QueueHttpHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var exception = await Assert.ThrowsExceptionAsync<WeatherServiceException>(
            () => new WeatherService(client).GetCurrentAsync("Rome"));

        Assert.AreEqual("Weather is temporarily unavailable.", exception.Message);
    }

    [TestMethod]
    public void MapWeatherCode_HandlesKnownAndUnknownCodes()
    {
        Assert.AreEqual(("Thunderstorm", "⛈️"), WeatherService.MapWeatherCode(95));
        Assert.AreEqual(("Unknown", ""), WeatherService.MapWeatherCode(500));
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class QueueHttpHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responses.Dequeue());
    }
}
