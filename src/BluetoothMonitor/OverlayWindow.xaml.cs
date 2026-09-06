using System.Collections.ObjectModel;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;
using BluetoothMonitor.Models;
using BluetoothMonitor.Services;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BluetoothMonitor;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WmDpichanged = 0x02E0;
    private const int WmDisplaychange = 0x007E;
    private const int WmSettingchange = 0x001A;
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private const uint DwmWindowCornerPreferenceDoNotRound = 1;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const double ScreenMargin = 24;
    private const double MinimumOverlayHeight = 96d;

    private OverlayPosition _position;
    private readonly IntPtr _referenceWindowHandle;
    private readonly OverlayViewModel _viewModel;
    private readonly SystemStatsService _systemStatsService = new();
    private readonly INowPlayingService _nowPlayingService = new WindowsNowPlayingService();
    private readonly TemperatureOverlayViewModel _temperatureViewModel;
    private readonly DispatcherTimer _systemWidgetTimer;
    private readonly DispatcherTimer _nowPlayingTimer;
    private readonly DispatcherTimer _weatherTimer;
    private readonly CancellationTokenSource _weatherCancellation = new();
    private readonly CancellationTokenSource _temperatureCancellation = new();
    private readonly CancellationTokenSource _nowPlayingCancellation = new();
    private bool _hasPositionedOnce;

    public OverlayWindow(
        ObservableCollection<BluetoothDeviceViewModel> devices,
        OverlayPosition position,
        AppSettings settings,
        IntPtr referenceWindowHandle,
        IWeatherService weatherService,
        ITemperatureTelemetryService temperatureTelemetryService)
    {
        InitializeComponent();
        _position = position;
        _referenceWindowHandle = referenceWindowHandle;
        _temperatureViewModel = new TemperatureOverlayViewModel(temperatureTelemetryService);
        _viewModel = new OverlayViewModel
        {
            Devices = devices,
            SystemWidget = new SystemOverlayWidgetViewModel
            {
                ShowClock = settings.ShowClockInOverlay,
                ShowCpu = settings.ShowCpuInOverlay,
                ShowCpuTemperature = settings.ShowCpuTemperatureInOverlay,
                ShowGpuTemperature = settings.ShowGpuTemperatureInOverlay,
                ShowRam = settings.ShowRamInOverlay,
                ShowNetworkIn = settings.ShowNetworkInOverlay,
                ShowNetworkOut = settings.ShowNetworkOutOverlay
            },
            NowPlaying = new NowPlayingOverlayViewModel
            {
                IsEnabled = settings.ShowNowPlayingInOverlay,
                ShowAnimeInfo = settings.ShowAnimeInfoInOverlay
            },
            Weather = new WeatherOverlayViewModel(weatherService)
        };
        _temperatureViewModel.Configure(
            settings.ShowCpuTemperatureInOverlay || settings.ShowGpuTemperatureInOverlay,
            CreateTemperatureOptions(settings));
        _viewModel.Weather.Configure(settings.ShowWeatherInOverlay, settings.WeatherLocation, settings.WeatherRefreshMinutes);
        DataContext = _viewModel;
        ApplyWidgetOrderSettings(settings);
        ApplyAppearanceSettings(settings);
        _systemWidgetTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _systemWidgetTimer.Tick += async (_, _) => await UpdateSystemWidgetAsync();
        _nowPlayingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _nowPlayingTimer.Tick += async (_, _) => await UpdateNowPlayingAsync();
        _weatherTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _weatherTimer.Tick += async (_, _) => await _viewModel.Weather.RefreshIfStaleAsync(_weatherCancellation.Token);
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => PositionOverlay();
        SizeChanged += (_, _) => PositionOverlay();
        Loaded += async (_, _) => await ConfigureSystemWidgetTimerAsync();
        Loaded += async (_, _) => await UpdateNowPlayingAsync();
        Loaded += async (_, _) =>
        {
            ConfigureWeatherTimer();
            await _viewModel.Weather.RefreshIfStaleAsync(_weatherCancellation.Token);
        };
        Closed += (_, _) =>
        {
            _systemWidgetTimer.Stop();
            _weatherTimer.Stop();
            _weatherCancellation.Cancel();
            _weatherCancellation.Dispose();
            _temperatureCancellation.Cancel();
            _temperatureCancellation.Dispose();
            _nowPlayingTimer.Stop();
            _nowPlayingCancellation.Cancel();
            _nowPlayingCancellation.Dispose();
        };
    }

    public void SetPosition(OverlayPosition position)
    {
        _position = position;
        PositionOverlay();
    }

    public void ApplySystemWidgetSettings(AppSettings settings)
    {
        _viewModel.SystemWidget.ShowClock = settings.ShowClockInOverlay;
        _viewModel.SystemWidget.ShowCpu = settings.ShowCpuInOverlay;
        _viewModel.SystemWidget.ShowCpuTemperature = settings.ShowCpuTemperatureInOverlay;
        _viewModel.SystemWidget.ShowGpuTemperature = settings.ShowGpuTemperatureInOverlay;
        _viewModel.SystemWidget.ShowRam = settings.ShowRamInOverlay;
        _viewModel.SystemWidget.ShowNetworkIn = settings.ShowNetworkInOverlay;
        _viewModel.SystemWidget.ShowNetworkOut = settings.ShowNetworkOutOverlay;
        _temperatureViewModel.Configure(
            settings.ShowCpuTemperatureInOverlay || settings.ShowGpuTemperatureInOverlay,
            CreateTemperatureOptions(settings));
        _ = ConfigureSystemWidgetTimerAsync();
        PositionOverlay();
    }

    public void ApplyNowPlayingSettings(AppSettings settings)
    {
        _viewModel.NowPlaying.IsEnabled = settings.ShowNowPlayingInOverlay;
        _viewModel.NowPlaying.ShowAnimeInfo = settings.ShowAnimeInfoInOverlay;
        if (_viewModel.NowPlaying.IsEnabled)
            _ = UpdateNowPlayingAsync();
        else
            _nowPlayingTimer.Stop();
        PositionOverlay();
    }

    public async Task ApplyTemperatureSettingsAsync(AppSettings settings)
    {
        _temperatureViewModel.Configure(
            settings.ShowCpuTemperatureInOverlay || settings.ShowGpuTemperatureInOverlay,
            CreateTemperatureOptions(settings));
        await _temperatureViewModel.RefreshAsync(force: true, _temperatureCancellation.Token);
        SyncTemperatureText();
    }

    public void ApplyWidgetOrderSettings(AppSettings settings)
    {
        if (OverlayWidgetsPanel is null)
            return;

        var widgets = new Dictionary<OverlayWidgetKind, UIElement>
        {
            [OverlayWidgetKind.Weather] = WeatherWidgetBackground,
            [OverlayWidgetKind.System] = SystemWidgetBackground,
            [OverlayWidgetKind.NowPlaying] = NowPlayingWidgetBackground,
            [OverlayWidgetKind.Devices] = DevicesItemsControl
        };

        OverlayWidgetsPanel.Children.Clear();
        foreach (var entry in OverlayWidgetOrderSettings.Normalize(settings.OverlayWidgetOrder))
        {
            if (Enum.TryParse<OverlayWidgetKind>(entry, out var widget)
                && widgets.TryGetValue(widget, out var element))
            {
                OverlayWidgetsPanel.Children.Add(element);
            }
        }

        PositionOverlay();
    }

    public async Task ApplyWeatherSettingsAsync(AppSettings settings, WeatherSnapshot? validatedSnapshot = null)
    {
        _viewModel.Weather.Configure(settings.ShowWeatherInOverlay, settings.WeatherLocation, settings.WeatherRefreshMinutes);
        ConfigureWeatherTimer();
        if (validatedSnapshot is not null)
            _viewModel.Weather.ApplySnapshot(validatedSnapshot);
        else
            await _viewModel.Weather.RefreshAsync(force: true, _weatherCancellation.Token);
        PositionOverlay();
    }

    public void ApplyAppearanceSettings(AppSettings settings)
    {
        SetBackgroundOpacity(settings.OverlayBackgroundOpacity);
        SetTextOpacity(settings.OverlayTextOpacity);
        SetScale(settings.OverlayScale);
    }

    public void SetBackgroundOpacity(double opacity)
    {
        var value = Math.Clamp(opacity, 0d, 1d);
        SetBackgroundBrushOpacity(OverlayRoot, value);
        SetBackgroundBrushOpacity(WeatherWidgetBackground, value);
        SetBackgroundBrushOpacity(SystemWidgetBackground, value);
        SetBackgroundBrushOpacity(NowPlayingWidgetBackground, value);
    }

    public void SetTextOpacity(double opacity)
    {
        var value = Math.Clamp(opacity, 0.15d, 1d);
        if (WeatherWidgetBackground is not null)
            WeatherWidgetBackground.Opacity = value;
        if (SystemWidgetContent is not null)
            SystemWidgetContent.Opacity = value;
        if (NowPlayingWidgetBackground is not null)
            NowPlayingWidgetBackground.Opacity = value;
        if (DevicesItemsControl is not null)
            DevicesItemsControl.Opacity = value;
    }

    public void SetScale(double scale)
    {
        var value = Math.Clamp(scale, 0.5d, 2d);
        if (OverlayScaleTransform is not null)
        {
            OverlayScaleTransform.ScaleX = value;
            OverlayScaleTransform.ScaleY = value;
        }

        Dispatcher.BeginInvoke(PositionOverlay, DispatcherPriority.Loaded);
    }

    private async Task ConfigureSystemWidgetTimerAsync()
    {
        if (_viewModel.SystemWidget.IsVisible)
        {
            await UpdateSystemWidgetAsync();
            if (!_systemWidgetTimer.IsEnabled)
                _systemWidgetTimer.Start();
        }
        else
        {
            _systemWidgetTimer.Stop();
        }
    }

    private async Task UpdateNowPlayingAsync()
    {
        if (!_viewModel.NowPlaying.IsEnabled)
            return;

        try
        {
            _viewModel.NowPlaying.Apply(await _nowPlayingService.GetCurrentAsync(_nowPlayingCancellation.Token));
        }
        catch (OperationCanceledException) when (_nowPlayingCancellation.IsCancellationRequested)
        {
        }
        catch
        {
            _viewModel.NowPlaying.Apply(null);
        }

        if (_viewModel.NowPlaying.IsEnabled && !_nowPlayingTimer.IsEnabled)
            _nowPlayingTimer.Start();
    }

    private void ConfigureWeatherTimer()
    {
        if (_viewModel.Weather.IsEnabled)
        {
            if (!_weatherTimer.IsEnabled)
                _weatherTimer.Start();
        }
        else
        {
            _weatherTimer.Stop();
        }
    }

    private async Task UpdateSystemWidgetAsync()
    {
        var snapshot = await Task.Run(_systemStatsService.Capture, _temperatureCancellation.Token);
        _viewModel.SystemWidget.TimeText = snapshot.TimeText;
        _viewModel.SystemWidget.CpuText = $"CPU {FormatPercent(snapshot.CpuUsagePercent)}";
        _viewModel.SystemWidget.RamText = $"RAM {FormatPercent(snapshot.RamUsagePercent)}";
        _viewModel.SystemWidget.NetworkInText = $"IN {FormatRate(snapshot.DownloadBytesPerSecond)}";
        _viewModel.SystemWidget.NetworkOutText = $"OUT {FormatRate(snapshot.UploadBytesPerSecond)}";
        await _temperatureViewModel.RefreshIfStaleAsync(_temperatureCancellation.Token);
        SyncTemperatureText();
    }

    private void SyncTemperatureText()
    {
        _viewModel.SystemWidget.CpuTemperatureText = $"CPU TMP {_temperatureViewModel.CpuText}";
        _viewModel.SystemWidget.GpuTemperatureText = $"GPU TMP {_temperatureViewModel.GpuText}";
    }

    private static TemperatureTelemetryOptions CreateTemperatureOptions(AppSettings settings) =>
        new(
            settings.TemperatureDataSource,
            TimeSpan.FromSeconds(settings.TemperatureRefreshSeconds));

    private void PositionOverlay()
    {
        if (!IsLoaded) return;

        var placementHandle = GetPlacementReferenceHandle();
        var dpi = GetDpiForWindow(placementHandle);
        var scale = dpi / 96d;
        var workingAreaPixels = Forms.Screen.FromHandle(placementHandle).WorkingArea;
        var marginPixels = ScreenMargin * scale;
        var currentWidthPixels = Math.Max(0d, ActualWidth * scale);
        var currentHeightPixels = Math.Max(MinimumOverlayHeight * scale, ActualHeight * scale);
        var layout = OverlayLayoutHelper.Calculate(
            new DisplayWorkArea(workingAreaPixels.Left, workingAreaPixels.Top, workingAreaPixels.Width, workingAreaPixels.Height),
            currentWidthPixels,
            currentHeightPixels,
            _position,
            marginPixels,
            MinimumOverlayHeight * scale);

        MaxWidth = layout.MaxWidth / scale;
        MaxHeight = layout.MaxHeight / scale;

        var handle = new WindowInteropHelper(this).Handle;
        SetWindowPos(
            handle,
            IntPtr.Zero,
            (int)Math.Round(layout.Left),
            (int)Math.Round(layout.Top),
            0,
            0,
            0x0001 | 0x0004 | 0x0010);

        _hasPositionedOnce = true;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        ApplyCustomCornerPreference(handle);
        HwndSource.FromHwnd(handle)?.AddHook(OverlayWndProc);
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExNoActivate));
    }

    private IntPtr OverlayWndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is WmDpichanged or WmDisplaychange or WmSettingchange)
            Dispatcher.BeginInvoke(PositionOverlay, DispatcherPriority.Loaded);

        return IntPtr.Zero;
    }

    private IntPtr GetPlacementReferenceHandle()
    {
        if (_hasPositionedOnce)
        {
            var overlayHandle = new WindowInteropHelper(this).Handle;
            if (overlayHandle != IntPtr.Zero)
                return overlayHandle;
        }

        return _referenceWindowHandle != IntPtr.Zero
            ? _referenceWindowHandle
            : new WindowInteropHelper(this).Handle;
    }

    private static string FormatPercent(int? percent) => percent is int value ? $"{value}%" : "--";

    private static void ApplyCustomCornerPreference(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;

        var preference = DwmWindowCornerPreferenceDoNotRound;
        _ = DwmSetWindowAttribute(
            handle,
            DwmWindowCornerPreferenceAttribute,
            ref preference,
            sizeof(uint));
    }

    private static void SetBackgroundBrushOpacity(System.Windows.Controls.Border? border, double opacity)
    {
        if (border?.Background is not SolidColorBrush brush)
            return;

        if (!brush.IsFrozen)
        {
            brush.Opacity = opacity;
            return;
        }

        var mutableBrush = brush.Clone();
        mutableBrush.Opacity = opacity;
        border.Background = mutableBrush;
    }

    private static string FormatRate(double? bytesPerSecond)
    {
        if (bytesPerSecond is null) return "--";

        var value = bytesPerSecond.Value;
        var units = new[] { "B/s", "KB/s", "MB/s", "GB/s" };
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return value >= 100 || unitIndex == 0
            ? $"{value:0} {units[unitIndex]}"
            : $"{value:0.0} {units[unitIndex]}";
    }

    private static IntPtr GetWindowLongPtr(IntPtr handle, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : new IntPtr(GetWindowLong32(handle, index));

    private static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(handle, index, value) : new IntPtr(SetWindowLong32(handle, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref uint pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
