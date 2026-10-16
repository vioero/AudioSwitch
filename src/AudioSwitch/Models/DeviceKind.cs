namespace AudioSwitch.Models;

/// <summary>
/// 设备总线类型分类（用于列表图标与副标题标签）。
/// </summary>
public enum DeviceKind
{
    /// <summary>USB 设备</summary>
    Usb,
    /// <summary>蓝牙设备</summary>
    Bluetooth,
    /// <summary>主板声卡（HD Audio 等板载设备）</summary>
    Internal,
    /// <summary>显示输出（显卡 HDMI / DP 音频）</summary>
    DisplayOutput,
    /// <summary>用户安装的虚拟声卡（VB-Cable / VoiceMeeter 等）</summary>
    Virtual,
    /// <summary>无法识别</summary>
    Unknown
}

/// <summary>
/// 总线类型字符串 → <see cref="DeviceKind"/> 的分类与中文标签映射。
/// </summary>
public static class DeviceKinds
{
    /// <summary>
    /// 根据 COM 读到的总线类型与设备名推断分类。
    /// </summary>
    public static DeviceKind Classify(string? busType, string? name)
    {
        var bus = (busType ?? string.Empty).ToUpperInvariant();
        var lower = (name ?? string.Empty).ToLowerInvariant();

        if (bus.Contains("USB")) return DeviceKind.Usb;
        if (bus.Contains("BLUETOOTH") || bus.Contains("BTHENUM")) return DeviceKind.Bluetooth;
        if (bus.Contains("VIRTUAL") || bus.Contains("SOFTWARE")) return DeviceKind.Virtual;

        // 显示输出：总线读不到时退回按名称判断（显卡 HDMI 音频）
        if (bus.Contains("HDAUDIO")) return DeviceKind.Internal;
        if (lower.Contains("hdmi") || lower.Contains("display audio") || lower.Contains("显示器"))
            return DeviceKind.DisplayOutput;

        return DeviceKind.Unknown;
    }

    /// <summary>
    /// 分类对应的中文标签（列表副标题用）。
    /// </summary>
    public static string GetLabel(DeviceKind kind) => kind switch
    {
        DeviceKind.Usb => "USB",
        DeviceKind.Bluetooth => "蓝牙",
        DeviceKind.Internal => "主板声卡",
        DeviceKind.DisplayOutput => "显示输出",
        DeviceKind.Virtual => "虚拟声卡",
        _ => "音频设备"
    };
}
