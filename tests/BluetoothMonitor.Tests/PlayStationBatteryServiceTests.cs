using BluetoothMonitor.Services;

namespace BluetoothMonitor.Tests;

[TestClass]
public sealed class PlayStationBatteryServiceTests
{
    [TestMethod]
    public void ParseDualShock4Status_MapsBluetoothBatteryBucket()
    {
        var report = new byte[78];
        report[0] = 0x11;
        report[32] = 0x07;

        var status = PlayStationBatteryService.ParseDualShock4Status(report);

        Assert.IsNotNull(status);
        Assert.AreEqual(PlayStationControllerKind.DualShock4, status.ControllerKind);
        Assert.AreEqual(75, status.BatteryLevel);
        Assert.IsFalse(status.IsCharging);
    }

    [TestMethod]
    public void ParseDualSenseStatus_MapsBluetoothBatteryAndCharging()
    {
        var report = new byte[78];
        report[0] = 0x31;
        report[54] = 0x26;

        var status = PlayStationBatteryService.ParseDualSenseStatus(report);

        Assert.IsNotNull(status);
        Assert.AreEqual(PlayStationControllerKind.DualSense, status.ControllerKind);
        Assert.AreEqual(65, status.BatteryLevel);
        Assert.IsTrue(status.IsCharging);
    }

    [TestMethod]
    public void ParseDualSenseStatus_UsesShiftedBluetoothOffsetWhenNeeded()
    {
        var report = new byte[78];
        report[0] = 0x31;
        report[54] = 0x00;
        report[55] = 0x28;

        var status = PlayStationBatteryService.ParseDualSenseStatus(report);

        Assert.IsNotNull(status);
        Assert.AreEqual(85, status.BatteryLevel);
        Assert.IsTrue(status.IsCharging);
    }

    [TestMethod]
    public void ParseDualSenseStatus_MapsEmptyBatteryBucketToZero()
    {
        var report = new byte[64];
        report[0] = 0x01;
        report[53] = 0x01;

        var status = PlayStationBatteryService.ParseDualSenseStatus(report);

        Assert.IsNotNull(status);
        Assert.AreEqual(0, status.BatteryLevel);
        Assert.IsFalse(status.IsCharging);
    }
}
