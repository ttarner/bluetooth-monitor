using BluetoothMonitor.Models;
using BluetoothMonitor.Services;

namespace BluetoothMonitor.Tests;

[TestClass]
public sealed class DisplayLayoutTests
{
    [TestMethod]
    public void OverlayLayoutHelper_PlacesTopRightWithinWorkingArea()
    {
        var result = OverlayLayoutHelper.Calculate(
            new DisplayWorkArea(1920, 0, 2560, 1440),
            overlayWidth: 360,
            overlayHeight: 220,
            OverlayPosition.TopRight,
            margin: 24);

        Assert.AreEqual(4096, result.Left);
        Assert.AreEqual(24, result.Top);
        Assert.AreEqual(2512, result.MaxWidth);
        Assert.AreEqual(1392, result.MaxHeight);
    }

    [TestMethod]
    public void OverlayLayoutHelper_ClampsOversizedOverlayToVisibleBounds()
    {
        var result = OverlayLayoutHelper.Calculate(
            new DisplayWorkArea(0, 0, 1280, 720),
            overlayWidth: 1800,
            overlayHeight: 900,
            OverlayPosition.BottomRight,
            margin: 24);

        Assert.AreEqual(24, result.Left);
        Assert.AreEqual(24, result.Top);
        Assert.AreEqual(1232, result.MaxWidth);
        Assert.AreEqual(672, result.MaxHeight);
    }

    [TestMethod]
    public void ResponsiveLayoutHelper_UsesCompactLayoutBelowThreshold()
    {
        Assert.IsTrue(ResponsiveLayoutHelper.UseCompactSettingsLayout(1180));
        Assert.IsFalse(ResponsiveLayoutHelper.UseCompactSettingsLayout(1500));
    }
}
