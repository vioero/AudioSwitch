using AudioSwitch.Core;
using AudioSwitch.Models;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace AudioSwitch.Services;

/// <summary>
/// 系统托盘服务：管理托盘图标、右键菜单（WPF 弹出面板）、设备切换。
/// 右键菜单为 <see cref="TrayMenuWindow"/>——与主界面同一套设计令牌；
/// WinForms 原生 ContextMenuStrip 无法呈现新 UI 的视觉语言，已弃用。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly DeviceService _deviceService;
    private WinForms.NotifyIcon? _trayIcon;
    private TrayMenuWindow? _menu;
    private Action? _onShowWindow;
    private Action? _onExit;

    /// <summary>
    /// 默认设备已变更时触发（供主窗口刷新高亮）。修复 M5。
    /// </summary>
    public event Action? DefaultDeviceChanged;

    public TrayService(DeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    public void Setup(Action onShowWindow, Action onExit)
    {
        _onShowWindow = onShowWindow;
        _onExit = onExit;

        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "AudioSwitch - 音频设备切换",
            Visible = true
        };
        _trayIcon.Icon = CreateTrayIcon();
        _trayIcon.DoubleClick += (_, _) => _onShowWindow?.Invoke();

        // 右键弹出 WPF 托盘菜单；左键双击仍是打开主窗口（行为不变）
        _trayIcon.MouseClick += TrayIcon_MouseClick;

        // 启动时先同步加载一次，避免第一次右键是空菜单
        _deviceService.RefreshNow();
    }

    private void TrayIcon_MouseClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button != WinForms.MouseButtons.Right) return;
        ShowMenu();
    }

    /// <summary>
    /// 打开托盘菜单：先同步刷新设备，再弹出 WPF 面板。
    /// 菜单已打开时再次右键 = 收起（与原生菜单的切换行为一致）。
    /// </summary>
    private void ShowMenu()
    {
        try
        {
            if (_menu is { IsVisible: true })
            {
                _menu.Close();
                _menu = null;
                return;
            }

            _deviceService.RefreshNow();

            if (_menu == null)
            {
                _menu = new TrayMenuWindow(_deviceService);
                _menu.DeviceSelected += id => TrySetDefault(id);
                _menu.OpenWindowRequested += () =>
                {
                    CloseMenu();
                    _onShowWindow?.Invoke();
                };
                _menu.ExitRequested += () =>
                {
                    CloseMenu();
                    _onExit?.Invoke();
                };
                _menu.Closed += (_, _) => _menu = null;
            }

            _menu.ShowAtTrayIcon();
        }
        catch (Exception ex)
        {
            Logger.Error("打开托盘菜单失败", ex);
        }
    }

    private void CloseMenu()
    {
        try { _menu?.Close(); } catch { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// 加载应用图标作为托盘图标（与窗口/任务栏一致）；失败时退回简单圆形图标。
    /// </summary>
    private static Drawing.Icon CreateTrayIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                var extracted = Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (extracted != null)
                    return (Drawing.Icon)extracted.Clone();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"从 exe 提取图标失败: {ex.Message}");
        }

        using var bitmap = new Drawing.Bitmap(32, 32);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Drawing.Color.Transparent);
        using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(0, 103, 192));
        graphics.FillEllipse(brush, 1, 1, 30, 30);

        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            using var temp = Drawing.Icon.FromHandle(hIcon);
            return (Drawing.Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    /// <summary>
    /// 切换默认设备（修复 M5：成功后刷新设备状态，失败时气泡提示）。
    /// </summary>
    private void TrySetDefault(string deviceId)
    {
        try
        {
            DeviceSwitcher.SetDefault(deviceId);
            _deviceService.Refresh();            // 立即重读默认设备
            DefaultDeviceChanged?.Invoke();      // 通知主窗口刷新高亮
        }
        catch (Exception ex)
        {
            ShowBalloon("切换失败", $"无法把该设备设为默认设备：{ex.Message}");
        }
    }

    /// <summary>
    /// 显示托盘气泡提示（修复 M3/M4/M5 共用的用户反馈通道）。
    /// </summary>
    public void ShowBalloon(string title, string text)
    {
        try { _trayIcon?.ShowBalloonTip(3000, title, text, WinForms.ToolTipIcon.Warning); } catch { }
    }

    public void Dispose()
    {
        // 修复 L4：释放 Icon 句柄，再释放菜单和托盘图标
        if (_trayIcon != null)
        {
            _trayIcon.Icon?.Dispose();
            _trayIcon.Icon = null;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        try { _menu?.Close(); } catch { }
        _menu = null;
    }
}
