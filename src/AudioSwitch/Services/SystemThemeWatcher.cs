using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AudioSwitch.Services;

/// <summary>
/// 监听系统深浅色变化（WM_SETTINGCHANGE + "ImmersiveColorSet"）。
/// 仅在 ThemeMode.System 下有意义：用户在系统设置里切换深浅色时实时换肤，
/// 不需要重启应用（见 UI设计方案.md 第 6 章边界情况）。
/// </summary>
public sealed class SystemThemeWatcher : IDisposable
{
    private const int WM_SETTINGCHANGE = 0x001A;

    private readonly Window _window;
    private readonly Action _onSystemThemeChanged;
    private HwndSource? _source;
    private bool _disposed;

    public SystemThemeWatcher(Window window, Action onSystemThemeChanged)
    {
        _window = window;
        _onSystemThemeChanged = onSystemThemeChanged;
        if (window.IsLoaded)
            Attach();
        else
            window.Loaded += (_, _) => Attach();
    }

    private void Attach()
    {
        if (_disposed) return;
        var helper = new WindowInteropHelper(_window);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE)
        {
            // 系统主题切换时广播的参数名是 "ImmersiveColorSet"
            var name = Marshal.PtrToStringAuto(lParam);
            if (string.Equals(name, "ImmersiveColorSet", StringComparison.Ordinal))
            {
                try { _onSystemThemeChanged(); }
                catch { /* 换肤失败不崩溃，保持现有主题 */ }
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
