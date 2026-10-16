using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AudioSwitch.Models;
using AudioSwitch.Services;
using DrawingRect = System.Drawing.Rectangle;
// UseWindowsForms + UseWPF 同时启用时，以下类型存在歧义，取 WPF 侧
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using Point = System.Windows.Point;

namespace AudioSwitch;

/// <summary>
/// 托盘右键菜单：WPF 弹出面板，与主界面共用设计令牌（圆角 / 投影 / 深浅色）。
/// 替代 WinForms ContextMenuStrip——原生菜单无法呈现新 UI 的视觉语言，
/// 且深浅色只能靠近似色硬凑。定位策略：右键点击时光标就在托盘图标上，
/// 按任务栏边缘（上/下/左/右）把面板贴到图标旁，并钳制在屏幕工作区内。
/// </summary>
public partial class TrayMenuWindow : Window
{
    private readonly DeviceService _deviceService;
    private DateTime _shownAtUtc = DateTime.MinValue;

    /// <summary>用户在菜单中选择了某台设备（调用方负责设为默认；已保证在线）。</summary>
    public event Action<string>? DeviceSelected;

    /// <summary>点击「打开主窗口」。</summary>
    public event Action? OpenWindowRequested;

    /// <summary>点击「退出」。</summary>
    public event Action? ExitRequested;

    public TrayMenuWindow(DeviceService deviceService)
    {
        InitializeComponent();
        _deviceService = deviceService;
    }

    // ==================== 显示与定位 ====================

    /// <summary>
    /// 刷新内容并显示在托盘图标旁。必须在 UI 线程调用。
    /// </summary>
    public void ShowAtTrayIcon()
    {
        // 先记时间：激活过程中的瞬态失活不应把菜单关掉
        _shownAtUtc = DateTime.UtcNow;

        try
        {
            BuildContent();

            // 先在屏幕外完成布局测量，避免定位前的闪烁
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -4000;
            Top = -4000;
            ShowActivated = false;
            Show();
            UpdateLayout();

            PositionNearTrayIcon();
            ForceForeground();
            PlayOpenAnimation();
        }
        catch (Exception ex)
        {
            Logger.Error("显示托盘菜单失败", ex);
            try { Close(); } catch { }
        }
    }

    /// <summary>按设备数据重建两个分区（每次打开前调用，保证读到最新状态）。</summary>
    private void BuildContent()
    {
        var mics = _deviceService.Microphones;
        var speakers = _deviceService.Speakers;

        Fill(MicList, mics, _deviceService.DefaultMicId, DeviceType.Microphone);
        Fill(SpeakerList, speakers, _deviceService.DefaultSpeakerId, DeviceType.Speaker);

        bool micEmpty = mics.Count == 0;
        MicList.Visibility = micEmpty ? Visibility.Collapsed : Visibility.Visible;
        MicEmpty.Visibility = micEmpty ? Visibility.Visible : Visibility.Collapsed;
        MicHeaderText.Text = micEmpty ? "麦克风 · 未检测到" : "麦克风";

        bool spkEmpty = speakers.Count == 0;
        SpeakerList.Visibility = spkEmpty ? Visibility.Collapsed : Visibility.Visible;
        SpeakerEmpty.Visibility = spkEmpty ? Visibility.Visible : Visibility.Collapsed;
        SpeakerHeaderText.Text = spkEmpty ? "音箱 · 未检测到" : "音箱";
    }

    private static void Fill(ListBox list, List<AudioDevice> devices, string? defaultId, DeviceType role)
    {
        var rows = devices
            .Select(d => new DeviceRowItem(d, d.Id == defaultId, role))
            .ToList();
        list.ItemsSource = rows;

        // 预选中当前默认设备：键盘用户按 ↓ 即从"当前设备"开始浏览
        int index = rows.FindIndex(r => r.IsDefault);
        list.SelectedIndex = index >= 0 ? index : -1;
    }

    // ---- 任务栏边缘定位 ----

