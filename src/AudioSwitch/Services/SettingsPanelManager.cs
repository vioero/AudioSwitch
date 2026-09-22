using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AudioSwitch.Services;

namespace AudioSwitch.Services;

/// <summary>
/// 设置面板管理器：负责设置抽屉的打开/关闭动画、保存/重置逻辑、快捷键录制状态。
/// </summary>
public sealed class SettingsPanelManager
{
    private const double DrawerWidth = 380;  // 5.1：抽屉宽度统一为常量
    private readonly FrameworkElement _settingsPanel;
    private readonly FrameworkElement _overlay;
    private readonly Action _onApplyTheme;
    private readonly Func<bool> _onRefreshHotkeys;
    private readonly Action _onRefreshDevices;
    private bool _isPanelOpen = false;
    private bool _isRecordingMic = false;
    private bool _isRecordingSpeaker = false;
    private HotkeyConfig _micHotkey = new();
    private HotkeyConfig _speakerHotkey = new();

    // 快捷键录制状态变化时触发（供外部更新 UI 显示）
    public event Action? RecordingStateChanged;
    /// <summary>
    /// 快捷键录制校验失败时触发（参数为面向用户的提示文案）。
    /// </summary>
    public event Action<string>? ValidationFailed;
    // 快捷键格式化显示（供外部 UI 更新）
    public HotkeyConfig MicHotkey => _micHotkey;
    public HotkeyConfig SpeakerHotkey => _speakerHotkey;
    public bool IsRecordingMic => _isRecordingMic;
    public bool IsRecordingSpeaker => _isRecordingSpeaker;

    public SettingsPanelManager(
        FrameworkElement settingsPanel,
        FrameworkElement overlay,
        Action onApplyTheme,
        Func<bool> onRefreshHotkeys,
        Action onRefreshDevices)
    {
        _settingsPanel = settingsPanel;
        _overlay = overlay;
        _onApplyTheme = onApplyTheme;
        _onRefreshHotkeys = onRefreshHotkeys;
        _onRefreshDevices = onRefreshDevices;
    }

    /// <summary>
    /// 初始化设置面板：从设置文件加载当前配置到 UI 控件。
    /// 需要外部传入 UI 控件的引用。
    /// </summary>
    public void Initialize(
        System.Windows.Controls.RadioButton closeAskRadio,
        System.Windows.Controls.RadioButton closeTrayRadio,
        System.Windows.Controls.RadioButton closeExitRadio,
        System.Windows.Controls.RadioButton themeSystemRadio,
        System.Windows.Controls.RadioButton themeLightRadio,
        System.Windows.Controls.RadioButton themeDarkRadio,
        System.Windows.Controls.RadioButton displaySmartRadio,
        System.Windows.Controls.RadioButton displayAllRadio,
        System.Windows.Controls.RadioButton displayUsbBtRadio)
    {
        var settings = SettingsManager.Current;
        _micHotkey = new HotkeyConfig { Modifiers = settings.MicrophoneHotkey.Modifiers, Key = settings.MicrophoneHotkey.Key };
        _speakerHotkey = new HotkeyConfig { Modifiers = settings.SpeakerHotkey.Modifiers, Key = settings.SpeakerHotkey.Key };

        switch (settings.CloseBehavior)
        {
            case CloseBehavior.Tray: closeTrayRadio.IsChecked = true; break;
            case CloseBehavior.Exit: closeExitRadio.IsChecked = true; break;
            default: closeAskRadio.IsChecked = true; break;
        }
        switch (settings.Theme)
        {
            case ThemeMode.Light: themeLightRadio.IsChecked = true; break;
            case ThemeMode.Dark: themeDarkRadio.IsChecked = true; break;
            default: themeSystemRadio.IsChecked = true; break;
        }
        switch (settings.DisplayMode)
        {
            case DisplayMode.All: displayAllRadio.IsChecked = true; break;
            case DisplayMode.UsbBluetoothOnly: displayUsbBtRadio.IsChecked = true; break;
            default: displaySmartRadio.IsChecked = true; break;
        }
    }

    /// <summary>
    /// 开始录制快捷键。
    /// </summary>
    public void StartRecording(bool isMic)
    {
        _isRecordingMic = isMic;
        _isRecordingSpeaker = !isMic;
        RecordingStateChanged?.Invoke();
    }

