using System.IO;
using System.Text.Json;

namespace AudioSwitch.Services;

/// <summary>
/// 设置管理器：负责保存和加载用户设置（如自定义快捷键）。
/// 使用JSON文件存储，方便移植（免安装软件的特性）。
/// </summary>
public static class SettingsManager
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudioSwitch",
        "settings.json"
    );

    // 修复 M9：加锁保证跨线程读取到一致引用
    private static readonly object _sync = new();
    private static Settings _current = Load();

    /// <summary>
    /// 当前设置（修复 M9：加锁保证跨线程可见性）。
    /// </summary>
    public static Settings Current
    {
        get { lock (_sync) return _current; }
    }

    /// <summary>
    /// 加载设置（如果文件不存在或损坏，返回默认设置）
    /// </summary>
    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<Settings>(json) ?? CreateDefault();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("加载设置失败", ex);
        }
        return CreateDefault();
    }

    /// <summary>
    /// 保存设置到文件（修复 M9：原子写入，先写临时文件再替换）。
    /// </summary>
    public static void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (directory != null && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var snapshot = Current; // 通过带锁的 getter 获取引用
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // 修复 M9：先写临时文件再替换，避免写入中断留下损坏的 settings.json
            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(SettingsPath))
                File.Replace(tempPath, SettingsPath, null);
            else
                File.Move(tempPath, SettingsPath);
        }
        catch (Exception ex)
        {
            Logger.Error("保存设置失败", ex);
            // 修复 L1：保存失败时清理可能残留的临时文件
            try { var tmp = SettingsPath + ".tmp"; if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>
    /// 重置为默认设置
    /// </summary>
    public static void Reset()
    {
        lock (_sync) { _current = CreateDefault(); }
        Save();
    }

    private static Settings CreateDefault()
    {
        return new Settings
        {
            MicrophoneHotkey = new HotkeyConfig
            {
                Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
                Key = System.Windows.Forms.Keys.F9
            },
            SpeakerHotkey = new HotkeyConfig
            {
                Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
                Key = System.Windows.Forms.Keys.F10
            }
        };
    }
}

/// <summary>
/// 应用设置数据结构
/// </summary>
public class Settings
{
    public HotkeyConfig MicrophoneHotkey { get; set; } = new();
    public HotkeyConfig SpeakerHotkey { get; set; } = new();
    // AutoStartEnabled 已移除：自启动状态以注册表为准，避免双源不一致
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.Ask;
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Smart;

    // 窗口位置记忆（null 表示从未保存过，首次使用默认位置）
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; } = false;
}

/// <summary>
/// 设备显示范围
/// </summary>
public enum DisplayMode
{
    All = 0,           // 显示全部
    Smart = 1,         // 智能模式（默认）：隐藏系统杂项（混音回环/显卡 HDMI/兜底端点），保留真实设备与用户虚拟声卡
    UsbBluetoothOnly = 2  // 只看 USB + 蓝牙
}

/// <summary>
/// 点关闭按钮时的行为
/// </summary>
public enum CloseBehavior
{
    Ask = 0,   // 每次询问
    Tray = 1,  // 最小化到托盘
    Exit = 2   // 完全退出
}

/// <summary>
/// 主题模式
/// </summary>
public enum ThemeMode
{
    Light = 0,  // 浅色
    Dark = 1,   // 深色
    System = 2  // 跟随系统
}

/// <summary>
/// 快捷键配置
/// </summary>
public class HotkeyConfig
{
    public HotkeyModifiers Modifiers { get; set; } = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    public System.Windows.Forms.Keys Key { get; set; } = System.Windows.Forms.Keys.None;
}

/// <summary>
/// 快捷键修饰键枚举（对应Win32 API的值）
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8
}
