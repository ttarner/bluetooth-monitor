using System.ComponentModel;
using System.Runtime.CompilerServices;
using BluetoothMonitor.Services;

namespace BluetoothMonitor.Models;

public sealed class TemperatureOverlayViewModel(
    ITemperatureTelemetryService telemetryService,
    TimeProvider? timeProvider = null) : INotifyPropertyChanged
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private TemperatureTelemetryOptions _options = TemperatureTelemetryOptions.Default;
    private bool _isEnabled;
    private bool _isLoading;
    private string _cpuText = "--";
    private string _gpuText = "--";
    private string _errorMessage = "";
    private DateTimeOffset? _lastSuccessfulRefresh;

    public bool IsEnabled => _isEnabled;
    public bool IsLoading => _isLoading;
    public string CpuText => _cpuText;
    public string GpuText => _gpuText;
    public string ErrorMessage => _errorMessage;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public void Configure(bool enabled, TemperatureTelemetryOptions options)
    {
        Set(ref _isEnabled, enabled, nameof(IsEnabled));
        _options = options;
        if (!enabled)
        {
            Set(ref _cpuText, "--", nameof(CpuText));
            Set(ref _gpuText, "--", nameof(GpuText));
            Set(ref _errorMessage, "", nameof(ErrorMessage), nameof(HasError));
        }
    }

    public Task RefreshIfStaleAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(force: false, cancellationToken);

    public void ApplySnapshot(TemperatureTelemetrySnapshot snapshot)
    {
        Set(ref _cpuText, FormatTemperature(snapshot.CpuTemperatureCelsius), nameof(CpuText));
        Set(ref _gpuText, FormatTemperature(snapshot.GpuTemperatureCelsius), nameof(GpuText));
        _lastSuccessfulRefresh = _timeProvider.GetLocalNow();
        Set(ref _errorMessage, "", nameof(ErrorMessage), nameof(HasError));
        Set(ref _isLoading, false, nameof(IsLoading));
    }

    public async Task RefreshAsync(bool force, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled || (!force && !IsStale()))
            return;

        if (!await _refreshLock.WaitAsync(0, cancellationToken))
            return;

        try
        {
            if (!force && !IsStale())
                return;

            Set(ref _isLoading, _lastSuccessfulRefresh is null, nameof(IsLoading));
            Set(ref _errorMessage, "", nameof(ErrorMessage), nameof(HasError));
            var snapshot = await Task.Run(
                () => telemetryService.GetCurrentAsync(_options, cancellationToken),
                cancellationToken);
            ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Set(ref _errorMessage,
                exception is TemperatureTelemetryException ? exception.Message : "Temperature refresh failed.",
                nameof(ErrorMessage),
                nameof(HasError));
            if (_lastSuccessfulRefresh is null)
            {
                Set(ref _cpuText, "--", nameof(CpuText));
                Set(ref _gpuText, "--", nameof(GpuText));
            }
        }
        finally
        {
            Set(ref _isLoading, false, nameof(IsLoading));
            _refreshLock.Release();
        }
    }

    private bool IsStale() => _lastSuccessfulRefresh is null
        || _timeProvider.GetLocalNow() - _lastSuccessfulRefresh >= _options.RefreshInterval;

    private static string FormatTemperature(float? temperatureCelsius) =>
        temperatureCelsius is float value ? $"{Math.Round(value):0}°C" : "--";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, params string[] propertyNames)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        foreach (var propertyName in propertyNames)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