    /// <summary>
    /// 从设置文件重新加载快捷键到临时副本（修复 M8：避免面板显示未保存的录制值）。
    /// </summary>
    private void ReloadHotkeysFromSettings()
    {
        var s = SettingsManager.Current;
        _micHotkey = new HotkeyConfig { Modifiers = s.MicrophoneHotkey.Modifiers, Key = s.MicrophoneHotkey.Key };
        _speakerHotkey = new HotkeyConfig { Modifiers = s.SpeakerHotkey.Modifiers, Key = s.SpeakerHotkey.Key };
    }

    /// <summary>
    /// 取消当前录制，保留原快捷键值。
    /// </summary>
    public void CancelRecording()
    {
        if (!_isRecordingMic && !_isRecordingSpeaker) return;
        _isRecordingMic = false;
        _isRecordingSpeaker = false;
        RecordingStateChanged?.Invoke();
    }

    /// <summary>
    /// 处理键盘按下事件，录制快捷键。
    /// 修复 M4：Esc 取消、必须带修饰键、不允许两个快捷键相同。
    /// </summary>
    public void HandleKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (!_isRecordingMic && !_isRecordingSpeaker) return;

        // Esc 取消录制，保留原值
        if (e.Key == Key.Escape)
        {
            CancelRecording();
            e.Handled = true;
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;

        var key = e.Key;
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin) return;

        // 必须带至少一个修饰键
        if (modifiers == HotkeyModifiers.None)
        {
            ValidationFailed?.Invoke("快捷键必须包含 Ctrl / Alt / Shift / Win 中的至少一个修饰键");
            return;
        }

        var formsKey = (System.Windows.Forms.Keys)KeyInterop.VirtualKeyFromKey(key);

        // 两个快捷键不允许相同
        var other = _isRecordingMic ? _speakerHotkey : _micHotkey;
        if (other.Key == formsKey && other.Modifiers == modifiers)
        {
            ValidationFailed?.Invoke("麦克风与音箱的快捷键不能相同");
            return;
        }

