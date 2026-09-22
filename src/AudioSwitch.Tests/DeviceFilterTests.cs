using AudioSwitch.Services;
using Xunit;

namespace AudioSwitch.Tests;

public class DeviceFilterTests
{
    [Theory]
    [InlineData("USB Audio Device", true)]
    [InlineData("usb", true)]
    [InlineData("Bluetooth Headset", true)]
    [InlineData("BTHENUM\\DEV", true)]
    [InlineData("Realtek High Definition Audio", false)]
    [InlineData("", false)]
    [InlineData("Unknown", false)]
    public void IsExternalBus_DetectsUsbAndBluetooth(string busType, bool expected)
        => Assert.Equal(expected, DeviceService.IsExternalBus(busType));

    [Theory]
    // 系统杂项：智能模式隐藏
    [InlineData("Stereo Mix", true)]
    [InlineData("立体声混音", true)]
    [InlineData("NVIDIA High Definition Audio", true)]
    [InlineData("Intel® Display Audio", true)]
    [InlineData("Primary Sound Driver", true)]
    [InlineData("Sound Mapper", true)]
    [InlineData("", true)]
    // 用户安装的虚拟声卡：应显示（可切换目标）
    [InlineData("VB-Audio Cable", false)]
    [InlineData("VoiceMeeter Input", false)]
    [InlineData("CABLE Input (VB-Audio Virtual Cable)", false)]
    [InlineData("虚拟音频设备", false)]
    [InlineData("Virtual Audio Cable", false)]
    [InlineData("OBS Virtual Camera", false)]
    // 普通设备
    [InlineData("Speakers (Realtek(R) Audio)", false)]
    [InlineData("Microphone (USB Mic)", false)]
    public void IsVirtualOrMisc_HidesOnlySystemJunk(string name, bool expected)
        => Assert.Equal(expected, DeviceService.IsVirtualOrMisc(name));

    [Fact]
    public void IsVirtualOrMisc_NormalizesTrademarkAndWhitespace()
    {
        // ® / 多余空白不应导致漏匹配
        Assert.True(DeviceService.IsVirtualOrMisc("Intel®  Display   Audio"));
    }
}
