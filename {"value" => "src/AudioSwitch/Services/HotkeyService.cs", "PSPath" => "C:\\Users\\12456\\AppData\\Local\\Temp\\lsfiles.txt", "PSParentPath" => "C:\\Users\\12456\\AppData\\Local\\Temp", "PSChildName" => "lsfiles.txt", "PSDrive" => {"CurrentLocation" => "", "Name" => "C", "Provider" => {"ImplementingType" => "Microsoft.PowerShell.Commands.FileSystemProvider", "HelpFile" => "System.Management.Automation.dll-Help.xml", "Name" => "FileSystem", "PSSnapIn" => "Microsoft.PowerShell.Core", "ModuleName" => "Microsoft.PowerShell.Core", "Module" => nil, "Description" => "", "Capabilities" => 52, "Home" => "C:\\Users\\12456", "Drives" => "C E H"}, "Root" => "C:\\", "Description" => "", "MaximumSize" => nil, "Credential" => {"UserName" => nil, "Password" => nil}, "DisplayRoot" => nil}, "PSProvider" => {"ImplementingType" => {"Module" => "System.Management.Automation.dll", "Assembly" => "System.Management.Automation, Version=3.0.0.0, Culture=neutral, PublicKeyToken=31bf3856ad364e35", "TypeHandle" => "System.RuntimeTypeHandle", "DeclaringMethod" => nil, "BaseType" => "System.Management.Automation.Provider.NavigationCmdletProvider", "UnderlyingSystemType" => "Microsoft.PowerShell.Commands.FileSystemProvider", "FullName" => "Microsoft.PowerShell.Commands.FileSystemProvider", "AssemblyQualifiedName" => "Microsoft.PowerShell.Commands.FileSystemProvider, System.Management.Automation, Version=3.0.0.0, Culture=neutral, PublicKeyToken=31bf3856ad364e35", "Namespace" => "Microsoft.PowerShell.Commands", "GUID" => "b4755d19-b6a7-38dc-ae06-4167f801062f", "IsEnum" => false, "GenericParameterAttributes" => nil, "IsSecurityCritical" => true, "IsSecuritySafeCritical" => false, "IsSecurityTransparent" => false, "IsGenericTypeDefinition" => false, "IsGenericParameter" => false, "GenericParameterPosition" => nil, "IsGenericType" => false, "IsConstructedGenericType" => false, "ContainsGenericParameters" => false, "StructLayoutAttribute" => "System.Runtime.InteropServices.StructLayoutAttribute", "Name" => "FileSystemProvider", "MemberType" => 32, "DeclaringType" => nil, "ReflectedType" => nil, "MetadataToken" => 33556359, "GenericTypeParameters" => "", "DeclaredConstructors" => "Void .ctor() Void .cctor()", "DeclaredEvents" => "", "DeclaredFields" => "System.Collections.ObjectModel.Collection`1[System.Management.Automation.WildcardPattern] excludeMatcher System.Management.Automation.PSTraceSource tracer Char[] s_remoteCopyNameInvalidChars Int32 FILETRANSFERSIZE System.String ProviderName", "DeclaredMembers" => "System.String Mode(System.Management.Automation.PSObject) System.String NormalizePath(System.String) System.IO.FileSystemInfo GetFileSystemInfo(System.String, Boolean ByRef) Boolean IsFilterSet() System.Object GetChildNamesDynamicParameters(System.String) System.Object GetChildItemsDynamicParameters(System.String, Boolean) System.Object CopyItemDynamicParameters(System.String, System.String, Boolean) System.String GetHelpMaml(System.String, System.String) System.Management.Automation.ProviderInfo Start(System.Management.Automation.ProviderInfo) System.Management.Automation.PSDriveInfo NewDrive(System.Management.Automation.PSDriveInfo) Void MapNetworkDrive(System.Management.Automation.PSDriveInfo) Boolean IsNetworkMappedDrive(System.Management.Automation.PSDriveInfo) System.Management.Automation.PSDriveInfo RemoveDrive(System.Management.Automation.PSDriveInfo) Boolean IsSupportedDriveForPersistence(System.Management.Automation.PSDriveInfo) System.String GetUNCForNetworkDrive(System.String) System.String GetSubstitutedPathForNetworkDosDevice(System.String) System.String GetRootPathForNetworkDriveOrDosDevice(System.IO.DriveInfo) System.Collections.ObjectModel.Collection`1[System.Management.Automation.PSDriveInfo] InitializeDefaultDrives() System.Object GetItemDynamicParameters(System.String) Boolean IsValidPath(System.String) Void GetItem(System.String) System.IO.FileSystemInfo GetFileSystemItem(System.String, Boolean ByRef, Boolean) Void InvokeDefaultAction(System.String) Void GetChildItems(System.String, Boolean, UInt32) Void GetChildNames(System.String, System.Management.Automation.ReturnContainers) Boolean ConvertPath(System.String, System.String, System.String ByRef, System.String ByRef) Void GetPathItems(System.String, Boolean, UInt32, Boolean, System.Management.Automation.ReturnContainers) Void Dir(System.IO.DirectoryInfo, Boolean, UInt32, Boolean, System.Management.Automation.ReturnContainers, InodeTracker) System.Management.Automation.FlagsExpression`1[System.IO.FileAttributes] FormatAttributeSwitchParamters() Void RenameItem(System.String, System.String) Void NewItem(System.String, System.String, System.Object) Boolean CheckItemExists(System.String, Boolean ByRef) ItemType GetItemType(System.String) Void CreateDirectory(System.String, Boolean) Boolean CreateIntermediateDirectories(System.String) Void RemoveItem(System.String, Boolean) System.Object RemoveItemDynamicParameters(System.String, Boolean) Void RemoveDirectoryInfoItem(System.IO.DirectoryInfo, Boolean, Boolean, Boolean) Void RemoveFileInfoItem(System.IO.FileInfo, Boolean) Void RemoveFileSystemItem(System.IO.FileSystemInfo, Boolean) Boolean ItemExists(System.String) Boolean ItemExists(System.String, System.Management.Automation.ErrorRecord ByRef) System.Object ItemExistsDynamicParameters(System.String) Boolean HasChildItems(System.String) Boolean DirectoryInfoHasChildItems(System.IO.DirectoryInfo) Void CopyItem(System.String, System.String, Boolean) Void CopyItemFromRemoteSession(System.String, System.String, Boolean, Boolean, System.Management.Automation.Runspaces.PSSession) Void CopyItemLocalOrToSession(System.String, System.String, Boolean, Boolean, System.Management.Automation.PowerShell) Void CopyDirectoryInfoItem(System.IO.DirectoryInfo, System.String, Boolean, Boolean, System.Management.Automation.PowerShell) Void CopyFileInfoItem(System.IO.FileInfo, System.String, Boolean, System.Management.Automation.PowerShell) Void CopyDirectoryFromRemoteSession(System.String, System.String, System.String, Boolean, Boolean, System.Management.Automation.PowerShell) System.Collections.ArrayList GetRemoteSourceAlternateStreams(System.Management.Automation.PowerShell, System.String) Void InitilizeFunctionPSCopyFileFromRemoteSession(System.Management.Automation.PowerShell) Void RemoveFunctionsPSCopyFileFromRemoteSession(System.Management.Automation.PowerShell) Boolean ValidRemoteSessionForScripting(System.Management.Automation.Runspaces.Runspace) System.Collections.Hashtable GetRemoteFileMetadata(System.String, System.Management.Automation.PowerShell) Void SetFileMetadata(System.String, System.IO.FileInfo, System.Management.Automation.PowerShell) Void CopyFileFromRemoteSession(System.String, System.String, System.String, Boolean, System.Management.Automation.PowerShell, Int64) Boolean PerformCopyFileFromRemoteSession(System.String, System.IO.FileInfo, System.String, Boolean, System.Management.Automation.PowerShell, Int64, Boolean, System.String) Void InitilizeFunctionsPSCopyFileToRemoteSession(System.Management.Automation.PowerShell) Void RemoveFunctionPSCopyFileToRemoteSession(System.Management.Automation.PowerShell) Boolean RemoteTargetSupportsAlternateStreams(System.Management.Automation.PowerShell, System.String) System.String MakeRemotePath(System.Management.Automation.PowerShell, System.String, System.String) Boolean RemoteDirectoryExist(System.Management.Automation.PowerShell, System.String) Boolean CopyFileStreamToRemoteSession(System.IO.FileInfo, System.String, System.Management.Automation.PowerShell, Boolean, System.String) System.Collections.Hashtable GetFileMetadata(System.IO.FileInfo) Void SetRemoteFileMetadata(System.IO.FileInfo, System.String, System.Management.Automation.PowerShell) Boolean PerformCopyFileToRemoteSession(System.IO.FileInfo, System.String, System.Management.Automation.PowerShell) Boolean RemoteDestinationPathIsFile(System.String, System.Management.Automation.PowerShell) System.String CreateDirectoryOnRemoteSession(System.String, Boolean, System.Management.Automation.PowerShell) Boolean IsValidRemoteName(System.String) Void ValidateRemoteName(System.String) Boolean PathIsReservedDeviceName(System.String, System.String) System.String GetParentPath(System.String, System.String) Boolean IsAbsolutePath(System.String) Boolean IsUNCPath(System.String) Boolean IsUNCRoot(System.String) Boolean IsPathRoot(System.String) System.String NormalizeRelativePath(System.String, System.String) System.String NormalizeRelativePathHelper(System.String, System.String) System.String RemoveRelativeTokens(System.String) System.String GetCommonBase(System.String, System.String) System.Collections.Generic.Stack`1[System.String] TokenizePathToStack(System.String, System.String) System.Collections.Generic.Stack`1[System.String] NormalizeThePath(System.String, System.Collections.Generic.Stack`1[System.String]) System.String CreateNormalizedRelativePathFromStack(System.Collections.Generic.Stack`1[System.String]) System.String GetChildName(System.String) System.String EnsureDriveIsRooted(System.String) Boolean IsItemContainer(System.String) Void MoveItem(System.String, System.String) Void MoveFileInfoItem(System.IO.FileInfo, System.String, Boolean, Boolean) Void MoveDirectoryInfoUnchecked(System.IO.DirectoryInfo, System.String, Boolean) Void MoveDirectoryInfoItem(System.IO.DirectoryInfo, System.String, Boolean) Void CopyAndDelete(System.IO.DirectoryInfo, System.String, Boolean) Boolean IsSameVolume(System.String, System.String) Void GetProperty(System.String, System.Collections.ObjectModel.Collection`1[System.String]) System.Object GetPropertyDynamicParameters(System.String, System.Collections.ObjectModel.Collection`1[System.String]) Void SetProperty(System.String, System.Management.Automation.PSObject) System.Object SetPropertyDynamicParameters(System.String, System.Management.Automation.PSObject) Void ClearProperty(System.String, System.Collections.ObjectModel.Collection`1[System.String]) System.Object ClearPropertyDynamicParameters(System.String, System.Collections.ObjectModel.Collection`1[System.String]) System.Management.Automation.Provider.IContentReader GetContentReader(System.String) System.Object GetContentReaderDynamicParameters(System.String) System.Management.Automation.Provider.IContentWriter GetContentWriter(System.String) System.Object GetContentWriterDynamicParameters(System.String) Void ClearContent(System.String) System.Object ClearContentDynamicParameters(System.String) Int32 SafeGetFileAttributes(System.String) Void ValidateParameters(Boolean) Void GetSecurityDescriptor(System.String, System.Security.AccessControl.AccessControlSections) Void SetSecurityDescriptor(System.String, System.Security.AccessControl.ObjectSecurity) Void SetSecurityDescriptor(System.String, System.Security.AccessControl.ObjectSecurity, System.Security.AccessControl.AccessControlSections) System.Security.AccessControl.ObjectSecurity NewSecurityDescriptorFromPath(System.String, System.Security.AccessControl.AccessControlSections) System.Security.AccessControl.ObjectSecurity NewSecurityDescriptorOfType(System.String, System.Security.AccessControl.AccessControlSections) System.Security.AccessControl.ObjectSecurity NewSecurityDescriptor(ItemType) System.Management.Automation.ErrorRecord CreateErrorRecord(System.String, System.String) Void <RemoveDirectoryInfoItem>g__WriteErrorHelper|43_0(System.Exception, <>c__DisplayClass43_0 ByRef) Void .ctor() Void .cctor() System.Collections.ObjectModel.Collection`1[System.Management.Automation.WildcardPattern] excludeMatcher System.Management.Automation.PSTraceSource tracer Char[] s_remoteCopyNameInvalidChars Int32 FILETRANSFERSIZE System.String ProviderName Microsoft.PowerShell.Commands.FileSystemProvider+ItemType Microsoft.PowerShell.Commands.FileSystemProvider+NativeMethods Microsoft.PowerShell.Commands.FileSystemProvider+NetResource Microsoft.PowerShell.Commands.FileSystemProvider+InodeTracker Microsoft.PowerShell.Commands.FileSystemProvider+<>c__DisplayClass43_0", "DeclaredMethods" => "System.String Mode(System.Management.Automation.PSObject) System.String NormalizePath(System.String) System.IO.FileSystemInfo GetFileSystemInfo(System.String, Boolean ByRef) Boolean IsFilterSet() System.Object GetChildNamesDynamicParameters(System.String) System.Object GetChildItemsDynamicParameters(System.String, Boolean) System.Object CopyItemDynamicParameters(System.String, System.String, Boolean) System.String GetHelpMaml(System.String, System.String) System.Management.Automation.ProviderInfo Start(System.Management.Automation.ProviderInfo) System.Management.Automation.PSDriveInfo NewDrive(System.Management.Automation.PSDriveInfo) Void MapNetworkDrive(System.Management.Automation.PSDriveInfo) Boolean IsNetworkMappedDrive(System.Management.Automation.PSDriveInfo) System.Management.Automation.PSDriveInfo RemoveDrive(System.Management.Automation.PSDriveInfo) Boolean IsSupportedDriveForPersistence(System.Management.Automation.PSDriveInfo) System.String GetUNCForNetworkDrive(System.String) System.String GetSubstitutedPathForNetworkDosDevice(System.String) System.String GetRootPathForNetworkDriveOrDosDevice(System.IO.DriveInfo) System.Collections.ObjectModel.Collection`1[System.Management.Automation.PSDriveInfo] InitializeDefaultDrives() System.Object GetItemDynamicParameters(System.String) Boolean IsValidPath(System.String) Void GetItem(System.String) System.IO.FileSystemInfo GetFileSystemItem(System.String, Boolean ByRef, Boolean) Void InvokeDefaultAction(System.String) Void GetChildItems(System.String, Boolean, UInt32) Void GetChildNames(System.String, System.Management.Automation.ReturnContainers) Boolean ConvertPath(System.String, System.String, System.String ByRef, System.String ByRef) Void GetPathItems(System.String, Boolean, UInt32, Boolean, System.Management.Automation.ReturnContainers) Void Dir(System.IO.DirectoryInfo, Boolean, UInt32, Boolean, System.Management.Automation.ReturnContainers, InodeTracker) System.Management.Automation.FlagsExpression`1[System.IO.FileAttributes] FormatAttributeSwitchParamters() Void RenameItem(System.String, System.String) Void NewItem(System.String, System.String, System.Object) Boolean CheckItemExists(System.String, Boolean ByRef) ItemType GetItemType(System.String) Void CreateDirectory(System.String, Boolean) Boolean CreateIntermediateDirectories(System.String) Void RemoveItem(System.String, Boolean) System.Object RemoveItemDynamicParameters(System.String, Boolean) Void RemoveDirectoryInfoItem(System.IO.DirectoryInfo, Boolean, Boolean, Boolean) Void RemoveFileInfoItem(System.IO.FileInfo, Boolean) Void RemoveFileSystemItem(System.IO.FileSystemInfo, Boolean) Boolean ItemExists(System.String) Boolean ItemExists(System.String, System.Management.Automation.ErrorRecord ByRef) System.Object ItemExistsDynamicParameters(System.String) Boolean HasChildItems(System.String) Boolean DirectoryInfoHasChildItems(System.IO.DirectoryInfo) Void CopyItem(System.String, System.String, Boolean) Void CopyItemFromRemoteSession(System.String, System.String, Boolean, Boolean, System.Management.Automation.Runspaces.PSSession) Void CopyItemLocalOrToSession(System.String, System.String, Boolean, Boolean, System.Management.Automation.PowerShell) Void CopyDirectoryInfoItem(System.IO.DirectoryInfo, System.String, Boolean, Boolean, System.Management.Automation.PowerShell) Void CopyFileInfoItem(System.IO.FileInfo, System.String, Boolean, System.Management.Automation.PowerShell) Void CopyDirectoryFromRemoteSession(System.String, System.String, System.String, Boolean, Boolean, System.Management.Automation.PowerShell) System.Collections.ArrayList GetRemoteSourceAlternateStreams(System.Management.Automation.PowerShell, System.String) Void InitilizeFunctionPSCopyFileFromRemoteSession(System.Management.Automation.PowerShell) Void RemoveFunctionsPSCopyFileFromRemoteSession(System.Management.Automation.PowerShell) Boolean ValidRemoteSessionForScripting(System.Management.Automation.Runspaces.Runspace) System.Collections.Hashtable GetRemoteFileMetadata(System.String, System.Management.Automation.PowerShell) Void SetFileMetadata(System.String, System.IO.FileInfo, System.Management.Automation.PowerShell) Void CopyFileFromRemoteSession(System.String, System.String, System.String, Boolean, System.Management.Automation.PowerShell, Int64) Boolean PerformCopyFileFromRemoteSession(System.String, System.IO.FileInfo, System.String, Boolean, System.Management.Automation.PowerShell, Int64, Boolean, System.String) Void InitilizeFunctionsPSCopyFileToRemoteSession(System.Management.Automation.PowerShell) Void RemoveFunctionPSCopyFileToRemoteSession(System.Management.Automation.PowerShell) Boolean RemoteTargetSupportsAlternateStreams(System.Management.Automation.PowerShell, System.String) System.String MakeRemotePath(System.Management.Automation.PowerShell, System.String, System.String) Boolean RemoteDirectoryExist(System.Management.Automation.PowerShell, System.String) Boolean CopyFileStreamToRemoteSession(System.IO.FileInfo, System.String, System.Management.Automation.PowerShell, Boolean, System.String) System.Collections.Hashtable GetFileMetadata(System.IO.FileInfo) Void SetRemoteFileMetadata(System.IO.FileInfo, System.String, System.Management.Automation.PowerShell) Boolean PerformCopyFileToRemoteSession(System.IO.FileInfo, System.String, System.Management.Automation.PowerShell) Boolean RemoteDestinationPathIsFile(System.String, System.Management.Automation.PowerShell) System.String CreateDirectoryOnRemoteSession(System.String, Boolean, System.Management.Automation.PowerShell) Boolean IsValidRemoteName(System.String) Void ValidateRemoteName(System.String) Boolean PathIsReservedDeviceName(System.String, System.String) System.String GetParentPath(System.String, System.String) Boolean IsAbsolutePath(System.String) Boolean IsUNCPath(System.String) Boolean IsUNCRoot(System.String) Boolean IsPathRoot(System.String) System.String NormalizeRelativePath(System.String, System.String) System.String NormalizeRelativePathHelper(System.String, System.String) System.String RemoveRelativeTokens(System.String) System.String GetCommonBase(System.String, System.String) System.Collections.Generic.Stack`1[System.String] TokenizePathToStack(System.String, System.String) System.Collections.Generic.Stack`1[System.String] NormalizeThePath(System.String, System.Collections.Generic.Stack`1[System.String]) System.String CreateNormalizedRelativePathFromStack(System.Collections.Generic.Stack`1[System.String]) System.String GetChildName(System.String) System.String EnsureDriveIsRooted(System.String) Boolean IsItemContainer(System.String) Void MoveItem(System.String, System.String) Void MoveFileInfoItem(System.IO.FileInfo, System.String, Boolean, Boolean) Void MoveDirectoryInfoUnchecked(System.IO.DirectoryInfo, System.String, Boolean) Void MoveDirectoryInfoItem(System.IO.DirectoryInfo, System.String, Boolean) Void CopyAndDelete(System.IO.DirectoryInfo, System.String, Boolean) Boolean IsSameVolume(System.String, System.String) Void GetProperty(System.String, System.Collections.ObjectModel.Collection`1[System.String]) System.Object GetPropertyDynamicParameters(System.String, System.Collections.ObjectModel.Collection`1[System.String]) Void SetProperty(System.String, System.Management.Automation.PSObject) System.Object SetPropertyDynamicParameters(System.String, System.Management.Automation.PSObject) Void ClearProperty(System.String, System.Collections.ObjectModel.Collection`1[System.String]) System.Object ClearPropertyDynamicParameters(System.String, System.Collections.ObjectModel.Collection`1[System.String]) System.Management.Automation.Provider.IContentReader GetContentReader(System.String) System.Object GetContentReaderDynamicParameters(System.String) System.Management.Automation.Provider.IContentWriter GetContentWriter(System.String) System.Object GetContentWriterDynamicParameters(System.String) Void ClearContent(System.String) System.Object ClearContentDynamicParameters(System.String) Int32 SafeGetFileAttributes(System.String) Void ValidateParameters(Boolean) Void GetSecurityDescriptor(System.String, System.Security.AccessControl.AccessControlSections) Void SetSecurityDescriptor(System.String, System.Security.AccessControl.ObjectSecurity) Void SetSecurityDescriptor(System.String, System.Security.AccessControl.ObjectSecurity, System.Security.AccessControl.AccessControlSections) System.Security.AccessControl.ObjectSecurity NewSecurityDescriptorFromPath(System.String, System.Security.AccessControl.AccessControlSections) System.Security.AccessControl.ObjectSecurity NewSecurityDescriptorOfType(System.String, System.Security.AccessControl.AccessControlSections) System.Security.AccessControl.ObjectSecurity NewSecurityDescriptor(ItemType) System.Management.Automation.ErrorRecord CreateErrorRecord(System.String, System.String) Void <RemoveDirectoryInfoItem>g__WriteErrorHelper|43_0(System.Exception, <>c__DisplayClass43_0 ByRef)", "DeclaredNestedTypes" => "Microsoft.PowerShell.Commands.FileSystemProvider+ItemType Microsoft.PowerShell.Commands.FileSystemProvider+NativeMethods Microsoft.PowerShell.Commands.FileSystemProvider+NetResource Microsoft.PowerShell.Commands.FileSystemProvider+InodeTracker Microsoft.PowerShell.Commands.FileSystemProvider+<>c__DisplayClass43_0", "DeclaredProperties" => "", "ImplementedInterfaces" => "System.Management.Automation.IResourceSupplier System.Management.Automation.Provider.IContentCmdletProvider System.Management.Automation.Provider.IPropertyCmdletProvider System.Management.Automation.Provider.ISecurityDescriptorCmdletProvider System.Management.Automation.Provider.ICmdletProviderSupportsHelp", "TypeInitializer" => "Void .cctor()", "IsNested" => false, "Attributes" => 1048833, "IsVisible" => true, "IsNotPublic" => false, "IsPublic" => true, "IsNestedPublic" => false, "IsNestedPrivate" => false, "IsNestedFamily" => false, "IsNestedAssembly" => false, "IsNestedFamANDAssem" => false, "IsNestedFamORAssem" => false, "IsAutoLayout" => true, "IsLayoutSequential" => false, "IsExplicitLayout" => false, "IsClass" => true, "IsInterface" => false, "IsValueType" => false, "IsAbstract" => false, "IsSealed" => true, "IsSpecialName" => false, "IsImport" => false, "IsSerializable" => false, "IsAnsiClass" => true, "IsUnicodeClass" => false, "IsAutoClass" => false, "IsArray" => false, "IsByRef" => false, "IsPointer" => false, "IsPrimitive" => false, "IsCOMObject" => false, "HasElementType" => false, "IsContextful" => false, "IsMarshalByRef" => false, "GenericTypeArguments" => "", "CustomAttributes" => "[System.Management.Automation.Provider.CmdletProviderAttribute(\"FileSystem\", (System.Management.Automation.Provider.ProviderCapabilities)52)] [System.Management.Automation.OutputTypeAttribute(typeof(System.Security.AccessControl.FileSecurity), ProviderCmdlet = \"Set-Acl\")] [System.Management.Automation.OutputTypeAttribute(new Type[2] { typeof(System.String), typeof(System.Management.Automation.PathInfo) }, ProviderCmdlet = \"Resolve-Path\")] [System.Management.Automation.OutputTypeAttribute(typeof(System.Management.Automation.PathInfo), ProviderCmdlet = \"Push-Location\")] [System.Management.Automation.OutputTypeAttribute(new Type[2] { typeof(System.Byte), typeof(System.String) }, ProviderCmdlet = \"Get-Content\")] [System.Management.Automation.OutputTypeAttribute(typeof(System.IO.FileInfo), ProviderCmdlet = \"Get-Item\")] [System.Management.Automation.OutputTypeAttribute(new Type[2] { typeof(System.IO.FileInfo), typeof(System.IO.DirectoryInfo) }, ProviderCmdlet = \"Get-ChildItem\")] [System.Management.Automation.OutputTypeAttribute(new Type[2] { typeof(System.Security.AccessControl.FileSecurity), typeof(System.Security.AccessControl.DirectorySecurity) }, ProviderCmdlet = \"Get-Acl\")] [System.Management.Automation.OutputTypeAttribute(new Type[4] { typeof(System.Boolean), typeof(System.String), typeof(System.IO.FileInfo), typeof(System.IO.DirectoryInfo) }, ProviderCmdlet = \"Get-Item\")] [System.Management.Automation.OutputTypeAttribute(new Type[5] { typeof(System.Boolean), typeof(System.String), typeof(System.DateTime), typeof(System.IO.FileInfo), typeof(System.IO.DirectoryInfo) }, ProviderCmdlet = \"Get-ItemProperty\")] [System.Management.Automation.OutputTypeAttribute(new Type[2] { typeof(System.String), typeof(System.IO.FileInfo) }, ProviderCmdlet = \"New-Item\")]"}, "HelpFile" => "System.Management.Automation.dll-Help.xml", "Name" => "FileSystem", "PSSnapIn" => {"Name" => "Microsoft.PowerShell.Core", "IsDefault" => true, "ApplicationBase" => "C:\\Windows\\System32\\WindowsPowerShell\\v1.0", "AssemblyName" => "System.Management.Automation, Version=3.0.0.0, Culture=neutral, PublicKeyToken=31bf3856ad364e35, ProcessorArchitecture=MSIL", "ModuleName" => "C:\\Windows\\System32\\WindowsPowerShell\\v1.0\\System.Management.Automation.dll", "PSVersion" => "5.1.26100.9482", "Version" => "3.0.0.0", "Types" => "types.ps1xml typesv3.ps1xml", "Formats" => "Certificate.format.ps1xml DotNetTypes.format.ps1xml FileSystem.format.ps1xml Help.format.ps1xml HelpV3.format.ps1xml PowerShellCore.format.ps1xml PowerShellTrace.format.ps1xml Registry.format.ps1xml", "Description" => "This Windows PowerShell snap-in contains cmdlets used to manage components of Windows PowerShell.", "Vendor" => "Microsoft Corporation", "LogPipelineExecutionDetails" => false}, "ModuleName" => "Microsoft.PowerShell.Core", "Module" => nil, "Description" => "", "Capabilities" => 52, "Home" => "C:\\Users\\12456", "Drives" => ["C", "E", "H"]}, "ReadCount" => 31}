using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AudioSwitch.Services;