        if (_isRecordingMic)
        {
            _micHotkey.Modifiers = modifiers; _micHotkey.Key = formsKey;
            _isRecordingMic = false;
        }
        else
        {
            _speakerHotkey.Modifiers = modifiers; _speakerHotkey.Key = formsKey;
            _isRecordingSpeaker = false;
        }
        e.Handled = true;
        RecordingStateChanged?.Invoke();
    }

    /// <summary>
    /// 格式化快捷键显示文本。
    /// </summary>
    public static string FormatHotkey(HotkeyConfig config)
    {
        var parts = new List<string>();
        if (config.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (config.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (config.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (config.Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        if (config.Key != System.Windows.Forms.Keys.None) parts.Add(config.Key.ToString());
        return string.Join(" + ", parts);
    }

    /// <summary>
    /// 从 UI 控件读取当前选择，保存到设置文件。
    /// </summary>
    public void Save(
        System.Windows.Controls.RadioButton closeTrayRadio,
        System.Windows.Controls.RadioButton closeExitRadio,
        System.Windows.Controls.RadioButton themeLightRadio,
        System.Windows.Controls.RadioButton themeDarkRadio,
        System.Windows.Controls.RadioButton displayAllRadio,
        System.Windows.Controls.RadioButton displayUsbBtRadio)
    {
        // 修复 M4：兜底校验，避免无效组合被写入设置
        if (_micHotkey.Modifiers == HotkeyModifiers.None || _micHotkey.Key == System.Windows.Forms.Keys.None)
        {
            ValidationFailed?.Invoke("麦克风快捷键无效，请重新录制");
            return;
        }
        if (_speakerHotkey.Modifiers == HotkeyModifiers.None || _speakerHotkey.Key == System.Windows.Forms.Keys.None)
        {
            ValidationFailed?.Invoke("音箱快捷键无效，请重新录制");
            return;
        }
        if (_micHotkey.Modifiers == _speakerHotkey.Modifiers && _micHotkey.Key == _speakerHotkey.Key)
        {
            ValidationFailed?.Invoke("麦克风与音箱的快捷键不能相同");
            return;
        }

        var s = SettingsManager.Current;
        var oldDisplayMode = s.DisplayMode;
        var oldMic = new HotkeyConfig { Modifiers = s.MicrophoneHotkey.Modifiers, Key = s.MicrophoneHotkey.Key };
        var oldSpk = new HotkeyConfig { Modifiers = s.SpeakerHotkey.Modifiers, Key = s.SpeakerHotkey.Key };

        s.MicrophoneHotkey = new HotkeyConfig { Modifiers = _micHotkey.Modifiers, Key = _micHotkey.Key };
        s.SpeakerHotkey = new HotkeyConfig { Modifiers = _speakerHotkey.Modifiers, Key = _speakerHotkey.Key };
        if (closeTrayRadio.IsChecked == true) s.CloseBehavior = CloseBehavior.Tray;
        else if (closeExitRadio.IsChecked == true) s.CloseBehavior = CloseBehavior.Exit;
        else s.CloseBehavior = CloseBehavior.Ask;
        if (themeLightRadio.IsChecked == true) s.Theme = ThemeMode.Light;
        else if (themeDarkRadio.IsChecked == true) s.Theme = ThemeMode.Dark;
        else s.Theme = ThemeMode.System;
        if (displayAllRadio.IsChecked == true) s.DisplayMode = DisplayMode.All;
        else if (displayUsbBtRadio.IsChecked == true) s.DisplayMode = DisplayMode.UsbBluetoothOnly;
        else s.DisplayMode = DisplayMode.Smart;

        // 先注册快捷键；任一失败则回滚并写盘，避免 settings.json 与系统实际注册不一致
        if (!_onRefreshHotkeys())
        {
            s.MicrophoneHotkey = oldMic;
            s.SpeakerHotkey = oldSpk;
            _micHotkey = new HotkeyConfig { Modifiers = oldMic.Modifiers, Key = oldMic.Key };
            _speakerHotkey = new HotkeyConfig { Modifiers = oldSpk.Modifiers, Key = oldSpk.Key };
            _onRefreshHotkeys(); // 尽力恢复原组合
            ValidationFailed?.Invoke("快捷键注册失败（可能被其他程序占用），设置未保存");
            RecordingStateChanged?.Invoke();
            return;
        }

        SettingsManager.Save();
        ClosePanel();
        _onApplyTheme();
        if (oldDisplayMode != s.DisplayMode)
            _onRefreshDevices();
    }

    /// <summary>
    /// 恢复默认设置并更新 UI。
    /// </summary>
    public void Reset(
        System.Windows.Controls.RadioButton closeAskRadio,
        System.Windows.Controls.RadioButton themeSystemRadio,
        System.Windows.Controls.RadioButton displaySmartRadio)
    {
        SettingsManager.Reset();
        var defaults = SettingsManager.Current;
        _micHotkey = new HotkeyConfig { Modifiers = defaults.MicrophoneHotkey.Modifiers, Key = defaults.MicrophoneHotkey.Key };
        _speakerHotkey = new HotkeyConfig { Modifiers = defaults.SpeakerHotkey.Modifiers, Key = defaults.SpeakerHotkey.Key };
        _isRecordingMic = false; _isRecordingSpeaker = false;
        closeAskRadio.IsChecked = true;
        themeSystemRadio.IsChecked = true;
        displaySmartRadio.IsChecked = true;
        _onApplyTheme();
        _onRefreshHotkeys();   // 默认快捷键立即重新注册（Reset 已写盘）
        _onRefreshDevices();
    }

    public void OpenPanel()
    {
        if (_isPanelOpen) return;
        _isPanelOpen = true;
        _isRecordingMic = false; _isRecordingSpeaker = false;
        ReloadHotkeysFromSettings();          // 修复 M8：避免面板显示未保存的录制值
        RecordingStateChanged?.Invoke();
        _overlay.Visibility = Visibility.Visible;
        _settingsPanel.Visibility = Visibility.Visible;
        var transform = (TranslateTransform)_settingsPanel.RenderTransform;
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        transform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    public void ClosePanel()
    {
        if (!_isPanelOpen) return;
        _isPanelOpen = false;
        var transform = (TranslateTransform)_settingsPanel.RenderTransform;
        var anim = new DoubleAnimation(DrawerWidth, TimeSpan.FromMilliseconds(200))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        anim.Completed += (_, _) =>
        {
            _settingsPanel.Visibility = Visibility.Collapsed;
            _overlay.Visibility = Visibility.Collapsed;
        };
        transform.BeginAnimation(TranslateTransform.XProperty, anim);
    }
}
