using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AudioSwitch.Core;
using AudioSwitch.Models;
using AudioSwitch.Services;

namespace AudioSwitch;

/// <summary>
/// 主窗口：组装各个服务模块，处理界面事件。
/// 设备管理、主题切换、托盘管理、设置面板等逻辑均委托给独立的 Service 类。
/// </summary>
public partial class MainWindow : Window
{
    private bool _loading = true;
    private readonly DeviceService _deviceService = new();
    private TrayService? _trayService;
    private HotkeyManager? _hotkeyManager;
    private SettingsPanelManager? _settingsPanel;
    private volatile bool _isExiting = false;
    private System.Threading.EventWaitHandle? _wakeUpEvent;

    public MainWindow()
    {
        InitializeComponent();
        SetupDeviceService();
        SetupTrayIcon();
        SetupHotkeys();
        SetupAutoStart();
        SetupSettingsPanel();
        SetupWakeUpListener();
        ThemeService.Apply(this);
        _loading = false;
    }

    // ==================== 设备管理 ====================

    private void SetupDeviceService()
    {
        _deviceService.DevicesChanged += () => Dispatcher.Invoke(RefreshDeviceUI);
        _deviceService.Refresh();
        _deviceService.StartAutoRefresh();   // 修复 M2：启用热插拔轮询（每 5 秒）
    }

    private void RefreshDeviceUI()
    {
        _loading = true;
        MicrophoneList.Items.Clear();
        SpeakerList.Items.Clear();
        foreach (var m in _deviceService.Microphones) MicrophoneList.Items.Add(m);
        foreach (var s in _deviceService.Speakers) SpeakerList.Items.Add(s);
        HighlightById(_deviceService.Microphones, MicrophoneList, _deviceService.DefaultMicId);
        HighlightById(_deviceService.Speakers, SpeakerList, _deviceService.DefaultSpeakerId);
        _loading = false;
    }

    private static void HighlightById(List<AudioDevice> devices, System.Windows.Controls.ListBox list, string? id)
    {
        if (id == null) return;
        int index = devices.FindIndex(d => d.Id == id);
        if (index >= 0) list.SelectedIndex = index;
    }

    // ==================== 开机自启动 ====================

    private void SetupAutoStart()
    {
        AutoStartCheckBox.IsChecked = AutoStartService.IsEnabled();
    }

