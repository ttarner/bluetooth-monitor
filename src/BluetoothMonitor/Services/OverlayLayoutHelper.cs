using BluetoothMonitor.Models;

namespace BluetoothMonitor.Services;

public readonly record struct OverlayLayoutResult(
    double Left,
    double Top,
    double MaxWidth,
    double MaxHeight);

public static class OverlayLayoutHelper
{
    public static OverlayLayoutResult Calculate(
        DisplayWorkArea workingArea,
        double overlayWidth,
        double overlayHeight,
        OverlayPosition position,
        double margin,
        double minimumHeight = 96d)
    {
        var safeMargin = Math.Max(0d, margin);
        var maxWidth = Math.Max(0d, workingArea.Width - (safeMargin * 2d));
        var maxHeight = Math.Max(minimumHeight, workingArea.Height - (safeMargin * 2d));
        var width = Math.Min(Math.Max(0d, overlayWidth), maxWidth);
        var height = Math.Min(Math.Max(0d, overlayHeight), maxHeight);

        var left = position is OverlayPosition.TopLeft or OverlayPosition.CenterLeft or OverlayPosition.BottomLeft
            ? workingArea.Left + safeMargin
            : workingArea.Right - width - safeMargin;

        var top = position switch
        {
            OverlayPosition.TopLeft or OverlayPosition.TopRight => workingArea.Top + safeMargin,
            OverlayPosition.CenterLeft or OverlayPosition.CenterRight => workingArea.Top + ((workingArea.Height - height) / 2d),
            _ => workingArea.Bottom - height - safeMargin
        };

        left = Math.Clamp(left, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - width));
        top = Math.Clamp(top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - height));

        return new OverlayLayoutResult(left, top, maxWidth, maxHeight);
    }
}
