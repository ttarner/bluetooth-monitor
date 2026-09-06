using System.Collections.ObjectModel;
using System.Windows.Threading;
using BluetoothMonitor.Models;
using Windows.Devices.Enumeration;

namespace BluetoothMonitor.Services;

public sealed class BluetoothBatteryService : IDisposable
{
    private const string BluetoothProtocolId = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string BluetoothLeProtocolId = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string BatteryLifeProperty = "System.Devices.BatteryLife";
    private const string ChargingStateProperty = "System.Devices.ChargingState";
    private const string ChargingStatePropertyKey = "{49CD1F76-5626-4B17-A4E8-18B4AA1A2213} 11";
    private const string CategoryProperty = "System.Devices.Aep.Category";
    // DEVPKEY_Device_BatteryLevel is the value displayed by Windows Settings
    // for Bluetooth LE accessories such as keyboards and mice.
    private const string BatteryLevelProperty = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    private static readonly string[] RequestedProperties =
    [
        IsConnectedProperty,
        BatteryLifeProperty,
        ChargingStateProperty,
        ChargingStatePropertyKey,
        CategoryProperty,
        BatteryLevelProperty,
        "System.Devices.Aep.DeviceAddress"
    ];

    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, BluetoothDeviceViewModel> _byId = [];
    private readonly Dictionary<string, string> _batteryDeviceNames = [];
    private readonly Dictionary<string, int> _batteryByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _chargingByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DeviceWatcher> _watchers = [];
    private int _completedWatchers;

    public BluetoothBatteryService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public ObservableCollection<BluetoothDeviceViewModel> Devices { get; } = [];
    public event EventHandler? EnumerationCompleted;
    public event EventHandler<string>? WatcherFailed;

    public void Start()
    {
        if (_watchers.Count != 0) return;

        try
        {
            _completedWatchers = 0;
            StartWatcher(BluetoothProtocolId);
            StartWatcher(BluetoothLeProtocolId);
            StartBatteryWatcher();
        }
        catch (Exception exception)
        {
            StopWatchers();
            WatcherFailed?.Invoke(this, exception.Message);
        }
    }

    private void StartWatcher(string protocolId)
    {
        var selector = $"System.Devices.Aep.ProtocolId:=\"{protocolId}\" AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";
        var watcher = DeviceInformation.CreateWatcher(selector, RequestedProperties, DeviceInformationKind.AssociationEndpoint);
        watcher.Added += OnAdded;
        watcher.Updated += OnUpdated;
        watcher.Removed += OnRemoved;
        watcher.EnumerationCompleted += OnEnumerationCompleted;
        _watchers.Add(watcher);
        watcher.Start();
    }

    private void StartBatteryWatcher()
    {
        var selector = $"System.Devices.ClassGuid:=\"{BluetoothProtocolId}\"";
        var watcher = DeviceInformation.CreateWatcher(selector, RequestedProperties, DeviceInformationKind.Device);
        watcher.Added += OnBatteryDeviceAdded;
        watcher.Updated += OnBatteryDeviceUpdated;
        watcher.Removed += OnBatteryDeviceRemoved;
        watcher.EnumerationCompleted += OnEnumerationCompleted;
        _watchers.Add(watcher);
        watcher.Start();
    }

