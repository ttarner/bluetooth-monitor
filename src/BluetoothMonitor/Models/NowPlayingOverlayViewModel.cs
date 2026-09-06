using System.ComponentModel;
using System.Runtime.CompilerServices;
using BluetoothMonitor.Services;

namespace BluetoothMonitor.Models;

public sealed class NowPlayingOverlayViewModel : INotifyPropertyChanged
{
    private bool _isEnabled = true;
    private bool _showAnimeInfo = true;
    private string _title = "";
    private string _artist = "";
    private string _romajiTitle = "";
    private string _appName = "";
    private string _mediaTag = "";

    public bool IsEnabled { get => _isEnabled; set { if (Set(ref _isEnabled, value)) OnPropertyChanged(nameof(IsVisible)); } }
    public bool IsVisible => IsEnabled && HasTrack;
    public bool ShowAnimeInfo
    {
        get => _showAnimeInfo;
        set
        {
            if (Set(ref _showAnimeInfo, value))
                OnPropertyChanged(nameof(HasMediaTag));
        }
    }
    public bool HasTrack => !string.IsNullOrWhiteSpace(Title);
    public string Title { get => _title; private set { if (Set(ref _title, value)) OnPropertyChanged(nameof(HasTrack)); } }
    public string Artist { get => _artist; private set => Set(ref _artist, value); }
    public string RomajiTitle { get => _romajiTitle; private set { if (Set(ref _romajiTitle, value)) OnPropertyChanged(nameof(HasRomajiTitle)); } }
    public bool HasRomajiTitle => !string.IsNullOrWhiteSpace(RomajiTitle) && !string.Equals(RomajiTitle, Title, StringComparison.OrdinalIgnoreCase);
    public string AppName { get => _appName; private set => Set(ref _appName, value); }
    public string MediaTag
    {
        get => _mediaTag;
        private set
        {
            if (Set(ref _mediaTag, value))
                OnPropertyChanged(nameof(HasMediaTag));
        }
    }
    public bool HasMediaTag => ShowAnimeInfo && !string.IsNullOrWhiteSpace(MediaTag);

    public void Apply(NowPlayingSnapshot? snapshot)
    {
        if (snapshot is null)
            return;

        Title = snapshot.Title;
        Artist = snapshot.Artist;
        RomajiTitle = snapshot.RomajiTitle;
        AppName = snapshot.AppName;
        MediaTag = snapshot.MediaTag;
        OnPropertyChanged(nameof(IsVisible));
    }

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