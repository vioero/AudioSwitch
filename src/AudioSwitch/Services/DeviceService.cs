using AudioSwitch.Models;
using NAudio.CoreAudioApi;

namespace AudioSwitch.Services;

/// <summary>
/// 设备管理服务：负责读取、过滤、刷新音频设备列表。
/// 优化策略：缓存设备元数据（Name、BusType），避免每次刷新都读 COM 属性。
/// </summary>
public sealed class DeviceService : IDisposable
{
    private readonly object _lock = new();
    private List<AudioDevice> _microphones = new();
    private List<AudioDevice> _speakers = new();
    private string? _defaultMicId;
    private string? _defaultSpeakerId;
    private System.Threading.Timer? _refreshTimer;
    private bool _disposed = false;
    private int _refreshing = 0;  // 修复 M1：重入保护

    // 设备元数据缓存（ID → {Name, BusType}），避免每次刷新都读 COM 属性
    // 修复 M1：所有缓存访问统一在 _lock 下进行
    private readonly Dictionary<string, (string Name, string BusType)> _metadataCache = new();

    /// <summary>
    /// 设备列表变化时触发（在后台线程调用）。
    /// </summary>
    public event Action? DevicesChanged;

    /// <summary>
    /// 获取当前麦克风列表（线程安全，返回副本）。
    /// </summary>
    public List<AudioDevice> Microphones
    {
        get { lock (_lock) return new List<AudioDevice>(_microphones); }
    }

    /// <summary>
    /// 获取当前音箱列表（线程安全，返回副本）。
    /// </summary>
    public List<AudioDevice> Speakers
    {
        get { lock (_lock) return new List<AudioDevice>(_speakers); }
    }

    /// <summary>
    /// 获取当前默认麦克风 ID。
    /// </summary>
    public string? DefaultMicId
    {
        get { lock (_lock) return _defaultMicId; }
    }

    /// <summary>
    /// 获取当前默认音箱 ID。
    /// </summary>
    public string? DefaultSpeakerId
    {
        get { lock (_lock) return _defaultSpeakerId; }
    }

