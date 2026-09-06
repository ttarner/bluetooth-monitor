using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Media = System.Windows.Media;
using BluetoothMonitor.Models;
using BluetoothMonitor.Services;
using BluetoothMonitor.Views;
using Forms = System.Windows.Forms;

namespace BluetoothMonitor;

public partial class MainWindow : Window
{
    private sealed class OverlayWidgetOrderItem
    {
        public required string Key { get; init; }
        public required string Label { get; init; }
        public int Index { get; set; }
    }

    private enum MainView
    {
        Dashboard,
        Settings,
        About
    }

    private const int HotkeyId = 0xB711;
    private const int WmGetMinMaxInfoMessage = 0x0024;
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private const double WindowCornerRadius = 8d;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint KeyB = 0x42;
    private const double MinimumWindowWidth = 900d;
    private const double MinimumWindowHeight = 680d;

    private readonly BluetoothBatteryService _batteryService;
    private readonly PlayStationBatteryService _playStationBatteryService;
    private readonly SteelSeriesBatteryService _steelSeriesBatteryService;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly HttpClient _weatherHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly IWeatherService _weatherService;
    private readonly ITemperatureTelemetryService _temperatureTelemetryService;
    private readonly LibreHardwareMonitorTemperatureSource _libreHardwareMonitorTemperatureSource = new();
    private readonly ObservableCollection<OverlayWidgetOrderItem> _overlayWidgetOrderItems = [];
    private CancellationTokenSource? _weatherValidationCancellation;
    private string _lastValidatedWeatherLocation = "";
    private OverlayWindow? _overlay;
    private HwndSource? _source;
    private Forms.NotifyIcon? _trayIcon;
    private readonly Dictionary<OverlayPosition, Forms.ToolStripMenuItem> _trayOverlayPositionItems = [];
    private readonly IUpdateService _updateService = new UpdateService();
    private UpdateInfo? _latestUpdateInfo;
    private Forms.ToolStripMenuItem? _trayUpdateMenuItem;
    private readonly System.Windows.Threading.DispatcherTimer _periodicUpdateTimer = new();
    private bool _isUpdating;
    private bool _exitRequested;
    private bool _initializingSettings = true;
    private readonly bool _launchedAtStartup;
    private System.Windows.Point _overlayWidgetOrderDragStartPoint;
    private OverlayWidgetOrderItem? _pendingOverlayWidgetDragItem;

