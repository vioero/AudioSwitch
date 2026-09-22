namespace AudioSwitch.Models;

/// <summary>
/// 一个音频设备的轻量描述。
/// Id：设备的唯一编号（切换时用）；
/// Name：设备的显示名字；
/// IsConnected：设备当前是否在线；
/// BusType：设备的连接总线类型（USB/蓝牙/主板声卡/虚拟设备/未知）。
/// </summary>
public sealed record AudioDevice(string Id, string Name, bool IsConnected, string BusType);