    /// <summary>
    /// 启动设备刷新定时器（每 5 秒检查一次热插拔）。
    /// </summary>
    public void StartAutoRefresh()
    {
        _refreshTimer = new System.Threading.Timer(_ => Refresh(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// 比较两个设备列表是否等价（按 Id 排序后逐项比较 Id/IsConnected/Name）。
    /// 修复 M4：仅比较 Id 会漏掉 Active/Unplugged 状态切换。
    /// </summary>
    private static bool SameDevices(List<AudioDevice> a, List<AudioDevice> b)
    {
        if (a.Count != b.Count) return false;
        var sortedA = a.OrderBy(d => d.Id).ToList();
        var sortedB = b.OrderBy(d => d.Id).ToList();
        for (int i = 0; i < sortedA.Count; i++)
        {
            if (sortedA[i].Id != sortedB[i].Id ||
                sortedA[i].IsConnected != sortedB[i].IsConnected ||
                sortedA[i].Name != sortedB[i].Name)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 手动刷新设备列表（在后台线程执行，完成后触发 DevicesChanged 事件）。
    /// 修复 M1：重入保护——上一次刷新未结束时直接跳过。
    /// 修复 M2：仅在设备集合或默认设备变化时才通知 UI。
    /// </summary>
    public void Refresh()
    {
        if (_disposed) return;

        // 修复 M1：重入保护
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var (mics, speakers, defaultMicId, defaultSpeakerId) = BuildDeviceLists();
                bool changed;
                lock (_lock)
                {
                    // 修复 M2：设备集合或默认设备发生变化才通知 UI
                    changed = !SameDevices(_microphones, mics)
                           || !SameDevices(_speakers, speakers)
                           || _defaultMicId != defaultMicId
                           || _defaultSpeakerId != defaultSpeakerId;

                    _microphones = mics;
                    _speakers = speakers;
                    _defaultMicId = defaultMicId;
                    _defaultSpeakerId = defaultSpeakerId;
                }
                if (changed) DevicesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Error("设备刷新失败", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        });
    }

    /// <summary>
    /// 同步刷新设备列表（托盘菜单弹出前调用，确保读到的是最新数据）。
    /// 若已有后台刷新在跑则短暂等待其完成，等不到就沿用现有缓存。
    /// </summary>
    public void RefreshNow()
    {
        if (_disposed) return;

        var spin = 0;
        while (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            if (spin++ >= 50) return; // 等约 1 秒仍未拿到锁：用现有缓存
            System.Threading.Thread.Sleep(20);
        }

        try
        {
            var (mics, speakers, defaultMicId, defaultSpeakerId) = BuildDeviceLists();
            lock (_lock)
            {
                _microphones = mics;
                _speakers = speakers;
                _defaultMicId = defaultMicId;
                _defaultSpeakerId = defaultSpeakerId;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("同步刷新设备失败", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    /// <summary>
    /// 在后台线程读取所有设备。
    /// 使用元数据缓存，只有新设备才读 COM 属性，已有设备直接复用缓存。
    /// 修复 L4：MMDevice 为 IDisposable，必须逐个释放。
    /// 修复 M1：缓存访问统一在 _lock 下进行。
    /// </summary>
    private (List<AudioDevice> mics, List<AudioDevice> speakers, string? defaultMicId, string? defaultSpeakerId) BuildDeviceLists()
    {
        var mics = new List<AudioDevice>();
        var speakers = new List<AudioDevice>();
        string? defaultMicId = null;
        string? defaultSpeakerId = null;
        using var enumerator = new MMDeviceEnumerator();

        // 收集当前所有设备 ID，用于清理缓存中已消失的设备
        var currentIds = new HashSet<string>();

        // 修复 M2：MMDevice 实现了 IDisposable，必须逐个释放避免 COM 引用泄漏。
        // 修复 M3：枚举到的设备 ID 一律登记进 currentIds，避免被过滤设备的缓存被误删。
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active | DeviceState.Unplugged))
        {
            using (device)
            {
                // 修复 M3：先登记 ID，再决定是否显示
                var id = device.ID;
                currentIds.Add(id);
                var item = ReadDeviceCached(device);
                if (item != null) mics.Add(item);
            }
        }

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active | DeviceState.Unplugged))
        {
            using (device)
            {
                var id = device.ID;
                currentIds.Add(id);
                var item = ReadDeviceCached(device);
                if (item != null) speakers.Add(item);
            }
        }

        // 修复：默认设备 MMDevice 同样需要释放，避免每 5 秒泄漏两个 COM 对象
        try { using var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia); defaultMicId = mic?.ID; } catch { }
        try { using var spk = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); defaultSpeakerId = spk?.ID; } catch { }

        // 修复 M1：清理缓存中已消失的设备（在锁内完成）
        lock (_lock)
        {
            var staleIds = _metadataCache.Keys.Where(id => !currentIds.Contains(id)).ToList();
            foreach (var id in staleIds) _metadataCache.Remove(id);
        }

        return (mics, speakers, defaultMicId, defaultSpeakerId);
    }

    /// <summary>
    /// 读取设备信息，优先从缓存读取元数据，只有新设备才读 COM 属性。
    /// 修复 M1：缓存访问统一在 _lock 下进行。
    /// </summary>
    private AudioDevice? ReadDeviceCached(MMDevice device)
    {
        try
        {
            var id = device.ID;
            var isConnected = device.State == DeviceState.Active;
            string name;
            string busType;

            // 修复 M1：缓存访问统一在 _lock 下进行
            bool hit;
            lock (_lock)
            {
                hit = _metadataCache.TryGetValue(id, out var cached);
                if (hit)
                {
                    name = cached.Name;
                    busType = cached.BusType;
                }
                else
                {
                    name = string.Empty;
                    busType = string.Empty;
                }
            }

            if (!hit)
            {
                // 缓存未命中：COM 读取较慢，放在锁外执行
                name = device.FriendlyName;
                busType = ReadBusType(device);
                lock (_lock)
                {
                    _metadataCache[id] = (name, busType);
                }
            }

            if (!ShouldShowDevice(busType, name)) return null;
            return new AudioDevice(id, name, isConnected, busType);
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取设备信息失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 读取设备的连接总线类型（COM 属性读取，较慢）。
    /// </summary>
    private static string ReadBusType(MMDevice device)
    {
        try
        {
            var key = new NAudio.CoreAudioApi.PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 24);
            var val = device.Properties[key];
            return val.Value?.ToString() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// 根据设置的"设备显示范围"决定是否显示某个设备。
    /// 修复 S2：智能模式改为黑名单策略——默认显示真实设备，只排除虚拟/显示输出等杂项。
    /// </summary>
    private static bool ShouldShowDevice(string busType, string name)
    {
        var mode = SettingsManager.Current.DisplayMode;

        if (mode == DisplayMode.All) return true;

        // USB / 蓝牙模式：对总线类型的大小写与写法差异不敏感
        if (mode == DisplayMode.UsbBluetoothOnly) return IsExternalBus(busType);

        // 智能模式：外部设备直接显示；其余仅排除系统杂项（混音/显卡 HDMI/兜底端点）
        // 用户安装的虚拟声卡（VB-Cable 等）会显示，便于切换
        if (IsExternalBus(busType)) return true;
        return !IsVirtualOrMisc(name);
    }

    /// <summary>
    /// 是否为外接总线（USB / 蓝牙）设备。
    /// </summary>
    internal static bool IsExternalBus(string busType)
    {
        if (string.IsNullOrEmpty(busType)) return false;
        var t = busType.Trim().ToUpperInvariant();
        return t.Contains("USB") || t.Contains("BLUETOOTH") || t.Contains("BTHENUM");
    }

    /// <summary>
    /// 智能模式下需要隐藏的系统杂项端点（混音回环、显卡 HDMI、Windows 兜底端点）。
    /// 注意：不要隐藏用户主动安装的虚拟声卡（VB-Cable / VoiceMeeter / OBS 等），
    /// 那些是可切换的目标设备，应出现在列表中。
    /// </summary>
    internal static bool IsVirtualOrMisc(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;

        // 先归一化名称（去 ®、™、(r)、全角括号等），再匹配关键词
        var lower = name.ToLowerInvariant();
        lower = lower.Replace("®", "").Replace("™", "").Replace("(r)", "").Replace("（r）", "");
        lower = System.Text.RegularExpressions.Regex.Replace(lower, @"\s+", " ").Trim();

        var blocked = new[]
        {
            "stereo mix", "立体声混音",          // 环回混音
            "nvidia high definition audio",     // 显卡 HDMI 音频输出
            "amd high definition audio",
            "intel display audio",              // 归一化后匹配 Intel® Display Audio
            "display audio",                    // 通用显示输出音频
            "显示器音频",                        // 中文版显卡音频
            "dump", "primary sound", "sound mapper",  // Windows 兜底端点
            "主声音驱动程序", "声音映射器"        // 中文版兜底端点
        };

        foreach (var kw in blocked)
            if (lower.Contains(kw)) return true;
        return false;
    }

    /// <summary>
    /// 清除设备缓存（切换显示模式时调用，强制重新读取设备信息）。
    /// </summary>
    public void ClearCache()
    {
        lock (_lock) _metadataCache.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }
}
