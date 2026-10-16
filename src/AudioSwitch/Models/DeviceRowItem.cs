namespace AudioSwitch.Models;

/// <summary>
/// 设备列表行的视图模型：把 <see cref="AudioDevice"/> 与「是否为当前默认」组装成
/// 界面直接可绑定的形式（默认态三重编码：填充 / 边框 / 徽章）。
/// </summary>
public sealed class DeviceRowItem
{
    public DeviceRowItem(AudioDevice device, bool isDefault, DeviceType role)
    {
        Device = device;
        IsDefault = isDefault;
        Role = role;
    }

    public AudioDevice Device { get; }
    public DeviceType Role { get; }

    /// <summary>是否为该角色的当前默认设备。</summary>
    public bool IsDefault { get; }

    /// <summary>是否在线（未断开）。</summary>
    public bool IsConnected => Device.IsConnected;

    public string Id => Device.Id;
    public string Name => Device.Name;

    /// <summary>角色图标（麦克风 / 音箱，Segoe Fluent Icons 字形）。</summary>
    public string IconGlyph => Role == DeviceType.Microphone ? "\uE720" : "\uE7F5";

    /// <summary>副标题：总线类型 · 连接状态，如「USB · 已连接」。</summary>
    public string Subtitle => $"{BusLabel} · {(IsConnected ? "已连接" : "未连接")}";

    /// <summary>总线中文标签。</summary>
    public string BusLabel => DeviceKinds.GetLabel(DeviceKinds.Classify(Device.BusType, Device.Name));

    /// <summary>自动化可访问名称。</summary>
    public string AutomationName => IsDefault ? $"{Name}（当前默认）" : Name;
}
