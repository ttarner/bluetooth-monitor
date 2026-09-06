using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace BluetoothMonitor.Services;

public sealed record WeatherSnapshot(
    string Location,
    double TemperatureCelsius,
    string Condition,
    string Icon,
    DateTimeOffset ObservedAt);

public interface IWeatherService
{
    Task<WeatherSnapshot> GetCurrentAsync(string location, CancellationToken cancellationToken = default);
}

public sealed class WeatherService(HttpClient httpClient) : IWeatherService
{
    public async Task<WeatherSnapshot> GetCurrentAsync(string location, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(location))
            throw new WeatherServiceException("Enter a weather location in Settings.");

        try
        {
            var geocodeUrl = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name="
                + Uri.EscapeDataString(location.Trim());
            using var geocodeResponse = await httpClient.GetAsync(geocodeUrl, cancellationToken).ConfigureAwait(false);
            geocodeResponse.EnsureSuccessStatusCode();
            await using var geocodeStream = await geocodeResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var geocodeJson = await JsonDocument.ParseAsync(geocodeStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!geocodeJson.RootElement.TryGetProperty("results", out var results)
                || results.GetArrayLength() == 0)
                throw new WeatherServiceException($"Location ‘{location.Trim()}’ was not found.");

            var place = results[0];
            var latitude = place.GetProperty("latitude").GetDouble();
            var longitude = place.GetProperty("longitude").GetDouble();
            var resolvedLocation = FormatLocation(place, location.Trim());

            var weatherUrl = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,weather_code&temperature_unit=celsius&timezone=auto");
            using var weatherResponse = await httpClient.GetAsync(weatherUrl, cancellationToken).ConfigureAwait(false);
            weatherResponse.EnsureSuccessStatusCode();
            await using var weatherStream = await weatherResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var weatherJson = await JsonDocument.ParseAsync(weatherStream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var current = weatherJson.RootElement.GetProperty("current");
            var temperature = current.GetProperty("temperature_2m").GetDouble();
            var weatherCode = current.GetProperty("weather_code").GetInt32();
            var observedAt = current.TryGetProperty("time", out var time)
                && DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedTime)
                    ? parsedTime
                    : DateTimeOffset.Now;
            var (condition, icon) = MapWeatherCode(weatherCode);

            return new WeatherSnapshot(resolvedLocation, temperature, condition, icon, observedAt);
        }
        catch (WeatherServiceException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WeatherServiceException("The weather request timed out.");
        }
        catch (HttpRequestException)
        {
            throw new WeatherServiceException("Weather is temporarily unavailable.");
        }
        catch (JsonException)
        {
            throw new WeatherServiceException("The weather service returned an invalid response.");
        }
        catch (KeyNotFoundException)
        {
            throw new WeatherServiceException("The weather service returned an incomplete response.");
        }
    }

    public static (string Condition, string Icon) MapWeatherCode(int code) => code switch
    {
        0 => ("Clear", "☀️"),
        1 or 2 => ("Partly cloudy", "⛅"),
        3 => ("Overcast", "☁️"),
        45 or 48 => ("Fog", "🌫️"),
        51 or 53 or 55 or 56 or 57 => ("Drizzle", "🌦️"),
        61 or 63 or 65 or 66 or 67 => ("Rain", "🌧️"),
        71 or 73 or 75 or 77 => ("Snow", "🌨️"),
        80 or 81 or 82 => ("Rain showers", "🌦️"),
        85 or 86 => ("Snow showers", "🌨️"),
        95 or 96 or 99 => ("Thunderstorm", "⛈️"),
        _ => ("Unknown", "")
    };

    private static string FormatLocation(JsonElement place, string fallback)
    {
        var name = place.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var country = place.TryGetProperty("country", out var countryElement) ? countryElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) return fallback;
        return string.IsNullOrWhiteSpace(country) ? name : $"{name}, {country}";
    }
}

public sealed class WeatherServiceException(string message) : Exception(message);
