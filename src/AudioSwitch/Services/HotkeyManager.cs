using AudioSwitch.Services;

namespace AudioSwitch;

/// <summary>
/// 快捷键管理器：封装全局快捷键的注册、刷新、循环切换逻辑。
/// 从 MainWindow 中独立出来，让 MainWindow 只管"调用"不管"实现"。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly DeviceService _deviceService;
    private HotkeyService? _hotkeys;
    private int _micHotkeyId = -1;
    private int _speakerHotkeyId = -1;

    /// <summary>
    /// 快捷键注册/更新失败时触发（参数为面向用户的提示文案）。
    /// </summary>
    public event Action<string>? HotkeyFailed;

    public HotkeyManager(DeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    /// <summary>
    /// 注册全局快捷键（在窗口句柄就绪后调用）。
    /// 修复 M3：麦克风与音箱独立注册，任一失败不影响另一个。
    /// </summary>
    public void Register(IntPtr windowHandle)
    {
        _hotkeys = new HotkeyService(windowHandle);
        var settings = SettingsManager.Current;

        try
        {
            _micHotkeyId = _hotkeys.Register(
                (uint)settings.MicrophoneHotkey.Modifiers,
                (uint)settings.MicrophoneHotkey.Key,
                CycleMicrophone);
        }
        catch (Exception ex)
        {
            _micHotkeyId = -1;
            HotkeyFailed?.Invoke($"麦克风快捷键注册失败：{ex.Message}");
        }

        try
        {
            _speakerHotkeyId = _hotkeys.Register(
                (uint)settings.SpeakerHotkey.Modifiers,
                (uint)settings.SpeakerHotkey.Key,
                CycleSpeaker);
        }
        catch (Exception ex)
        {
            _speakerHotkeyId = -1;
            HotkeyFailed?.Invoke($"音箱快捷键注册失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 刷新快捷键（设置保存后调用）。修复 M3：两个快捷键独立更新。
    /// 返回 true 表示两个快捷键都已生效；false 表示至少一个失败（调用方不应写盘）。
    /// </summary>
    public bool Refresh()
    {
        if (_hotkeys == null) return false;
        var settings = SettingsManager.Current;
        bool micOk = ApplyOne(ref _micHotkeyId, settings.MicrophoneHotkey, CycleMicrophone, "麦克风");
        bool spkOk = ApplyOne(ref _speakerHotkeyId, settings.SpeakerHotkey, CycleSpeaker, "音箱");
        return micOk && spkOk;
    }

    /// <summary>
    /// 注册或更新单个快捷键；失败时尽量保持/恢复原组合。
    /// </summary>
    private bool ApplyOne(ref int id, HotkeyConfig config, Action action, string label)
    {
        if (_hotkeys == null) return false;
        try
        {
            // id < 0 表示初始注册失败，尝试重新注册
            if (id >= 0)
            {
                _hotkeys.UpdateHotkey(id, (uint)config.Modifiers, (uint)config.Key, action);
            }
            else
            {
                id = _hotkeys.Register((uint)config.Modifiers, (uint)config.Key, action);
            }
            return true;
        }
        catch (Exception ex)
        {
            HotkeyFailed?.Invoke($"{label}快捷键更新失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 循环切换麦克风（回调，由 HotkeyService 触发）。
    /// </summary>
    private void CycleMicrophone()
    {
        var mics = _deviceService.Microphones;
        CycleRequested?.Invoke(this, new CycleEventArgs { DeviceType = DeviceType.Microphone, DeviceCount = mics.Count });
    }

    /// <summary>
    /// 循环切换音箱（回调，由 HotkeyService 触发）。
    /// </summary>
    private void CycleSpeaker()
    {
        var speakers = _deviceService.Speakers;
        CycleRequested?.Invoke(this, new CycleEventArgs { DeviceType = DeviceType.Speaker, DeviceCount = speakers.Count });
    }

    /// <summary>
    /// 快捷键触发循环切换时通知外部（由 MainWindow 响应并更新 UI）。
    /// </summary>
    public event EventHandler<CycleEventArgs>? CycleRequested;

    public void Dispose()
    {
        _hotkeys?.Dispose();
    }
}

public enum DeviceType { Microphone, Speaker }

public class CycleEventArgs : EventArgs
{
    public DeviceType DeviceType { get; init; }
    public int DeviceCount { get; init; }
}
