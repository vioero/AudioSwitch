using AudioSwitch.Models;
using AudioSwitch.Services;
using Xunit;

namespace AudioSwitch.Tests;

/// <summary>
/// 设备展示排序测试：默认设备置顶、在线优先、离线沉底、组内保持原顺序。
/// </summary>
public class DeviceSortTests
{
    private static AudioDevice Dev(string id, bool connected = true)
        => new(id, $"Device-{id}", connected, "USB");

    [Fact]
    public void DefaultDevice_ComesFirst()
    {
        var list = new List<AudioDevice>
        {
            Dev("A"), Dev("B"), Dev("C")
        };

        var sorted = DeviceService.SortForDisplay(list, "C");

        Assert.Equal("C", sorted[0].Id);
        Assert.Equal(new[] { "C", "A", "B" }, sorted.Select(d => d.Id));
    }

    [Fact]
    public void OnlineDevices_ComeBeforeOffline()
    {
        var list = new List<AudioDevice>
        {
            Dev("A", connected: false),
            Dev("B"),
            Dev("C", connected: false),
            Dev("D"),
        };

        var sorted = DeviceService.SortForDisplay(list, defaultId: null);

        Assert.Equal(new[] { "B", "D", "A", "C" }, sorted.Select(d => d.Id));
    }

    [Fact]
    public void OfflineDefault_StillFirst()
    {
        // 极端情况：默认设备恰好离线——依然置顶（用户需要看到当前默认的是什么）
        var list = new List<AudioDevice>
        {
            Dev("A"),
            Dev("B", connected: false),
        };

        var sorted = DeviceService.SortForDisplay(list, "B");

        Assert.Equal("B", sorted[0].Id);
        Assert.Equal(new[] { "B", "A" }, sorted.Select(d => d.Id));
    }

    [Fact]
    public void SameGroup_KeepsEnumerationOrder()
    {
        // 稳定排序：同组内不打乱系统枚举顺序
        var list = new List<AudioDevice>
        {
            Dev("Z"), Dev("M"), Dev("A"), Dev("Q")
        };

        var sorted = DeviceService.SortForDisplay(list, defaultId: null);

        Assert.Equal(new[] { "Z", "M", "A", "Q" }, sorted.Select(d => d.Id));
    }

    [Fact]
    public void DefaultAndOnline_BothWin_OverOfflineGroup()
    {
        var list = new List<AudioDevice>
        {
            Dev("offline1", connected: false),
            Dev("offline2", connected: false),
            Dev("online1"),
            Dev("online2"),
        };

        // 默认是在线设备中的第二个
        var sorted = DeviceService.SortForDisplay(list, "online2");

        Assert.Equal(new[] { "online2", "online1", "offline1", "offline2" }, sorted.Select(d => d.Id));
    }

    [Fact]
    public void EmptyList_ReturnsEmpty()
        => Assert.Empty(DeviceService.SortForDisplay(new List<AudioDevice>(), "X"));

    [Fact]
    public void UnknownDefaultId_DoesNotCrash_JustOnlineFirst()
    {
        // 默认设备被显示模式过滤掉时：没有任何设备匹配 defaultId，退化为仅按在线排序
        var list = new List<AudioDevice> { Dev("A"), Dev("B") };

        var sorted = DeviceService.SortForDisplay(list, "hidden-device-id");

        Assert.Equal(new[] { "A", "B" }, sorted.Select(d => d.Id));
    }
}