/// <summary>
/// 全局快捷键服务：登记一组系统级组合键，无论用户当前在用什么软件，
/// 按下组合键都会触发对应的操作。支持动态更改快捷键。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private readonly IntPtr _handle;
    private readonly HwndSource? _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private readonly Dictionary<int, HotkeyRegistration> _registrations = new();
    private int _nextId = 1;
    private bool _disposed = false;

    public HotkeyService(IntPtr handle)
    {
        _handle = handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>
    /// 登记一个组合键并绑定要执行的操作，返回热键ID。
    /// </summary>
    public int Register(uint modifiers, uint key, Action action)
    {
        int id = _nextId++;
        if (!RegisterHotKey(_handle, id, modifiers, key))
            throw new InvalidOperationException("无法注册全局快捷键，可能已被其他程序占用。");
        _handlers[id] = action;
        _registrations[id] = new HotkeyRegistration { Modifiers = modifiers, Key = key };
        return id;
    }

    /// <summary>
    /// 更新快捷键（修复 M3：新组合注册失败时回滚为原组合，避免旧快捷键直接失效）。
    /// </summary>
    public void UpdateHotkey(int id, uint newModifiers, uint newKey, Action newAction)
    {
        if (!_handlers.ContainsKey(id)) return;
        if (!_registrations.TryGetValue(id, out var old)) return;

        UnregisterHotKey(_handle, id);

        if (!RegisterHotKey(_handle, id, newModifiers, newKey))
        {
            // 回滚：把原组合重新注册回去
            bool rolledBack = RegisterHotKey(_handle, id, old.Modifiers, old.Key);
            if (!rolledBack)
            {
                _handlers.Remove(id);
                _registrations.Remove(id);
                throw new InvalidOperationException("新快捷键注册失败，且原快捷键无法恢复，请重启程序。");
            }
            throw new InvalidOperationException("无法注册新的快捷键，可能已被其他程序占用（已恢复原快捷键）。");
        }

        _handlers[id] = newAction;
        _registrations[id] = new HotkeyRegistration { Modifiers = newModifiers, Key = newKey };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_handlers.TryGetValue(id, out var action))
            {
                action();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (int id in _handlers.Keys)
                UnregisterHotKey(_handle, id);
            _handlers.Clear();
            _registrations.Clear();
            _source?.RemoveHook(WndProc);
            _disposed = true;
        }
    }
}

/// <summary>
/// 快捷键注册信息
/// </summary>
public class HotkeyRegistration
{
    public uint Modifiers { get; set; }
    public uint Key { get; set; }
}
