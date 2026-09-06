using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BluetoothMonitor.Models;

public sealed class BluetoothDeviceViewModel : INotifyPropertyChanged
{
    private static int s_lowBatteryThresholdPercent = 20;

    private string _name = "Bluetooth device";
    private int? _batteryLevel;
    private bool _isConnected;
    private bool _isCharging;
    private BluetoothDeviceCategory _category;
    private bool _showInOverlay = true;
    private bool _isLowBatteryAttentionActive;
    private int _lowBatteryAttentionSequence;
    private bool _hasExternalBatteryStatus;
    private string _alias = string.Empty;

    public required string Id { get; init; }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public int? BatteryLevel
    {
        get => _batteryLevel;
        set
        {
            if (Set(ref _batteryLevel, value))
            {
                OnBatteryVisualsChanged();
                UpdateLowBatteryAttentionState();
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (Set(ref _isConnected, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    public bool IsCharging
    {
        get => _isCharging;
        set
        {
            if (Set(ref _isCharging, value))
                UpdateLowBatteryAttentionState();
        }
    }

    public BluetoothDeviceCategory Category
    {
        get => _category;
        set
        {
            if (Set(ref _category, value))
            {
                OnPropertyChanged(nameof(CategoryIcon));
                OnPropertyChanged(nameof(CategoryBadgeColor));
            }
        }
    }

    public bool ShowInOverlay
    {
        get => _showInOverlay;
        set
        {
            if (Set(ref _showInOverlay, value))
            {
                OnPropertyChanged(nameof(OverlayVisibilityIcon));
                OnPropertyChanged(nameof(OverlayVisibilityTooltip));
                OnPropertyChanged(nameof(OverlayVisibilityButtonBackground));
                OnPropertyChanged(nameof(OverlayVisibilityButtonForeground));
            }
        }
    }

    public string Alias
    {
        get => _alias;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (Set(ref _alias, normalized))
            {
                OnPropertyChanged(nameof(HasAlias));
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(AliasText));
                OnPropertyChanged(nameof(AliasTooltip));
                OnPropertyChanged(nameof(AliasButtonBackground));
                OnPropertyChanged(nameof(AliasButtonForeground));
            }
        }
    }

    public string BatteryText => BatteryLevel is int value ? $"{value}%" : "—";
    public double BatteryWidth => Math.Max(0, Math.Min(100, BatteryLevel ?? 0));
    public string BatteryColor => BatteryLevel is int level
        ? InterpolateBatteryColor(Math.Clamp(level, 0, 100))
        : "#9299A8";
    public string BatteryOutlineColor => BatteryLevel is int ? BatteryColor : "#748091";
    public string BatteryTipColor => BatteryOutlineColor;
    public string BatteryEmptySegmentColor => "#2D3946";
    public string BatterySegment1Color => GetBatterySegmentColor(1);
    public string BatterySegment2Color => GetBatterySegmentColor(2);
    public string BatterySegment3Color => GetBatterySegmentColor(3);
    public string BatterySegment4Color => GetBatterySegmentColor(4);
    public string StatusText => IsConnected ? "Connected" : "Not connected";
    public bool HasAlias => !string.IsNullOrWhiteSpace(Alias);
    public string DisplayName => HasAlias ? Alias : Name;
    public string AliasText => HasAlias ? Alias : string.Empty;
    public string CategoryIcon => Category switch
    {
        BluetoothDeviceCategory.Controller => "🎮",
        BluetoothDeviceCategory.Keyboard => "⌨",
        BluetoothDeviceCategory.Mouse => "🖱",
        BluetoothDeviceCategory.Headset => "🎧",
        BluetoothDeviceCategory.Audio => "🔊",
        BluetoothDeviceCategory.Phone => "📱",
        BluetoothDeviceCategory.Computer => "💻",
        _ => "BT"
    };
    public string CategoryBadgeColor => Category switch
    {
        BluetoothDeviceCategory.Controller => "#31415C",
        BluetoothDeviceCategory.Keyboard => "#3A3E5B",
        BluetoothDeviceCategory.Mouse => "#3A4A5B",
        BluetoothDeviceCategory.Headset => "#4A3858",
        BluetoothDeviceCategory.Audio => "#4E3F35",
        BluetoothDeviceCategory.Phone => "#2F4A52",
        BluetoothDeviceCategory.Computer => "#34444D",
        _ => "#2D3946"
    };
    public string OverlayVisibilityIcon => ShowInOverlay ? "👁" : "🚫";
    public string OverlayVisibilityTooltip => ShowInOverlay
        ? "Hide this device from the overlay"
        : "Show this device in the overlay";
    public string OverlayVisibilityButtonBackground => ShowInOverlay ? "#304E4A" : "#3A3240";
    public string OverlayVisibilityButtonForeground => ShowInOverlay ? "#9CF5EA" : "#F2B6D6";
    public string AliasTooltip => HasAlias
        ? "Edit this device alias"
        : "Set a device alias";
    public string AliasButtonBackground => HasAlias ? "#30453D" : "#353945";
    public string AliasButtonForeground => HasAlias ? "#B7F2C8" : "#D2DAE7";
    public int LowBatteryAttentionSequence
    {
        get => _lowBatteryAttentionSequence;
        private set => Set(ref _lowBatteryAttentionSequence, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static void SetLowBatteryThreshold(int thresholdPercent)
    {
        s_lowBatteryThresholdPercent = Math.Max(0, thresholdPercent);
    }

    public void UpdateBatteryStatus(int? batteryLevel, bool isCharging, bool isExternal = false)
    {
        if (isExternal)
        {
            _hasExternalBatteryStatus = batteryLevel is not null;
        }
        else if (_hasExternalBatteryStatus)
        {
            return;
        }

        var batteryChanged = Set(ref _batteryLevel, batteryLevel, nameof(BatteryLevel));
        if (batteryChanged)
            OnBatteryVisualsChanged();

        Set(ref _isCharging, isCharging, nameof(IsCharging));
        UpdateLowBatteryAttentionState();
    }

    public void ClearExternalBatteryStatus()
    {
        _hasExternalBatteryStatus = false;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(Name))
            OnPropertyChanged(nameof(DisplayName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void UpdateLowBatteryAttentionState()
    {
        var isLowBattery = _batteryLevel is int level
            && level <= s_lowBatteryThresholdPercent
            && !_isCharging;

        if (isLowBattery)
        {
            if (_isLowBatteryAttentionActive)
                return;

            _isLowBatteryAttentionActive = true;
            LowBatteryAttentionSequence++;
            return;
        }

        _isLowBatteryAttentionActive = false;
    }

    public void ReevaluateLowBatteryAttention() => UpdateLowBatteryAttentionState();

    private void OnBatteryVisualsChanged()
    {
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(BatteryWidth));
        OnPropertyChanged(nameof(BatteryColor));
        OnPropertyChanged(nameof(BatteryOutlineColor));
        OnPropertyChanged(nameof(BatteryTipColor));
        OnPropertyChanged(nameof(BatterySegment1Color));
        OnPropertyChanged(nameof(BatterySegment2Color));
        OnPropertyChanged(nameof(BatterySegment3Color));
        OnPropertyChanged(nameof(BatterySegment4Color));
    }

    private string GetBatterySegmentColor(int segmentIndex)
    {
        if (BatteryLevel is not int level)
            return BatteryEmptySegmentColor;

        var filledSegments = Math.Clamp((int)Math.Ceiling(level / 25d), 0, 4);
        return segmentIndex <= filledSegments
            ? BatteryColor
            : BatteryEmptySegmentColor;
    }

    private static string InterpolateBatteryColor(int level)
    {
        // Smooth red -> amber -> green gradient so every percentage has a
        // color that reflects its level instead of jumping between buckets.
        var low = (R: 255, G: 95, B: 86);
        var middle = (R: 255, G: 209, B: 102);
        var high = (R: 85, G: 224, B: 122);

        var (from, to, amount) = level <= 50
            ? (low, middle, level / 50d)
            : (middle, high, (level - 50) / 50d);

        var red = (int)Math.Round(from.R + (to.R - from.R) * amount);
        var green = (int)Math.Round(from.G + (to.G - from.G) * amount);
        var blue = (int)Math.Round(from.B + (to.B - from.B) * amount);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }
}
