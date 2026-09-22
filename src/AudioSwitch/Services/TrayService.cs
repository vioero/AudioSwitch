using AudioSwitch.Core;
using AudioSwitch.Models;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace AudioSwitch.Services;

/// <summary>
/// 系统托盘服务：管理托盘图标、右键菜单、设备切换。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly DeviceService _deviceService;
    private WinForms.NotifyIcon? _trayIcon;
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

        var menu = new WinForms.ContextMenuStrip();
        menu.Renderer = new ModernMenuRenderer();
        menu.Padding = new WinForms.Padding(8);
        menu.ShowImageMargin = false;

        // 弹出前同步刷新并重建。注意：不要在 Opening 里 Dispose 菜单项，
        // WinForms 正在测量/显示这些项，Dispose 会导致菜单空白。
        menu.Opening += (_, _) =>
        {
            _deviceService.RefreshNow();
            RebuildTrayMenu(menu);
        };

        // 启动时先同步加载一次，避免第一次右键是空菜单
        _deviceService.RefreshNow();
        RebuildTrayMenu(menu);

        _trayIcon.ContextMenuStrip = menu;
    }

    private void RebuildTrayMenu(WinForms.ContextMenuStrip menu)
    {
        try
        {
            var mics = _deviceService.Microphones;
            var speakers = _deviceService.Speakers;
            var defaultMicId = _deviceService.DefaultMicId;
            var defaultSpeakerId = _deviceService.DefaultSpeakerId;

            // 先摘下旧项，建完新菜单再释放，避免 Opening 期间 Dispose 导致空白菜单
            var oldItems = new List<WinForms.ToolStripItem>();
            foreach (WinForms.ToolStripItem old in menu.Items) oldItems.Add(old);
            menu.Items.Clear();

            try
            {
                // 麦克风区
                var currentMic = mics.FirstOrDefault(m => m.Id == defaultMicId);
                var micHeader = currentMic != null ? $"麦克风: {currentMic.Name}" : "选择麦克风";
                menu.Items.Add(CreateHeader(micHeader));
                if (mics.Count == 0)
                {
                    menu.Items.Add(CreateEmptyItem("（无麦克风设备）"));
                }
                else
                {
                    foreach (var mic in mics)
                    {
                        menu.Items.Add(CreateDeviceItem(mic, mic.Id == defaultMicId, () => TrySetDefault(mic.Id)));
                    }
                }

                menu.Items.Add(new WinForms.ToolStripSeparator());

                // 音箱区
                var currentSpeaker = speakers.FirstOrDefault(s => s.Id == defaultSpeakerId);
                var speakerHeader = currentSpeaker != null ? $"音箱: {currentSpeaker.Name}" : "选择音箱";
                menu.Items.Add(CreateHeader(speakerHeader));
                if (speakers.Count == 0)
                {
                    menu.Items.Add(CreateEmptyItem("（无音箱设备）"));
                }
                else
                {
                    foreach (var speaker in speakers)
                    {
                        menu.Items.Add(CreateDeviceItem(speaker, speaker.Id == defaultSpeakerId, () => TrySetDefault(speaker.Id)));
                    }
                }

                menu.Items.Add(new WinForms.ToolStripSeparator());

                var openItem = new WinForms.ToolStripMenuItem("打开主窗口");
                openItem.Click += (_, _) => _onShowWindow?.Invoke();
                menu.Items.Add(openItem);

                var exitItem = new WinForms.ToolStripMenuItem("退出");
                exitItem.Click += (_, _) => _onExit?.Invoke();
                menu.Items.Add(exitItem);
            }
            finally
            {
                foreach (var old in oldItems) old.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("重建托盘菜单失败", ex);
        }
    }

    // Font 复用，避免每次右键重建都创建新 GDI 对象
    private static readonly Drawing.Font _headerFont = new("Segoe UI", 9, Drawing.FontStyle.Bold);
    private static readonly Drawing.Font _defaultFont = new("Segoe UI", 9, Drawing.FontStyle.Bold);

    /// <summary>
    /// 分区标题。用禁用的 ToolStripMenuItem 而不是 ToolStripLabel，
    /// 后者在 ContextMenuStrip + 自定义 Renderer 下经常高度为 0 / 不显示。
    /// </summary>
    private static WinForms.ToolStripMenuItem CreateHeader(string text)
    {
        var isDark = ThemeService.IsDark(SettingsManager.Current.Theme);
        return new WinForms.ToolStripMenuItem(text)
        {
            Enabled = false,
            ForeColor = isDark ? Drawing.Color.FromArgb(160, 160, 160) : Drawing.Color.FromArgb(120, 120, 120),
            Font = _headerFont,
            Padding = new WinForms.Padding(4, 6, 4, 2)
        };
    }

    private static WinForms.ToolStripMenuItem CreateEmptyItem(string text)
    {
        return new WinForms.ToolStripMenuItem(text) { Enabled = false };
    }

    private static WinForms.ToolStripMenuItem CreateDeviceItem(AudioDevice device, bool isDefault, Action onClick)
    {
        // 用 ✓ 前缀标默认设备：自定义 Renderer 下 Checked 对勾经常看不见
        var title = isDefault ? $"✓ {device.Name}" : device.Name;
        var item = new WinForms.ToolStripMenuItem(title)
        {
            Enabled = device.IsConnected,
            Padding = new WinForms.Padding(4, 4, 4, 4)
        };
        if (isDefault) item.Font = _defaultFont;
        if (!device.IsConnected) item.ForeColor = Drawing.Color.Gray;
        item.Click += (_, _) => onClick();
        return item;
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
        graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
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
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}

/// <summary>
/// Win11 风格的菜单渲染器：自定义背景、高亮、分隔线颜色。
/// </summary>
internal sealed class ModernMenuRenderer : WinForms.ToolStripProfessionalRenderer
{
    public ModernMenuRenderer() : base(new ModernColorTable()) { }

    protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
    {
        var rect = new Drawing.Rectangle(Drawing.Point.Empty, e.Item.Size);
        if (e.Item.Selected)
        {
            var hoverColor = ThemeService.IsDark(SettingsManager.Current.Theme)
                ? Drawing.Color.FromArgb(20, 255, 255, 255)
                : Drawing.Color.FromArgb(20, 0, 0, 0);
            using var brush = new Drawing.SolidBrush(hoverColor);
            e.Graphics.FillRectangle(brush, rect);
        }
    }

    protected override void OnRenderToolStripBorder(WinForms.ToolStripRenderEventArgs e)
    {
        using var pen = new Drawing.Pen(Drawing.Color.FromArgb(40, 0, 0, 0));
        var rect = new Drawing.Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        e.Graphics.DrawRectangle(pen, rect);
    }
}

/// <summary>
/// 菜单颜色表：定义背景、文字、分隔线等颜色。
/// </summary>
internal sealed class ModernColorTable : WinForms.ProfessionalColorTable
{
    // 5.7：根据系统主题切换托盘菜单配色
    private static bool IsDark => ThemeService.IsDark(SettingsManager.Current.Theme);

    private static Drawing.Color Bg => IsDark ? Drawing.Color.FromArgb(40, 40, 40) : Drawing.Color.FromArgb(248, 248, 248);
    private static Drawing.Color Border => IsDark ? Drawing.Color.FromArgb(60, 60, 60) : Drawing.Color.FromArgb(200, 200, 200);
    private static Drawing.Color Hover => IsDark ? Drawing.Color.FromArgb(255, 255, 255, 255) : Drawing.Color.FromArgb(20, 0, 0, 0);
    private static Drawing.Color Sep => IsDark ? Drawing.Color.FromArgb(60, 255, 255, 255) : Drawing.Color.FromArgb(60, 0, 0, 0);

    public override Drawing.Color ToolStripDropDownBackground => Bg;
    public override Drawing.Color ImageMarginGradientBegin => Bg;
    public override Drawing.Color ImageMarginGradientMiddle => Bg;
    public override Drawing.Color ImageMarginGradientEnd => Bg;
    public override Drawing.Color MenuBorder => Border;
    public override Drawing.Color MenuItemBorder => Border;
    public override Drawing.Color MenuItemSelected => Hover;
    public override Drawing.Color SeparatorDark => Sep;
    public override Drawing.Color SeparatorLight => Drawing.Color.Transparent;
}