    public void Refresh()
    {
        StopWatchers();
        _dispatcher.Invoke(() =>
        {
            Devices.Clear();
            _byId.Clear();
            _batteryDeviceNames.Clear();
            _batteryByName.Clear();
            _chargingByName.Clear();
        });
        Start();
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation info) => _dispatcher.BeginInvoke(() =>
    {
        if (_byId.ContainsKey(info.Id)) return;
        var device = new BluetoothDeviceViewModel { Id = info.Id };
        Apply(device, info.Name, info.Properties);
        if (device.IsConnected && device.BatteryLevel is null && _batteryByName.TryGetValue(NormalizeName(device.Name), out var batteryLevel))
            device.UpdateBatteryStatus(batteryLevel, device.IsCharging);
        if (device.IsConnected && _chargingByName.TryGetValue(NormalizeName(device.Name), out var isCharging))
            device.UpdateBatteryStatus(device.BatteryLevel, isCharging);
        _byId[info.Id] = device;
        if (device.IsConnected)
            InsertSorted(device);
    });

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update) => _dispatcher.BeginInvoke(() =>
    {
        if (_byId.TryGetValue(update.Id, out var device))
        {
            var wasVisible = Devices.Contains(device);
            Apply(device, null, update.Properties);
            if (device.IsConnected && !wasVisible)
                InsertSorted(device);
            else if (!device.IsConnected && wasVisible)
                Devices.Remove(device);
        }
    });

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update) => _dispatcher.BeginInvoke(() =>
    {
        if (!_byId.Remove(update.Id, out var device)) return;
        Devices.Remove(device);
    });

    private void OnBatteryDeviceAdded(DeviceWatcher sender, DeviceInformation info) => _dispatcher.BeginInvoke(() =>
    {
        var name = NormalizeName(info.Name);
        _batteryDeviceNames[info.Id] = name;
        ApplyBatteryByName(name, info.Properties);
    });

    private void OnBatteryDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update) => _dispatcher.BeginInvoke(() =>
    {
        if (_batteryDeviceNames.TryGetValue(update.Id, out var name))
            ApplyBatteryByName(name, update.Properties);
    });

    private void OnBatteryDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate update) => _dispatcher.BeginInvoke(() =>
    {
        if (_batteryDeviceNames.Remove(update.Id, out var name))
        {
            _batteryByName.Remove(name);
            _chargingByName.Remove(name);
            foreach (var device in Devices.Where(device => NormalizeName(device.Name).Equals(name, StringComparison.OrdinalIgnoreCase)))
                device.UpdateBatteryStatus(device.BatteryLevel, false);
        }
    });

    private void OnEnumerationCompleted(DeviceWatcher sender, object args) => _dispatcher.BeginInvoke(() =>
    {
        _completedWatchers++;
        if (_completedWatchers >= _watchers.Count)
            EnumerationCompleted?.Invoke(this, EventArgs.Empty);
    });

    private static void Apply(BluetoothDeviceViewModel device, string? name, IReadOnlyDictionary<string, object> properties)
    {
        if (!string.IsNullOrWhiteSpace(name)) device.Name = name;

        if (properties.TryGetValue(IsConnectedProperty, out var connected))
            device.IsConnected = connected is true;

        device.Category = ResolveCategory(device.Name, properties);

        var batteryLevel = TryGetBattery(properties, out var battery)
            ? ConvertBattery(battery)
            : device.BatteryLevel;
        var isCharging = TryGetChargingState(properties, out var chargingState)
            ? chargingState
            : device.IsCharging;

        if (!device.IsConnected)
        {
            device.ClearExternalBatteryStatus();
            device.UpdateBatteryStatus(null, false);
            return;
        }

        device.UpdateBatteryStatus(batteryLevel, isCharging);
    }

    private static bool TryGetBattery(IReadOnlyDictionary<string, object> properties, out object battery) =>
        properties.TryGetValue(BatteryLevelProperty, out battery!)
        || properties.TryGetValue(BatteryLifeProperty, out battery!);

    private void ApplyBatteryByName(string name, IReadOnlyDictionary<string, object> properties)
    {
        if (TryGetBattery(properties, out var rawBattery) && ConvertBattery(rawBattery) is int batteryLevel)
        {
            _batteryByName[name] = batteryLevel;
            foreach (var device in Devices.Where(device => device.IsConnected && NormalizeName(device.Name).Equals(name, StringComparison.OrdinalIgnoreCase)))
                device.UpdateBatteryStatus(
                    batteryLevel,
                    _chargingByName.TryGetValue(name, out var cachedIsCharging) && cachedIsCharging);
        }

        if (TryGetChargingState(properties, out var isCharging))
        {
            _chargingByName[name] = isCharging;
            foreach (var device in Devices.Where(device => device.IsConnected && NormalizeName(device.Name).Equals(name, StringComparison.OrdinalIgnoreCase)))
                device.UpdateBatteryStatus(
                    _batteryByName.TryGetValue(name, out var cachedBatteryLevel) ? cachedBatteryLevel : device.BatteryLevel,
                    isCharging);
        }
    }

    private static bool TryGetChargingState(IReadOnlyDictionary<string, object> properties, out bool isCharging)
    {
        if ((properties.TryGetValue(ChargingStateProperty, out var rawState)
             || properties.TryGetValue(ChargingStatePropertyKey, out rawState))
            && ConvertChargingState(rawState) is bool charging)
        {
            isCharging = charging;
            return true;
        }

        isCharging = false;
        return false;
    }

    private static bool? ConvertChargingState(object state) => state switch
    {
        byte value when value <= 2 => value == 1,
        ushort value when value <= 2 => value == 1,
        uint value when value <= 2 => value == 1,
        int value when value is >= 0 and <= 2 => value == 1,
        _ => null
    };

    private static int? ConvertBattery(object battery) => battery switch
    {
        byte value => value,
        ushort value => value,
        uint value => (int)value,
        int value => value,
        _ => null
    };

    private static string NormalizeName(string name) => name.Trim();

    private static BluetoothDeviceCategory ResolveCategory(string? name, IReadOnlyDictionary<string, object> properties)
    {
        var category = properties.TryGetValue(CategoryProperty, out var rawCategory)
            ? rawCategory?.ToString()
            : null;

        var normalizedName = name?.Trim() ?? string.Empty;
        return ResolveCategory(category, normalizedName);
    }

    private static BluetoothDeviceCategory ResolveCategory(string? category, string name)
    {
        if (!string.IsNullOrWhiteSpace(category))
        {
            if (category.Contains("Input", StringComparison.OrdinalIgnoreCase))
                return ResolveInputCategory(name);

            if (category.Contains("Audio", StringComparison.OrdinalIgnoreCase))
                return name.Contains("head", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("buds", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("pods", StringComparison.OrdinalIgnoreCase)
                    ? BluetoothDeviceCategory.Headset
                    : BluetoothDeviceCategory.Audio;

            if (category.Contains("Phone", StringComparison.OrdinalIgnoreCase))
                return BluetoothDeviceCategory.Phone;

            if (category.Contains("Computer", StringComparison.OrdinalIgnoreCase))
                return BluetoothDeviceCategory.Computer;
        }

        if (name.Contains("controller", StringComparison.OrdinalIgnoreCase)
            || name.Contains("dualshock", StringComparison.OrdinalIgnoreCase)
            || name.Contains("dualsense", StringComparison.OrdinalIgnoreCase)
            || name.Contains("gamepad", StringComparison.OrdinalIgnoreCase))
            return BluetoothDeviceCategory.Controller;

        if (name.Contains("keyboard", StringComparison.OrdinalIgnoreCase)
            || name.Contains("keychron", StringComparison.OrdinalIgnoreCase)
            || name.Contains("air75", StringComparison.OrdinalIgnoreCase))
            return BluetoothDeviceCategory.Keyboard;

        if (name.Contains("mouse", StringComparison.OrdinalIgnoreCase)
            || name.Contains("mx master", StringComparison.OrdinalIgnoreCase))
            return BluetoothDeviceCategory.Mouse;

        if (name.Contains("headset", StringComparison.OrdinalIgnoreCase)
            || name.Contains("headphone", StringComparison.OrdinalIgnoreCase)
            || name.Contains("earbud", StringComparison.OrdinalIgnoreCase)
            || name.Contains("earbuds", StringComparison.OrdinalIgnoreCase)
            || name.Contains("arctis", StringComparison.OrdinalIgnoreCase))
            return BluetoothDeviceCategory.Headset;

        if (name.Contains("speaker", StringComparison.OrdinalIgnoreCase)
            || name.Contains("audio", StringComparison.OrdinalIgnoreCase))
            return BluetoothDeviceCategory.Audio;

        return BluetoothDeviceCategory.Generic;
    }

    private static BluetoothDeviceCategory ResolveInputCategory(string name)
    {
        if (name.Contains("controller", StringComparison.OrdinalIgnoreCase)
            || name.Contains("dualshock", StringComparison.OrdinalIgnoreCase)
            || name.Contains("dualsense", StringComparison.OrdinalIgnoreCase)
            || name.Contains("gamepad", StringComparison.OrdinalIgnoreCase))
            return BluetoothDeviceCategory.Controller;

        if (name.Contains("keyboard", StringComparison.OrdinalIgnoreCase)
            || name.Contains("keychron", StringComparison.OrdinalIgnoreCase)
            || name.Contains("air75", StringComparison.OrdinalIgnoreCase))
            return BluetoothDeviceCategory.Keyboard;

        if (name.Contains("mouse", StringComparison.OrdinalIgnoreCase)
            || name.Contains("mx master", StringComparison.OrdinalIgnoreCase))
            return BluetoothDeviceCategory.Mouse;

        return BluetoothDeviceCategory.Generic;
    }

    private void InsertSorted(BluetoothDeviceViewModel device)
    {
        var index = 0;
        while (index < Devices.Count && string.Compare(Devices[index].Name, device.Name, StringComparison.CurrentCultureIgnoreCase) < 0)
            index++;
        Devices.Insert(index, device);
    }

    private void StopWatchers()
    {
        foreach (var watcher in _watchers)
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                watcher.Stop();
            watcher.Added -= OnAdded;
            watcher.Updated -= OnUpdated;
            watcher.Removed -= OnRemoved;
            watcher.Added -= OnBatteryDeviceAdded;
            watcher.Updated -= OnBatteryDeviceUpdated;
            watcher.Removed -= OnBatteryDeviceRemoved;
            watcher.EnumerationCompleted -= OnEnumerationCompleted;
        }
        _watchers.Clear();
    }

    public void Dispose() => StopWatchers();
}
