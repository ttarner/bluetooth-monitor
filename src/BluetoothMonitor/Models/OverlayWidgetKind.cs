namespace BluetoothMonitor.Models;

public enum OverlayWidgetKind
{
    Weather,
    System,
    NowPlaying,
    Devices
}

public static class OverlayWidgetOrderSettings
{
    private static readonly OverlayWidgetKind[] DefaultOrder =
    [
        OverlayWidgetKind.Weather,
        OverlayWidgetKind.System,
        OverlayWidgetKind.NowPlaying,
        OverlayWidgetKind.Devices
    ];

    public static IReadOnlyList<OverlayWidgetKind> Defaults => DefaultOrder;

    public static List<string> Normalize(IEnumerable<string>? configuredOrder)
    {
        var normalized = new List<string>(DefaultOrder.Length);
        var seen = new HashSet<OverlayWidgetKind>();

        if (configuredOrder is not null)
        {
            foreach (var entry in configuredOrder)
            {
                if (!Enum.TryParse<OverlayWidgetKind>(entry, ignoreCase: true, out var widget)
                    || !seen.Add(widget))
                {
                    continue;
                }

                normalized.Add(widget.ToString());
            }
        }

        foreach (var widget in DefaultOrder)
        {
            if (seen.Add(widget))
                normalized.Add(widget.ToString());
        }

        return normalized;
    }
}
