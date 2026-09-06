namespace BluetoothMonitor.Services;

public static class ResponsiveLayoutHelper
{
    public const double CompactSettingsThreshold = 1320d;

    public static bool UseCompactSettingsLayout(double availableWidth) =>
        availableWidth > 0d && availableWidth < CompactSettingsThreshold;
}