    private const uint ABE_LEFT = 0;
    private const uint ABE_TOP = 1;
    private const uint ABE_RIGHT = 2;
    private const uint ABE_BOTTOM = 3;
    private const uint ABM_GETTASKBARPOS = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    /// <summary>
    /// 把面板摆到托盘图标旁：底部任务栏在上方、顶部在下方、左右任务栏在相反侧，
    /// 最后钳制在光标所在屏幕的工作区内，防止多屏 / 高分辨率下跑出可见区域。
    /// </summary>
    private void PositionNearTrayIcon()
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // 右键点击发生在图标上，此刻的光标位置就是图标位置（屏幕像素）
        var cursor = System.Windows.Forms.Cursor.Position;
        var (edge, taskbar) = GetTaskbar();

        double x;
        double y;
        switch (edge)
        {
            case ABE_TOP:
                x = cursor.X - w / 2;
                y = taskbar.Bottom + 6;
                break;
            case ABE_LEFT:
                x = taskbar.Right + 6;
                y = cursor.Y - h / 2;
                break;
            case ABE_RIGHT:
                x = taskbar.Left - w - 6;
                y = cursor.Y - h / 2;
                break;
            default: // ABE_BOTTOM
                x = cursor.X - w / 2;
                y = cursor.Y - h - 6;
                break;
        }

        var work = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;
        x = Math.Min(Math.Max(x, work.Left + 4), work.Right - w - 4);
        y = Math.Min(Math.Max(y, work.Top + 4), work.Bottom - h - 4);

        // 屏幕像素 → WPF 设备无关单位（DPI 缩放）
        var toDiU = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        var pos = toDiU.HasValue ? toDiU.Value.Transform(new Point(x, y)) : new Point(x, y);
        Left = pos.X;
        Top = pos.Y;
    }

    private static (uint edge, DrawingRect rect) GetTaskbar()
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref data) == IntPtr.Zero)
            return (ABE_BOTTOM, default);
        return (data.uEdge, new DrawingRect(data.rc.Left, data.rc.Top, data.rc.Right - data.rc.Left, data.rc.Bottom - data.rc.Top));
    }

    // ---- 激活（无边框窗口必须真正拿到前台焦点，"点外面关闭"与键盘导航才可用） ----

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>
    /// 用 AttachThreadInput 技巧把窗口可靠地带到前台。
    /// 直接 SetForegroundWindow 在进程非前台时会被 Windows 静默忽略。
    /// </summary>
    private void ForceForeground()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            Activate();
            return;
        }

        uint foreThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint myThread = GetCurrentThreadId();
        bool attached = false;
        if (foreThread != myThread)
            attached = AttachThreadInput(foreThread, myThread, true);

        SetForegroundWindow(hwnd);
        Activate();

        if (attached)
            AttachThreadInput(foreThread, myThread, false);
    }

    // ---- 入场动画 ----

    /// <summary>淡入 + 自下而上 6px 滑入；系统「减少动画」时瞬时显示。</summary>
    private void PlayOpenAnimation()
    {
        if (MotionService.IsReducedMotion) return;

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        FlyoutRoot.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });
        SlideTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(6d, 0d, TimeSpan.FromMilliseconds(110)) { EasingFunction = ease });
    }

    // ==================== 交互 ====================

    private void DeviceList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 点击行即选中（ListBox 默认行为），直接对选中项生效：
        // 点在已选中的默认行上也能关闭菜单，与原生菜单一致。
        if (sender is not ListBox list) return;
        if (list.SelectedItem is not DeviceRowItem row) return;
        Activate(row);
    }

    private void Activate(DeviceRowItem row)
    {
        if (!row.IsConnected) return; // 离线行已禁用，双保险
        DeviceSelected?.Invoke(row.Id);
        Close();
    }

    private void OpenWindowBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        OpenWindowRequested?.Invoke();
    }

    private void ExitBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
        ExitRequested?.Invoke();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        // Enter 只在列表聚焦时生效（按钮自己处理 Enter，避免抢焦点）
        if (e.Key == Key.Enter && (MicList.IsKeyboardFocusWithin || SpeakerList.IsKeyboardFocusWithin))
        {
            var row = MicList.IsKeyboardFocusWithin ? MicList.SelectedItem : SpeakerList.SelectedItem;
            if (row is DeviceRowItem item)
            {
                Activate(item);
                e.Handled = true;
            }
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // 激活过程可能有瞬态失活：开启动画期内忽略，避免菜单自己闪退
        if ((DateTime.UtcNow - _shownAtUtc).TotalMilliseconds < 400) return;
        Close();
    }
}
