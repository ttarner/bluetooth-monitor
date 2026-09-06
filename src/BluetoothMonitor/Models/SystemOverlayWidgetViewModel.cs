using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BluetoothMonitor.Models;

public sealed class SystemOverlayWidgetViewModel : INotifyPropertyChanged
{
    private bool _showClock = true;
    private bool _showCpu = true;
    private bool _showCpuTemperature = true;
    private bool _showGpuTemperature = true;
    private bool _showRam = true;
    private bool _showNetworkIn = true;
    private bool _showNetworkOut = true;
    private string _timeText = "--:--";
    private string _cpuText = "CPU --";
    private string _cpuTemperatureText = "CPU TMP --";
    private string _gpuTemperatureText = "GPU TMP --";
    private string _ramText = "RAM --";
    private string _networkInText = "IN --";
    private string _networkOutText = "OUT --";

    public bool ShowClock
    {
        get => _showClock;
        set
        {
            if (Set(ref _showClock, value))
                OnPropertyChanged(nameof(IsVisible));
        }
    }

    public bool ShowCpu
    {
        get => _showCpu;
        set
        {
            if (Set(ref _showCpu, value))
                OnPropertyChanged(nameof(IsVisible));
        }
    }

    public bool ShowCpuTemperature
    {
        get => _showCpuTemperature;
        set
        {
            if (Set(ref _showCpuTemperature, value))
                OnPropertyChanged(nameof(IsVisible));
        }
    }

    public bool ShowGpuTemperature
    {
        get => _showGpuTemperature;
        set
        {
            if (Set(ref _showGpuTemperature, value))
                OnPropertyChanged(nameof(IsVisible));
        }
    }

    public bool ShowRam
    {
        get => _showRam;
        set
        {
            if (Set(ref _showRam, value))
                OnPropertyChanged(nameof(IsVisible));
        }
    }

    public bool ShowNetworkIn
    {
        get => _showNetworkIn;
        set
        {
            if (Set(ref _showNetworkIn, value))
                OnPropertyChanged(nameof(IsVisible));
        }
    }

    public bool ShowNetworkOut
    {
        get => _showNetworkOut;
        set
        {
            if (Set(ref _showNetworkOut, value))
                OnPropertyChanged(nameof(IsVisible));
        }
    }

    public string TimeText
    {
        get => _timeText;
        set => Set(ref _timeText, value);
    }

    public string CpuText
    {
        get => _cpuText;
        set => Set(ref _cpuText, value);
    }

    public string CpuTemperatureText
    {
        get => _cpuTemperatureText;
        set => Set(ref _cpuTemperatureText, value);
    }

    public string GpuTemperatureText
    {
        get => _gpuTemperatureText;
        set => Set(ref _gpuTemperatureText, value);
    }

    public string RamText
    {
        get => _ramText;
        set => Set(ref _ramText, value);
    }

    public string NetworkInText
    {
        get => _networkInText;
        set => Set(ref _networkInText, value);
    }

    public string NetworkOutText
    {
        get => _networkOutText;
        set => Set(ref _networkOutText, value);
    }

    public bool IsVisible => ShowClock || ShowCpu || ShowCpuTemperature || ShowGpuTemperature || ShowRam || ShowNetworkIn || ShowNetworkOut;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