    private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AutoStartService.SetEnabled(AutoStartCheckBox.IsChecked == true);
    }

    // ==================== 全局快捷键 ====================

    private void SetupHotkeys()
    {
        _hotkeyManager = new HotkeyManager(_deviceService);
        _hotkeyManager.HotkeyFailed += msg => Dispatcher.Invoke(() => _trayService?.ShowBalloon("快捷键不可用", msg));
        _hotkeyManager.CycleRequested += (_, args) =>
        {
            // 修复：直接以 UI 列表的实际项数为准，避免跨线程 Count 不一致
            var list = args.DeviceType == DeviceType.Microphone ? MicrophoneList : SpeakerList;
            if (list.Items.Count <= 1) return;
            list.SelectedIndex = (list.SelectedIndex + 1) % list.Items.Count;
        };

        Loaded += (_, _) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            _hotkeyManager.Register(handle);
        };
    }

    // ==================== 设置面板 ====================

    private void SetupSettingsPanel()
    {
        _settingsPanel = new SettingsPanelManager(
            settingsPanel: SettingsPanel,
            overlay: Overlay,
            onApplyTheme: () => ThemeService.Apply(this),
            onRefreshHotkeys: () => _hotkeyManager?.Refresh() == true,
            onRefreshDevices: () => { _deviceService.ClearCache(); _deviceService.Refresh(); }  // UI 由 DevicesChanged 事件更新
        );

        _settingsPanel.Initialize(
            CloseAskRadio, CloseTrayRadio, CloseExitRadio,
            ThemeSystemRadio, ThemeLightRadio, ThemeDarkRadio,
            DisplaySmartRadio, DisplayAllRadio, DisplayUsbBtRadio
        );

        // 修复 M4：快捷键校验失败时提示
        _settingsPanel.ValidationFailed += msg =>
        {
            Dispatcher.Invoke(() =>
            {
                MicHotkeyBox.Text = msg;
                SpeakerHotkeyBox.Text = msg;
            });
        };

        _settingsPanel.RecordingStateChanged += () =>
        {
            MicHotkeyBtn.Content = _settingsPanel.IsRecordingMic ? "按下快捷键..." : "修改";
            SpeakerHotkeyBtn.Content = _settingsPanel.IsRecordingSpeaker ? "按下快捷键..." : "修改";
            if (!_settingsPanel.IsRecordingMic && !_settingsPanel.IsRecordingSpeaker)
            {
                MicHotkeyBox.Text = SettingsPanelManager.FormatHotkey(_settingsPanel.MicHotkey);
                SpeakerHotkeyBox.Text = SettingsPanelManager.FormatHotkey(_settingsPanel.SpeakerHotkey);
            }
        };

        // 快捷键录制按钮
        MicHotkeyBtn.Click += (_, _) =>
        {
            _settingsPanel.StartRecording(isMic: true);
            MicHotkeyBtn.Content = "按下快捷键...";
            MicHotkeyBox.Text = "请按组合键...";
        };
        SpeakerHotkeyBtn.Click += (_, _) =>
        {
            _settingsPanel.StartRecording(isMic: false);
            SpeakerHotkeyBtn.Content = "按下快捷键...";
            SpeakerHotkeyBox.Text = "请按组合键...";
        };

        // 恢复默认按钮
        ResetBtn.Click += (_, _) =>
        {
            try
            {
                _settingsPanel.Reset(CloseAskRadio, ThemeSystemRadio, DisplaySmartRadio);

                // 修复 M8：重置会把 AutoStartEnabled 复位为 false，同步注册表与复选框
                AutoStartService.SetEnabled(false);
                _loading = true;
                AutoStartCheckBox.IsChecked = false;
                _loading = false;

                MicHotkeyBtn.Content = "修改";
                SpeakerHotkeyBtn.Content = "修改";
                MicHotkeyBox.Text = SettingsPanelManager.FormatHotkey(_settingsPanel.MicHotkey);
                SpeakerHotkeyBox.Text = SettingsPanelManager.FormatHotkey(_settingsPanel.SpeakerHotkey);
                RefreshDeviceUI();
            }
            catch (Exception ex)
            {
                Logger.Error("恢复默认失败", ex);
            }
        };

        // 保存按钮
        SaveBtn.Click += (_, _) =>
        {
            try
            {
                _settingsPanel.Save(
                    CloseTrayRadio, CloseExitRadio,
                    ThemeLightRadio, ThemeDarkRadio,
                    DisplayAllRadio, DisplayUsbBtRadio);
            }
            catch (Exception ex)
            {
                Logger.Error("保存设置失败", ex);
            }
        };

        // 快捷键录制时的键盘监听
        PreviewKeyDown += (sender, e) => _settingsPanel.HandleKeyDown(e);
    }

    // ==================== 窗口标题栏按钮 ====================

    private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaxBtn_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    protected override void OnStateChanged(EventArgs e)
    {
        // 5.4: 最大化时切换为"还原"图标
        MaxBtn.Content = WindowState == WindowState.Maximized ? "" : "";
        base.OnStateChanged(e);
    }
    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    // ==================== 设置面板 ====================

    private void SettingsBtn_Click(object sender, RoutedEventArgs e) => _settingsPanel?.OpenPanel();
    private void CloseSettingsBtn_Click(object sender, RoutedEventArgs e) => _settingsPanel?.ClosePanel();
    private void Overlay_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => _settingsPanel?.ClosePanel();

    // ==================== 系统托盘 ====================

    private void SetupTrayIcon()
    {
        _trayService = new TrayService(_deviceService);
        _trayService.Setup(
            onShowWindow: ShowMainWindow,
            onExit: ExitApplication
        );
        _trayService.DefaultDeviceChanged += () => Dispatcher.Invoke(RefreshDeviceUI);  // 修复 M5
    }

    // ==================== 单实例唤醒 ====================

    private void SetupWakeUpListener()
    {
        try { _wakeUpEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, @"Local\AudioSwitch_WakeUp_Event"); }
        catch { try { _wakeUpEvent = System.Threading.EventWaitHandle.OpenExisting(@"Local\AudioSwitch_WakeUp_Event"); } catch { return; } }
        var thread = new System.Threading.Thread(() =>
        {
            while (!_isExiting)
            {
                try { if (_wakeUpEvent.WaitOne(500)) Dispatcher.Invoke(ShowMainWindow); }
                catch { break; }
            }
        }) { IsBackground = true };
        thread.Start();
    }

    private void ShowMainWindow()
    {
        try
        {
            RestoreWindowPosition();
            Show(); WindowState = WindowState.Normal; Activate();
            Topmost = true; Topmost = false;
            _deviceService.Refresh();
        }
        catch (Exception ex)
        {
            Logger.Error("显示主窗口失败", ex);
        }
    }

    /// <summary>
    /// 保存当前窗口位置和大小到设置。
    /// </summary>
    private void SaveWindowPosition()
    {
        try
        {
            var s = SettingsManager.Current;
            s.WindowMaximized = WindowState == WindowState.Maximized;
            var bounds = WindowState == WindowState.Normal ? new System.Windows.Rect(Left, Top, Width, Height) : RestoreBounds;
            s.WindowLeft = bounds.Left;
            s.WindowTop = bounds.Top;
            s.WindowWidth = bounds.Width;
            s.WindowHeight = bounds.Height;
            SettingsManager.Save();
        }
        catch (Exception ex)
        {
            Logger.Error("保存窗口位置失败", ex);
        }
    }

    /// <summary>
    /// 从设置恢复窗口位置和大小。
    /// 首次使用（从未保存过）不改位置，保留 XAML 的 CenterScreen。
    /// 多屏校验：副屏拔除时回退主屏居中。
    /// </summary>
    private void RestoreWindowPosition()
    {
        var s = SettingsManager.Current;

        // 从未保存过位置：维持 WindowStartupLocation=CenterScreen
        if (s.WindowLeft is null || s.WindowTop is null || s.WindowWidth is null || s.WindowHeight is null)
        {
            if (s.WindowMaximized) WindowState = WindowState.Maximized;
            return;
        }

        double left   = s.WindowLeft.Value;
        double top    = s.WindowTop.Value;
        double width  = s.WindowWidth.Value;
        double height = s.WindowHeight.Value;

        width  = Math.Max(MinWidth, width);
        height = Math.Max(MinHeight, height);

        var target = new System.Windows.Rect(left, top, width, height);

        // 校验窗口是否仍与任一屏幕工作区相交
        bool visible = System.Windows.Forms.Screen.AllScreens.Any(sc =>
        {
            var wa = sc.WorkingArea;
            return new System.Windows.Rect(wa.Left, wa.Top, wa.Width, wa.Height).IntersectsWith(target);
        });

        if (visible)
        {
            Left = left; Top = top; Width = width; Height = height;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            Width = width; Height = height;
            Left = wa.Left + (wa.Width - width) / 2;
            Top  = wa.Top  + (wa.Height - height) / 2;
        }

        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    // ==================== 窗口关闭行为 ====================

    /// <summary>
    /// 统一释放服务并结束应用（退出路径只走这里）。
    /// </summary>
    private void ShutdownCore()
    {
        _isExiting = true;
        _hotkeyManager?.Dispose();
        _trayService?.Dispose();
        _deviceService.Dispose();
    }

    private void ExitApplication()
    {
        ShutdownCore();
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveWindowPosition();
        if (_isExiting) { base.OnClosing(e); return; }
        switch (SettingsManager.Current.CloseBehavior)
        {
            case CloseBehavior.Exit:
                e.Cancel = false;
                ShutdownCore();
                base.OnClosing(e); return;
            case CloseBehavior.Tray:
                e.Cancel = true; Hide(); base.OnClosing(e); return;
            case CloseBehavior.Ask: default:
                e.Cancel = true; AskOnClose(); base.OnClosing(e); return;
        }
    }

    private void AskOnClose()
    {
        try
        {
            var dialog = new ClosePromptWindow { Owner = this };
            dialog.ShowDialog();
            if (dialog.ExitChosen == null) return;
            if (dialog.RememberChoice)
            {
                SettingsManager.Current.CloseBehavior = dialog.ExitChosen == true ? CloseBehavior.Exit : CloseBehavior.Tray;
                SettingsManager.Save();
            }
            if (dialog.ExitChosen == true)
            {
                ShutdownCore();
                System.Windows.Application.Current.Shutdown();
            }
            else { Hide(); }
        }
        catch (Exception ex)
        {
            Logger.Error("关闭询问失败", ex);
        }
    }

    // ==================== 设备列表点击切换 ====================

    private void MicrophoneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (MicrophoneList.SelectedItem is AudioDevice device && device.IsConnected)
            TrySetDefault(device.Id);
    }

    private void SpeakerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (SpeakerList.SelectedItem is AudioDevice device && device.IsConnected)
            TrySetDefault(device.Id);
    }

    private static void TrySetDefault(string deviceId)
    {
        try { DeviceSwitcher.SetDefault(deviceId); }
        catch (Exception ex) { Logger.Error("切换设备失败", ex); }
    }
}