    public MainWindow(bool launchedAtStartup = false)
    {
        _weatherService = new WeatherService(_weatherHttpClient);
        _temperatureTelemetryService = new TemperatureTelemetryService(
            _libreHardwareMonitorTemperatureSource,
            new WindowsThermalZoneTemperatureSource());
        _launchedAtStartup = launchedAtStartup;
        InitializeComponent();
        OverlayWidgetOrderListBox.ItemsSource = _overlayWidgetOrderItems;
        ShowDashboardView();
        UpdateOverlayButtonVisualState();
        PositionCombo.SelectedValue = _settings.OverlayPosition.ToString();
        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        ShowOverlayOnStartupCheckBox.IsChecked = _settings.ShowOverlayOnStartup;
        KeepRunningInTrayOnCloseCheckBox.IsChecked = _settings.KeepRunningInTrayOnClose;
        AutoCheckForUpdatesCheckBox.IsChecked = _settings.AutoCheckForUpdates;
        CurrentVersionTextBlock.Text = $"v{_updateService.CurrentVersion}";
        LowBatteryThresholdSlider.Value = _settings.LowBatteryThreshold;
        LowBatteryThresholdValueText.Text = $"{_settings.LowBatteryThreshold}%";
        OverlayScaleSlider.Value = _settings.OverlayScale * 100d;
        OverlayScaleValueText.Text = $"{(int)Math.Round(OverlayScaleSlider.Value)}%";
        OverlayBackgroundOpacitySlider.Value = _settings.OverlayBackgroundOpacity * 100d;
        OverlayBackgroundOpacityValueText.Text = $"{(int)Math.Round(OverlayBackgroundOpacitySlider.Value)}%";
        OverlayTextOpacitySlider.Value = _settings.OverlayTextOpacity * 100d;
        OverlayTextOpacityValueText.Text = $"{(int)Math.Round(OverlayTextOpacitySlider.Value)}%";
        ShowClockInOverlayCheckBox.IsChecked = _settings.ShowClockInOverlay;
        ShowCpuInOverlayCheckBox.IsChecked = _settings.ShowCpuInOverlay;
        ShowCpuTemperatureInOverlayCheckBox.IsChecked = _settings.ShowCpuTemperatureInOverlay;
        ShowGpuTemperatureInOverlayCheckBox.IsChecked = _settings.ShowGpuTemperatureInOverlay;
        ShowRamInOverlayCheckBox.IsChecked = _settings.ShowRamInOverlay;
        ShowNetworkInOverlayCheckBox.IsChecked = _settings.ShowNetworkInOverlay;
        ShowNetworkOutOverlayCheckBox.IsChecked = _settings.ShowNetworkOutOverlay;
        TemperatureSourceComboBox.SelectedValue = _settings.TemperatureDataSource.ToString();
        TemperatureRefreshIntervalComboBox.SelectedValue = _settings.TemperatureRefreshSeconds.ToString();
        ShowWeatherInOverlayCheckBox.IsChecked = _settings.ShowWeatherInOverlay;
        ShowNowPlayingInOverlayCheckBox.IsChecked = _settings.ShowNowPlayingInOverlay;
        ShowAnimeInfoInOverlayCheckBox.IsChecked = _settings.ShowAnimeInfoInOverlay;
        WeatherLocationTextBox.Text = _settings.WeatherLocation;
        _lastValidatedWeatherLocation = _settings.WeatherLocation;
        WeatherRefreshIntervalComboBox.SelectedValue = _settings.WeatherRefreshMinutes.ToString();
        SyncOverlayWidgetOrderUi();
        BluetoothDeviceViewModel.SetLowBatteryThreshold(_settings.LowBatteryThreshold);
        SyncOverlayPositionButtons(_settings.OverlayPosition);
        _initializingSettings = false;
        _batteryService = new BluetoothBatteryService(Dispatcher);
        _playStationBatteryService = new PlayStationBatteryService();
        _steelSeriesBatteryService = new SteelSeriesBatteryService();
        DataContext = _batteryService;
        _batteryService.Devices.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null)
            {
                foreach (BluetoothDeviceViewModel device in args.NewItems)
                {
                    RestoreDeviceOverlayVisibility(device);
                    RestoreDeviceAlias(device);
                }
            }
            UpdateEmptyState();
            ApplyPlayStationBattery();
            UpdateDeviceStatusText();
        };
        _playStationBatteryService.BatteryStatusChanged += (_, status) => Dispatcher.BeginInvoke(() =>
        {
            _playStationBatteryStatuses[status.ControllerKind] = status;
            ApplyPlayStationBattery(status.ControllerKind);
        });
        _steelSeriesBatteryService.StatusChanged += (_, status) => Dispatcher.BeginInvoke(() =>
            ApplySteelSeriesStatus(status));
        _batteryService.WatcherFailed += (_, message) =>
        {
            StatusText.Text = $"Bluetooth discovery failed: {message}";
            EmptyState.Visibility = Visibility.Visible;
        };
        _batteryService.EnumerationCompleted += (_, _) =>
        {
            UpdateDeviceStatusText();
            UpdateEmptyState();
        };
        InitializeUpdateSchedule();
        Loaded += OnLoaded;
        StateChanged += (_, _) =>
        {
            UpdateWindowStateVisuals();
            UpdateWindowFrameClip();
            if (WindowState == WindowState.Minimized)
            {
                ShowInTaskbar = false;
                Hide();
            }
        };
        SizeChanged += (_, _) =>
        {
            UpdateResponsiveLayout();
            UpdateWindowFrameClip();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        ApplyCustomCornerPreference(handle);
        _source = HwndSource.FromHwnd(handle);
        _source.AddHook(WndProc);
        RegisterHotKey(handle, HotkeyId, ModControl | ModShift, KeyB);
        InitializeTrayIcon();
        _batteryService.Start();
        _playStationBatteryService.Start();
        _steelSeriesBatteryService.Start();
        UpdateResponsiveLayout();
        UpdateWindowFrameClip();
        UpdateWindowStateVisuals();
        if (_settings.ShowOverlayOnStartup)
            ToggleOverlay();
        if (_launchedAtStartup)
        {
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void InitializeTrayIcon()
    {
        if (_trayIcon is not null) return;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Bluetooth Monitor", null, (_, _) => Dispatcher.Invoke(ShowMainWindow));
        menu.Items.Add("Toggle overlay", null, (_, _) => Dispatcher.Invoke(ToggleOverlay));
        menu.Items.Add(CreateOverlayPositionTrayMenu());
        _trayUpdateMenuItem = new Forms.ToolStripMenuItem("Check for updates...", null, (_, _) => Dispatcher.Invoke(PromptOrShowUpdate));
        menu.Items.Add(_trayUpdateMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "Bluetooth Battery Monitor",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
        _trayIcon.BalloonTipClicked += (_, _) => Dispatcher.Invoke(PromptOrShowUpdate);
    }

    private Forms.ToolStripMenuItem CreateOverlayPositionTrayMenu()
    {
        _trayOverlayPositionItems.Clear();

        var rootItem = new Forms.ToolStripMenuItem("Overlay position");
        foreach (var position in Enum.GetValues<OverlayPosition>())
        {
            var item = new Forms.ToolStripMenuItem(GetOverlayPositionLabel(position))
            {
                Checked = position == _settings.OverlayPosition,
                CheckOnClick = false
            };
            item.Click += (_, _) => Dispatcher.Invoke(() =>
                SetOverlayPosition(position, updateComboBox: true, updateButtons: true));
            _trayOverlayPositionItems[position] = item;
            rootItem.DropDownItems.Add(item);
        }

        return rootItem;
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var extractedIcon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (extractedIcon is not null)
                return (System.Drawing.Icon)extractedIcon.Clone();
        }

        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(27, 31, 39));
            graphics.FillEllipse(background, 1, 1, 30, 30);

            using var pen = new Pen(Color.FromArgb(110, 231, 216), 2.5f);
            graphics.DrawRectangle(pen, new Rectangle(8, 10, 15, 13));
            graphics.DrawLine(pen, 24, 14, 24, 19);
            using var charge = new SolidBrush(Color.FromArgb(110, 231, 216));
            graphics.FillRectangle(charge, 11, 13, 8, 7);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private void ShowMainWindow()
    {
        ShowInTaskbar = true;
        if (!IsVisible)
            Show();

        WindowState = WindowState.Normal;
        Activate();
    }

    public void BringToFrontFromExternalLaunch()
    {
        ShowMainWindow();
    }

    private void SidebarDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDashboardView();
        MainScrollViewer.ScrollToVerticalOffset(0);
        ShowMainWindow();
    }

    private void SidebarSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsView();
        ShowMainWindow();
    }

    private void SidebarInfoButton_Click(object sender, RoutedEventArgs e)
    {
        ShowAboutView();
        ShowMainWindow();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        Close();

    private void OverlayButton_Click(object sender, RoutedEventArgs e) => ToggleOverlay();

    private void OverlayVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: BluetoothDeviceViewModel device })
            return;

        device.ShowInOverlay = !device.ShowInOverlay;
        _settings.DeviceOverlayVisibility[device.Id] = device.ShowInOverlay;
        _settings.Save();
    }

    private void AliasButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: BluetoothDeviceViewModel device })
            return;

        var dialog = new DeviceAliasDialog(device.Name, device.Alias)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        device.Alias = dialog.Alias;
        if (string.IsNullOrWhiteSpace(device.Alias))
            _settings.DeviceAliases.Remove(device.Id);
        else
            _settings.DeviceAliases[device.Id] = device.Alias;

        _settings.Save();
    }

    private void OverlayPositionButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings || sender is not System.Windows.Controls.RadioButton { Tag: string tag })
            return;

        if (!Enum.TryParse<OverlayPosition>(tag, out var position))
            return;

        SetOverlayPosition(position, updateComboBox: true, updateButtons: false);
    }

    private void PositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PositionCombo.SelectedValue is not string value || !Enum.TryParse<OverlayPosition>(value, out var position))
            return;

        if (_initializingSettings)
            return;

        SetOverlayPosition(position, updateComboBox: false, updateButtons: true);
    }

    private void StartupOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings) return;

        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        _settings.ShowOverlayOnStartup = ShowOverlayOnStartupCheckBox.IsChecked == true;
        _settings.KeepRunningInTrayOnClose = KeepRunningInTrayOnCloseCheckBox.IsChecked == true;
        _settings.ShowClockInOverlay = ShowClockInOverlayCheckBox.IsChecked == true;
        _settings.ShowCpuInOverlay = ShowCpuInOverlayCheckBox.IsChecked == true;
        _settings.ShowCpuTemperatureInOverlay = ShowCpuTemperatureInOverlayCheckBox.IsChecked == true;
        _settings.ShowGpuTemperatureInOverlay = ShowGpuTemperatureInOverlayCheckBox.IsChecked == true;
        _settings.ShowRamInOverlay = ShowRamInOverlayCheckBox.IsChecked == true;
        _settings.ShowNetworkInOverlay = ShowNetworkInOverlayCheckBox.IsChecked == true;
        _settings.ShowNetworkOutOverlay = ShowNetworkOutOverlayCheckBox.IsChecked == true;
        _settings.ShowNowPlayingInOverlay = ShowNowPlayingInOverlayCheckBox.IsChecked == true;
        _settings.ShowAnimeInfoInOverlay = ShowAnimeInfoInOverlayCheckBox.IsChecked == true;
        _settings.AutoCheckForUpdates = AutoCheckForUpdatesCheckBox.IsChecked == true;
        _settings.Save();
        StartupManager.SetEnabled(_settings.StartWithWindows);
        _overlay?.ApplySystemWidgetSettings(_settings);
        _overlay?.ApplyNowPlayingSettings(_settings);
    }

    private async void WeatherSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings) return;

        _settings.ShowWeatherInOverlay = ShowWeatherInOverlayCheckBox.IsChecked == true;
        if (WeatherRefreshIntervalComboBox.SelectedValue is string refreshMinutes
            && int.TryParse(refreshMinutes, out var parsedRefreshMinutes))
            _settings.WeatherRefreshMinutes = parsedRefreshMinutes;

        var candidateLocation = WeatherLocationTextBox.Text.Trim();
        WeatherSnapshot? validatedSnapshot = null;
        if (_settings.ShowWeatherInOverlay
            && !candidateLocation.Equals(_lastValidatedWeatherLocation, StringComparison.OrdinalIgnoreCase))
        {
            _weatherValidationCancellation?.Cancel();
            _weatherValidationCancellation?.Dispose();
            _weatherValidationCancellation = new CancellationTokenSource();
            var cancellationToken = _weatherValidationCancellation.Token;
            WeatherLocationValidationText.Text = "Checking location…";
            WeatherLocationValidationText.Foreground = System.Windows.Media.Brushes.LightSkyBlue;

            try
            {
                validatedSnapshot = await _weatherService.GetCurrentAsync(candidateLocation, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                _settings.WeatherLocation = validatedSnapshot.Location;
                _lastValidatedWeatherLocation = validatedSnapshot.Location;
                WeatherLocationTextBox.Text = validatedSnapshot.Location;
                WeatherLocationValidationText.Text = $"Location found: {validatedSnapshot.Location}";
                WeatherLocationValidationText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (WeatherServiceException exception)
            {
                WeatherLocationValidationText.Text = exception.Message;
                WeatherLocationValidationText.Foreground = System.Windows.Media.Brushes.LightCoral;
                _settings.Normalize();
                _settings.Save();
                return;
            }
            catch
            {
                WeatherLocationValidationText.Text = "The location could not be validated right now.";
                WeatherLocationValidationText.Foreground = System.Windows.Media.Brushes.LightCoral;
                return;
            }
        }

        _settings.Normalize();
        _settings.Save();

        if (_overlay is not null)
            await _overlay.ApplyWeatherSettingsAsync(_settings, validatedSnapshot);
    }

    private async void TemperatureSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingSettings)
            return;

        if (TemperatureSourceComboBox.SelectedValue is string sourceValue
            && Enum.TryParse<TemperatureDataSourceMode>(sourceValue, out var sourceMode))
        {
            _settings.TemperatureDataSource = sourceMode;
        }

        if (TemperatureRefreshIntervalComboBox.SelectedValue is string refreshSeconds
            && int.TryParse(refreshSeconds, out var parsedRefreshSeconds))
        {
            _settings.TemperatureRefreshSeconds = parsedRefreshSeconds;
        }

        _settings.Normalize();
        _settings.Save();

        if (_overlay is not null)
            await _overlay.ApplyTemperatureSettingsAsync(_settings);
    }

    private void WeatherLocationTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        WeatherSettings_Changed(sender, e);
    }

    private void ResetOverlaySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _initializingSettings = true;

        _settings.OverlayPosition = OverlayPosition.TopRight;
        _settings.OverlayScale = 1d;
        _settings.OverlayBackgroundOpacity = 0.78d;
        _settings.OverlayTextOpacity = 1d;
        _settings.ShowClockInOverlay = false;
        _settings.ShowCpuInOverlay = false;
        _settings.ShowCpuTemperatureInOverlay = false;
        _settings.ShowGpuTemperatureInOverlay = false;
        _settings.ShowRamInOverlay = false;
        _settings.ShowNetworkInOverlay = false;
        _settings.ShowNetworkOutOverlay = false;
        _settings.ShowNowPlayingInOverlay = true;
        _settings.ShowAnimeInfoInOverlay = true;
        _settings.TemperatureDataSource = TemperatureDataSourceMode.Auto;
        _settings.TemperatureRefreshSeconds = AppSettings.DefaultTemperatureRefreshSeconds;
        _settings.OverlayWidgetOrder = [.. OverlayWidgetOrderSettings.Defaults.Select(widget => widget.ToString())];

        PositionCombo.SelectedValue = _settings.OverlayPosition.ToString();
        SyncOverlayPositionButtons(_settings.OverlayPosition);
        SyncOverlayWidgetOrderUi();
        ShowClockInOverlayCheckBox.IsChecked = false;
        ShowCpuInOverlayCheckBox.IsChecked = false;
        ShowCpuTemperatureInOverlayCheckBox.IsChecked = false;
        ShowGpuTemperatureInOverlayCheckBox.IsChecked = false;
        ShowRamInOverlayCheckBox.IsChecked = false;
        ShowNetworkInOverlayCheckBox.IsChecked = false;
        ShowNetworkOutOverlayCheckBox.IsChecked = false;
        ShowNowPlayingInOverlayCheckBox.IsChecked = true;
        ShowAnimeInfoInOverlayCheckBox.IsChecked = true;
        TemperatureSourceComboBox.SelectedValue = _settings.TemperatureDataSource.ToString();
        TemperatureRefreshIntervalComboBox.SelectedValue = _settings.TemperatureRefreshSeconds.ToString();

        OverlayScaleSlider.Value = _settings.OverlayScale * 100d;
        OverlayScaleValueText.Text = $"{(int)Math.Round(OverlayScaleSlider.Value)}%";
        OverlayBackgroundOpacitySlider.Value = _settings.OverlayBackgroundOpacity * 100d;
        OverlayBackgroundOpacityValueText.Text = $"{(int)Math.Round(OverlayBackgroundOpacitySlider.Value)}%";
        OverlayTextOpacitySlider.Value = _settings.OverlayTextOpacity * 100d;
        OverlayTextOpacityValueText.Text = $"{(int)Math.Round(OverlayTextOpacitySlider.Value)}%";

        _initializingSettings = false;

        _settings.Save();
        _overlay?.SetPosition(_settings.OverlayPosition);
        _overlay?.SetScale(_settings.OverlayScale);
        _overlay?.SetBackgroundOpacity(_settings.OverlayBackgroundOpacity);
        _overlay?.SetTextOpacity(_settings.OverlayTextOpacity);
        _overlay?.ApplySystemWidgetSettings(_settings);
        _overlay?.ApplyWidgetOrderSettings(_settings);
    }

    private void OverlayBackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var opacity = Math.Clamp(e.NewValue / 100d, 0d, 1d);
        if (OverlayBackgroundOpacityValueText is not null)
            OverlayBackgroundOpacityValueText.Text = $"{(int)Math.Round(e.NewValue)}%";

        if (_initializingSettings) return;

        _settings.OverlayBackgroundOpacity = opacity;
        _settings.Save();
        _overlay?.SetBackgroundOpacity(opacity);
    }

    private void OverlayScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var scale = Math.Clamp(e.NewValue / 100d, 0.5d, 2d);
        if (OverlayScaleValueText is not null)
            OverlayScaleValueText.Text = $"{(int)Math.Round(e.NewValue)}%";

        if (_initializingSettings) return;

        _settings.OverlayScale = scale;
        _settings.Save();
        _overlay?.SetScale(scale);
    }

    private void OverlayTextOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var opacity = Math.Clamp(e.NewValue / 100d, 0.15d, 1d);
        if (OverlayTextOpacityValueText is not null)
            OverlayTextOpacityValueText.Text = $"{(int)Math.Round(e.NewValue)}%";

        if (_initializingSettings) return;

        _settings.OverlayTextOpacity = opacity;
        _settings.Save();
        _overlay?.SetTextOpacity(opacity);
    }

    private void LowBatteryThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var threshold = (int)Math.Round(e.NewValue);
        if (LowBatteryThresholdValueText is not null)
            LowBatteryThresholdValueText.Text = $"{threshold}%";

        if (_initializingSettings)
            return;

        _settings.LowBatteryThreshold = threshold;
        BluetoothDeviceViewModel.SetLowBatteryThreshold(threshold);
        foreach (var device in _batteryService.Devices)
            device.ReevaluateLowBatteryAttention();
        _settings.Save();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Looking for connected devices…";
        EmptyState.Visibility = Visibility.Collapsed;
        _batteryService.Refresh();
        _steelSeriesBatteryService.Refresh();
    }

    private void ToggleOverlay()
    {
        if (_overlay is null)
        {
            _overlay = new OverlayWindow(
                _batteryService.Devices,
                _settings.OverlayPosition,
                _settings,
                new WindowInteropHelper(this).Handle,
                _weatherService,
                _temperatureTelemetryService);
            _overlay.Closed += (_, _) =>
            {
                _overlay = null;
                UpdateOverlayButtonVisualState();
            };
            _overlay.Show();
            UpdateOverlayButtonVisualState();
        }
        else
        {
            _overlay.Close();
        }
    }

    private void UpdateEmptyState() =>
        EmptyState.Visibility = _batteryService.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void ShowDashboardView()
    {
        SetCurrentView(MainView.Dashboard);
    }

    private void ShowSettingsView()
    {
        SetCurrentView(MainView.Settings);
    }

    private void ShowAboutView()
    {
        SetCurrentView(MainView.About);
    }

    private void SetCurrentView(MainView view)
    {
        if (DashboardView is null || SettingsView is null || AboutView is null) return;

        DashboardView.Visibility = view == MainView.Dashboard ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = view == MainView.Settings ? Visibility.Visible : Visibility.Collapsed;
        AboutView.Visibility = view == MainView.About ? Visibility.Visible : Visibility.Collapsed;
        ApplySidebarSelection(view);

        if (view == MainView.Settings)
        {
            Dispatcher.BeginInvoke(UpdateResponsiveLayout, DispatcherPriority.Loaded);
        }
    }

    private void ApplySidebarSelection(MainView currentView)
    {
        if (DashboardSidebarButton is null || SettingsSidebarButton is null || AboutSidebarButton is null) return;

        DashboardSidebarButton.Style = (Style)FindResource(currentView == MainView.Dashboard
            ? "SidebarSelectedButtonStyle"
            : "SidebarButtonStyle");

        SettingsSidebarButton.Style = (Style)FindResource(currentView == MainView.Settings
            ? "SidebarSelectedButtonStyle"
            : "SidebarButtonStyle");

        AboutSidebarButton.Style = (Style)FindResource(currentView == MainView.About
            ? "SidebarSelectedButtonStyle"
            : "SidebarButtonStyle");
    }

    private void SetOverlayPosition(OverlayPosition position, bool updateComboBox, bool updateButtons)
    {
        _settings.OverlayPosition = position;

        if (updateComboBox)
            PositionCombo.SelectedValue = position.ToString();

        if (updateButtons)
            SyncOverlayPositionButtons(position);

        SyncTrayOverlayPositionMenu(position);
        _settings.Save();
        _overlay?.SetPosition(position);
    }

    private void SyncTrayOverlayPositionMenu(OverlayPosition position)
    {
        foreach (var (itemPosition, item) in _trayOverlayPositionItems)
            item.Checked = itemPosition == position;
    }

    private void OverlayWidgetOrderListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _overlayWidgetOrderDragStartPoint = e.GetPosition(OverlayWidgetOrderListBox);
        _pendingOverlayWidgetDragItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as OverlayWidgetOrderItem;
    }

    private void OverlayWidgetOrderListBox_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pendingOverlayWidgetDragItem is null)
            return;

        var currentPosition = e.GetPosition(OverlayWidgetOrderListBox);
        if (Math.Abs(currentPosition.X - _overlayWidgetOrderDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPosition.Y - _overlayWidgetOrderDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(
            OverlayWidgetOrderListBox,
            new System.Windows.DataObject(typeof(OverlayWidgetOrderItem), _pendingOverlayWidgetDragItem),
            System.Windows.DragDropEffects.Move);

        _pendingOverlayWidgetDragItem = null;
    }

    private void OverlayWidgetOrderListBox_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(OverlayWidgetOrderItem))
            ? System.Windows.DragDropEffects.Move
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OverlayWidgetOrderListBox_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(OverlayWidgetOrderItem)))
            return;

        if (e.Data.GetData(typeof(OverlayWidgetOrderItem)) is not OverlayWidgetOrderItem sourceItem)
            return;

        var targetItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as OverlayWidgetOrderItem;
        var sourceIndex = _overlayWidgetOrderItems.IndexOf(sourceItem);
        if (sourceIndex < 0)
            return;

        var targetIndex = targetItem is null
            ? _overlayWidgetOrderItems.Count - 1
            : _overlayWidgetOrderItems.IndexOf(targetItem);

        if (targetIndex < 0 || targetIndex == sourceIndex)
            return;

        _overlayWidgetOrderItems.Move(sourceIndex, targetIndex);
        PersistOverlayWidgetOrderFromUi();
        SyncOverlayWidgetOrderUi();
    }

    private void SyncOverlayWidgetOrderUi()
    {
        _settings.OverlayWidgetOrder = OverlayWidgetOrderSettings.Normalize(_settings.OverlayWidgetOrder);
        _overlayWidgetOrderItems.Clear();
        foreach (var (key, index) in _settings.OverlayWidgetOrder.Select((key, index) => (key, index)))
        {
            _overlayWidgetOrderItems.Add(new OverlayWidgetOrderItem
            {
                Key = key,
                Label = ParseOverlayWidgetLabel(key),
                Index = index + 1
            });
        }
    }

    private void PersistOverlayWidgetOrderFromUi()
    {
        for (var index = 0; index < _overlayWidgetOrderItems.Count; index++)
            _overlayWidgetOrderItems[index].Index = index + 1;

        _settings.OverlayWidgetOrder = _overlayWidgetOrderItems.Select(item => item.Key).ToList();
        _settings.Save();
        _overlay?.ApplyWidgetOrderSettings(_settings);
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
                return match;

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static string ParseOverlayWidgetLabel(string value) =>
        Enum.TryParse<OverlayWidgetKind>(value, out var widget)
            ? widget switch
            {
                OverlayWidgetKind.Weather => "Weather",
                OverlayWidgetKind.System => "System stats",
                OverlayWidgetKind.NowPlaying => "Now playing",
                OverlayWidgetKind.Devices => "Devices",
                _ => value
            }
            : value;

    private static string GetOverlayPositionLabel(OverlayPosition position) =>
        position switch
        {
            OverlayPosition.TopLeft => "Top left",
            OverlayPosition.TopRight => "Top right",
            OverlayPosition.CenterLeft => "Center left",
            OverlayPosition.CenterRight => "Center right",
            OverlayPosition.BottomLeft => "Bottom left",
            OverlayPosition.BottomRight => "Bottom right",
            _ => position.ToString()
        };

    private void SyncOverlayPositionButtons(OverlayPosition position)
    {
        if (TopLeftPositionButton is null) return;

        TopLeftPositionButton.IsChecked = position == OverlayPosition.TopLeft;
        TopRightPositionButton.IsChecked = position == OverlayPosition.TopRight;
        CenterLeftPositionButton.IsChecked = position == OverlayPosition.CenterLeft;
        CenterRightPositionButton.IsChecked = position == OverlayPosition.CenterRight;
        BottomLeftPositionButton.IsChecked = position == OverlayPosition.BottomLeft;
        BottomRightPositionButton.IsChecked = position == OverlayPosition.BottomRight;
    }

    private void UpdateWindowStateVisuals()
    {
        if (MaximizeRestoreButton is not null)
            MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";

        if (WindowFrame is not null)
        {
            WindowFrame.CornerRadius = WindowState == WindowState.Maximized
                ? new CornerRadius(12)
                : new CornerRadius(20);
            WindowFrame.Margin = WindowState == WindowState.Maximized
                ? new Thickness(8)
                : new Thickness(0);
        }
    }

    private void UpdateResponsiveLayout()
    {
        if (SettingsCardsGrid is null
            || SettingsPrimaryColumnDefinition is null
            || SettingsGapColumnDefinition is null
            || SettingsSecondaryColumnDefinition is null
            || SettingsPrimaryColumn is null
            || OverlaySettingsCard is null)
        {
            return;
        }

        var compact = ResponsiveLayoutHelper.UseCompactSettingsLayout(SettingsCardsGrid.ActualWidth);
        SettingsGapColumnDefinition.Width = compact ? new GridLength(0) : new GridLength(24);
        SettingsPrimaryColumnDefinition.Width = new GridLength(1.05, GridUnitType.Star);
        SettingsSecondaryColumnDefinition.Width = compact
            ? new GridLength(0)
            : new GridLength(1.25, GridUnitType.Star);

        Grid.SetRow(SettingsPrimaryColumn, 0);
        Grid.SetColumn(SettingsPrimaryColumn, 0);
        Grid.SetColumnSpan(SettingsPrimaryColumn, 1);
        SettingsPrimaryColumn.MaxWidth = compact ? double.PositiveInfinity : 560;

        Grid.SetRow(OverlaySettingsCard, compact ? 1 : 0);
        Grid.SetColumn(OverlaySettingsCard, compact ? 0 : 2);
        Grid.SetColumnSpan(OverlaySettingsCard, compact ? 3 : 1);
        OverlaySettingsCard.MaxWidth = compact ? double.PositiveInfinity : 720;
        OverlaySettingsCard.Margin = compact
            ? new Thickness(0, 0, 0, 18)
            : new Thickness(0, 0, 0, 18);
    }

    private void UpdateOverlayButtonVisualState()
    {
        if (OverlayButton is null)
            return;

        var overlayVisible = _overlay is not null;
        OverlayButton.Content = overlayVisible ? "Hide overlay" : "Show overlay";
        OverlayButton.Style = (Style)FindResource(overlayVisible
            ? "PillButtonStyle"
            : "AccentButtonStyle");
    }

    private readonly Dictionary<PlayStationControllerKind, PlayStationBatteryStatus> _playStationBatteryStatuses = [];

    private const string SteelSeriesDeviceId = "usb:1038:arctis-7-plus";

    private void ApplyPlayStationBattery()
    {
        ApplyPlayStationBattery(PlayStationControllerKind.DualShock4);
        ApplyPlayStationBattery(PlayStationControllerKind.DualSense);
    }

    private void ApplyPlayStationBattery(PlayStationControllerKind controllerKind)
    {
        if (!_playStationBatteryStatuses.TryGetValue(controllerKind, out var status))
            return;

        foreach (var controller in FindMatchingPlayStationDevices(controllerKind))
            controller.UpdateBatteryStatus(status.BatteryLevel, status.IsCharging, isExternal: true);
    }

    private IEnumerable<BluetoothDeviceViewModel> FindMatchingPlayStationDevices(PlayStationControllerKind controllerKind)
    {
        var specificMatches = new List<BluetoothDeviceViewModel>();

        foreach (var device in _batteryService.Devices.Where(device => device.IsConnected))
        {
            var name = device.Name;
            if (controllerKind == PlayStationControllerKind.DualSense
                && (name.Contains("DualSense", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("PS5", StringComparison.OrdinalIgnoreCase)))
            {
                specificMatches.Add(device);
                continue;
            }

            if (controllerKind == PlayStationControllerKind.DualShock4
                && (name.Contains("DualShock", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("PS4", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("DualSense Windows Controller", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Wireless Controller", StringComparison.OrdinalIgnoreCase)))
            {
                specificMatches.Add(device);
            }
        }

        if (specificMatches.Count != 0)
        {
            foreach (var match in specificMatches)
                yield return match;
            yield break;
        }

        var sonyLikeDevices = _batteryService.Devices
            .Where(device => device.IsConnected && IsSonyControllerName(device.Name))
            .ToList();

        if (sonyLikeDevices.Count == 1)
            yield return sonyLikeDevices[0];
    }

    private static bool IsSonyControllerName(string name) =>
        name.Contains("controller", StringComparison.OrdinalIgnoreCase)
        || name.Contains("dualshock", StringComparison.OrdinalIgnoreCase)
        || name.Contains("dualsense", StringComparison.OrdinalIgnoreCase)
        || name.Contains("playstation", StringComparison.OrdinalIgnoreCase)
        || name.Contains("ps4", StringComparison.OrdinalIgnoreCase)
        || name.Contains("ps5", StringComparison.OrdinalIgnoreCase);

    private void ApplySteelSeriesStatus(SteelSeriesHeadsetStatus status)
    {
        var device = _batteryService.Devices.FirstOrDefault(device => device.Id == SteelSeriesDeviceId);
        if (!status.IsConnected)
        {
            if (device is not null)
                _batteryService.Devices.Remove(device);
            return;
        }

        if (device is null)
        {
            device = new BluetoothDeviceViewModel
            {
                Id = SteelSeriesDeviceId,
                Name = status.Name,
                IsConnected = true,
                Category = BluetoothDeviceCategory.Headset
            };
            device.UpdateBatteryStatus(status.BatteryLevel, status.IsCharging, isExternal: true);
            var insertionIndex = 0;
            while (insertionIndex < _batteryService.Devices.Count
                   && string.Compare(_batteryService.Devices[insertionIndex].Name, device.Name, StringComparison.CurrentCultureIgnoreCase) < 0)
                insertionIndex++;
            _batteryService.Devices.Insert(insertionIndex, device);
            return;
        }

        device.Name = status.Name;
        device.IsConnected = true;
        device.UpdateBatteryStatus(status.BatteryLevel, status.IsCharging, isExternal: true);
    }

    private void UpdateDeviceStatusText()
    {
        var count = _batteryService.Devices.Count;
        StatusText.Text = $"{count} connected device{(count == 1 ? "" : "s")} found";
    }

    private void RestoreDeviceOverlayVisibility(BluetoothDeviceViewModel device)
    {
        if (_settings.DeviceOverlayVisibility.TryGetValue(device.Id, out var showInOverlay))
            device.ShowInOverlay = showInOverlay;
    }

    private void RestoreDeviceAlias(BluetoothDeviceViewModel device)
    {
        if (_settings.DeviceAliases.TryGetValue(device.Id, out var alias))
            device.Alias = alias;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmGetMinMaxInfoMessage)
        {
            ApplyMaximizedBounds(hwnd, lParam);
            handled = true;
            return IntPtr.Zero;
        }

        if (message == 0x0312 && wParam.ToInt32() == HotkeyId)
        {
            ToggleOverlay();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void ApplyMaximizedBounds(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, 0x00000002);
        if (monitor == IntPtr.Zero)
            return;

        var monitorInfo = new MonitorInfo();
        monitorInfo.Size = Marshal.SizeOf<MonitorInfo>();
        if (!GetMonitorInfo(monitor, ref monitorInfo))
            return;

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMaxInfo.MaxPosition.X = monitorInfo.WorkArea.Left - monitorInfo.MonitorArea.Left;
        minMaxInfo.MaxPosition.Y = monitorInfo.WorkArea.Top - monitorInfo.MonitorArea.Top;
        minMaxInfo.MaxSize.X = monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left;
        minMaxInfo.MaxSize.Y = monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top;
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi / 96d;
        minMaxInfo.MinTrackSize.X = (int)Math.Ceiling(MinimumWindowWidth * scale);
        minMaxInfo.MinTrackSize.Y = (int)Math.Ceiling(MinimumWindowHeight * scale);
        Marshal.StructureToPtr(minMaxInfo, lParam, fDeleteOld: true);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exitRequested && _settings.KeepRunningInTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _overlay?.Close();
        _batteryService.Dispose();
        _playStationBatteryService.Dispose();
        _steelSeriesBatteryService.Dispose();
        _libreHardwareMonitorTemperatureSource.Dispose();
        _weatherValidationCancellation?.Cancel();
        _weatherValidationCancellation?.Dispose();
        _weatherHttpClient.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Icon?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        if (_source is not null)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _source.RemoveHook(WndProc);
        }
        base.OnClosing(e);
    }

    private static void ApplyCustomCornerPreference(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;

        const uint defaultCornerPreference = 0;
        var preference = defaultCornerPreference;
        _ = DwmSetWindowAttribute(
            handle,
            DwmWindowCornerPreferenceAttribute,
            ref preference,
            sizeof(uint));
    }

    private void UpdateWindowFrameClip()
    {
        if (WindowFrame is null)
            return;

        var bounds = new Rect(0, 0, WindowFrame.ActualWidth, WindowFrame.ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var radius = WindowState == WindowState.Maximized
            ? 0d
            : Math.Min(WindowCornerRadius, Math.Min(bounds.Width, bounds.Height) / 2d);

        var clip = CreateRoundedRectangleGeometry(bounds, radius);
        WindowFrame.Clip = clip;
        if (WindowContentRoot is not null)
            WindowContentRoot.Clip = clip.Clone();
    }

    private static Media.Geometry CreateRoundedRectangleGeometry(Rect bounds, double radius)
    {
        if (radius <= 0d)
            return new Media.RectangleGeometry(bounds);

        var geometry = new Media.StreamGeometry();
        using var context = geometry.Open();

        var topLeft = new System.Windows.Point(bounds.Left + radius, bounds.Top);
        var topRight = new System.Windows.Point(bounds.Right - radius, bounds.Top);
        var rightTop = new System.Windows.Point(bounds.Right, bounds.Top + radius);
        var rightBottom = new System.Windows.Point(bounds.Right, bounds.Bottom - radius);
        var bottomRight = new System.Windows.Point(bounds.Right - radius, bounds.Bottom);
        var bottomLeft = new System.Windows.Point(bounds.Left + radius, bounds.Bottom);
        var leftBottom = new System.Windows.Point(bounds.Left, bounds.Bottom - radius);
        var leftTop = new System.Windows.Point(bounds.Left, bounds.Top + radius);
        var arcSize = new System.Windows.Size(radius, radius);

        context.BeginFigure(topLeft, true, true);
        context.LineTo(topRight, true, false);
        context.ArcTo(rightTop, arcSize, 0, false, Media.SweepDirection.Clockwise, true, false);
        context.LineTo(rightBottom, true, false);
        context.ArcTo(bottomRight, arcSize, 0, false, Media.SweepDirection.Clockwise, true, false);
        context.LineTo(bottomLeft, true, false);
        context.ArcTo(leftBottom, arcSize, 0, false, Media.SweepDirection.Clockwise, true, false);
        context.LineTo(leftTop, true, false);
        context.ArcTo(topLeft, arcSize, 0, false, Media.SweepDirection.Clockwise, true, false);

        geometry.Freeze();
        return geometry;
    }

    private void InitializeUpdateSchedule()
    {
        if (_settings.AutoCheckForUpdates)
        {
            var startupTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            startupTimer.Tick += async (_, _) =>
            {
                startupTimer.Stop();
                if (_settings.AutoCheckForUpdates)
                    await CheckForUpdatesAsync(silent: true);
            };
            startupTimer.Start();
        }

        _periodicUpdateTimer.Interval = TimeSpan.FromHours(6);
        _periodicUpdateTimer.Tick += async (_, _) =>
        {
            if (_settings.AutoCheckForUpdates)
                await CheckForUpdatesAsync(silent: true);
        };
        _periodicUpdateTimer.Start();
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(silent: false);
    }

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latestUpdateInfo is not null)
        {
            await PerformUpdateDownloadAndApplyAsync(_latestUpdateInfo);
        }
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (_isUpdating) return;

        if (!silent)
        {
            CheckForUpdatesButton.IsEnabled = false;
            UpdateStatusContainer.Visibility = Visibility.Visible;
            UpdateStatusTitleTextBlock.Text = "Checking for updates...";
            UpdateStatusDetailTextBlock.Text = "Connecting to GitHub Releases...";
            InstallUpdateButton.Visibility = Visibility.Collapsed;
            UpdateDownloadProgressBar.Visibility = Visibility.Collapsed;
        }

        var result = await _updateService.CheckForUpdatesAsync();
        _settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
        _settings.Save();

        if (result.UpdateAvailable && result.Update is not null)
        {
            _latestUpdateInfo = result.Update;
            UpdateStatusContainer.Visibility = Visibility.Visible;
            UpdateStatusTitleTextBlock.Text = $"Update available: v{result.Update.Version}";
            UpdateStatusDetailTextBlock.Text = string.IsNullOrWhiteSpace(result.Update.ReleaseName)
                ? "A new version of Bluetooth Battery Monitor is available."
                : result.Update.ReleaseName;
            InstallUpdateButton.Visibility = Visibility.Visible;
            InstallUpdateButton.IsEnabled = true;

            if (_trayUpdateMenuItem is not null)
            {
                _trayUpdateMenuItem.Text = $"🌟 Update available: v{result.Update.Version}";
            }

            if (_trayIcon is not null)
            {
                _trayIcon.ShowBalloonTip(
                    6000,
                    "Update Available",
                    $"Bluetooth Battery Monitor v{result.Update.Version} is available. Click here to install.",
                    Forms.ToolTipIcon.Info);
            }
        }
        else if (result.ErrorMessage is not null)
        {
            if (!silent)
            {
                UpdateStatusContainer.Visibility = Visibility.Visible;
                UpdateStatusTitleTextBlock.Text = "Check failed";
                UpdateStatusDetailTextBlock.Text = result.ErrorMessage;
                InstallUpdateButton.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            _latestUpdateInfo = null;
            if (_trayUpdateMenuItem is not null)
            {
                _trayUpdateMenuItem.Text = "Check for updates...";
            }

            if (!silent)
            {
                UpdateStatusContainer.Visibility = Visibility.Visible;
                UpdateStatusTitleTextBlock.Text = "You're up to date";
                UpdateStatusDetailTextBlock.Text = $"Bluetooth Battery Monitor v{_updateService.CurrentVersion} is the latest version.";
                InstallUpdateButton.Visibility = Visibility.Collapsed;
            }
        }

        if (!silent)
        {
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    private async Task PerformUpdateDownloadAndApplyAsync(UpdateInfo update)
    {
        if (_isUpdating) return;
        _isUpdating = true;

        try
        {
            CheckForUpdatesButton.IsEnabled = false;
            InstallUpdateButton.IsEnabled = false;
            UpdateStatusContainer.Visibility = Visibility.Visible;
            UpdateStatusTitleTextBlock.Text = "Downloading update...";
            UpdateStatusDetailTextBlock.Text = "Starting download...";
            UpdateDownloadProgressBar.Visibility = Visibility.Visible;
            UpdateDownloadProgressBar.Value = 0;

            var progress = new Progress<double>(percent =>
            {
                UpdateDownloadProgressBar.Value = percent * 100d;
                UpdateStatusDetailTextBlock.Text = $"Downloading update: {(int)Math.Round(percent * 100d)}%";
            });

            var downloadedExePath = await _updateService.DownloadUpdateAsync(update, progress);

            UpdateStatusTitleTextBlock.Text = "Applying update...";
            UpdateStatusDetailTextBlock.Text = "Restarting Bluetooth Battery Monitor...";

            _updateService.ApplyUpdateAndRestart(downloadedExePath);
        }
        catch (Exception ex)
        {
            _isUpdating = false;
            CheckForUpdatesButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = true;
            UpdateDownloadProgressBar.Visibility = Visibility.Collapsed;
            UpdateStatusTitleTextBlock.Text = "Update failed";
            UpdateStatusDetailTextBlock.Text = $"Error downloading or applying update: {ex.Message}";
        }
    }

    private async void PromptOrShowUpdate()
    {
        if (_latestUpdateInfo is not null)
        {
            var result = System.Windows.MessageBox.Show(
                $"A new version (v{_latestUpdateInfo.Version}) of Bluetooth Battery Monitor is available.\n\n" +
                $"Release: {_latestUpdateInfo.ReleaseName}\n\n" +
                "Would you like to download and install the update now?",
                "Bluetooth Battery Monitor Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                ShowMainWindow();
                ShowSettingsView();
                await PerformUpdateDownloadAndApplyAsync(_latestUpdateInfo);
            }
        }
        else
        {
            ShowMainWindow();
            ShowSettingsView();
            await CheckForUpdatesAsync(silent: false);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref uint pvAttribute,
        int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointStruct Reserved;
        public PointStruct MaxSize;
        public PointStruct MaxPosition;
        public PointStruct MinTrackSize;
        public PointStruct MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectStruct
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public RectStruct MonitorArea;
        public RectStruct WorkArea;
        public int Flags;
    }
}
