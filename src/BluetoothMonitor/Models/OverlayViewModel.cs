using System.Collections.ObjectModel;

namespace BluetoothMonitor.Models;

public sealed class OverlayViewModel
{
    public required ObservableCollection<BluetoothDeviceViewModel> Devices { get; init; }
    public required SystemOverlayWidgetViewModel SystemWidget { get; init; }
    public required NowPlayingOverlayViewModel NowPlaying { get; init; }
    public required WeatherOverlayViewModel Weather { get; init; }
}
