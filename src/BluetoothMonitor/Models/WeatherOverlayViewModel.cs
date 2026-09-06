using System.ComponentModel;
using System.Runtime.CompilerServices;
using BluetoothMonitor.Services;

namespace BluetoothMonitor.Models;

public sealed class WeatherOverlayViewModel(IWeatherService weatherService, TimeProvider? timeProvider = null) : INotifyPropertyChanged
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private string _configuredLocation = AppSettings.DefaultWeatherLocation;
    private TimeSpan _refreshInterval = TimeSpan.FromMinutes(AppSettings.DefaultWeatherRefreshMinutes);
    private bool _isEnabled = true;
    private bool _isLoading;
    private string _location = AppSettings.DefaultWeatherLocation;
    private string _temperature = "--°C";
    private string _condition = "Waiting for weather";
    private string _icon = "";
    private string _errorMessage = "";
    private DateTimeOffset? _lastSuccessfulRefresh;

    public bool IsEnabled { get => _isEnabled; private set { if (Set(ref _isEnabled, value)) OnPropertyChanged(nameof(IsVisible)); } }
    public bool IsVisible => IsEnabled;
    public bool IsLoading { get => _isLoading; private set { if (Set(ref _isLoading, value)) OnPropertyChanged(nameof(ShowContent)); } }
    public bool ShowContent => !IsLoading;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string Location { get => _location; private set => Set(ref _location, value); }
    public string Temperature { get => _temperature; private set => Set(ref _temperature, value); }
    public string Condition { get => _condition; private set => Set(ref _condition, value); }
    public string Icon { get => _icon; private set { if (Set(ref _icon, value)) OnPropertyChanged(nameof(HasIcon)); } }
    public bool HasIcon => !string.IsNullOrWhiteSpace(Icon);
    public string ErrorMessage { get => _errorMessage; private set { if (Set(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }

    public void Configure(bool enabled, string location, int refreshMinutes)
    {
        IsEnabled = enabled;
        _configuredLocation = string.IsNullOrWhiteSpace(location) ? AppSettings.DefaultWeatherLocation : location.Trim();
        _refreshInterval = TimeSpan.FromMinutes(Math.Clamp(refreshMinutes, AppSettings.MinimumWeatherRefreshMinutes, AppSettings.MaximumWeatherRefreshMinutes));
        if (_lastSuccessfulRefresh is null)
            Location = _configuredLocation;
    }

    public Task RefreshIfStaleAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(force: false, cancellationToken);

    public void ApplySnapshot(WeatherSnapshot snapshot)
    {
        Location = snapshot.Location;
        Temperature = $"{Math.Round(snapshot.TemperatureCelsius):0}°C";
        Condition = snapshot.Condition;
        Icon = snapshot.Icon;
        _lastSuccessfulRefresh = _timeProvider.GetLocalNow();
        ErrorMessage = "";
        IsLoading = false;
    }

    public async Task RefreshAsync(bool force, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || (!force && !IsStale())) return;
        if (!await _refreshLock.WaitAsync(0, cancellationToken)) return;

        try
        {
            if (!force && !IsStale()) return;
            IsLoading = _lastSuccessfulRefresh is null;
            ErrorMessage = "";
            var snapshot = await weatherService.GetCurrentAsync(_configuredLocation, cancellationToken);
            ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = exception is WeatherServiceException ? exception.Message : "Weather refresh failed.";
            if (_lastSuccessfulRefresh is null)
            {
                Temperature = "--°C";
                Condition = "Unavailable";
                Icon = "";
            }
        }
        finally
        {
            IsLoading = false;
            _refreshLock.Release();
        }
    }

    private bool IsStale() => _lastSuccessfulRefresh is null
        || _timeProvider.GetLocalNow() - _lastSuccessfulRefresh >= _refreshInterval;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
