using System.Runtime.InteropServices;
using AudioSwitch.Services;

namespace AudioSwitch.Core;

/// <summary>
/// 负责切换 Windows 的默认音频设备（麦克风 / 音箱）。
/// 原理：Windows 没有公开"设置默认设备"的接口，这里调用其内部接口 IPolicyConfig
/// （未公开但长期稳定，SoundSwitch、EarTrumpet 等同类软件都依赖它）。
/// </summary>
public static class DeviceSwitcher
{
    // Windows 内部组件的类标识（CLSID）
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class CPolicyConfigClient { }

    // Windows 内部接口 IPolicyConfig 的定义
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        int GetMixFormat(string deviceId, IntPtr format);
        int GetDeviceFormat(string deviceId, int isDefault, IntPtr format);
        int ResetDeviceFormat(string deviceId);
        int SetDeviceFormat(string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        int GetProcessingPeriod(string deviceId, int isDefault, IntPtr defaultPeriod, IntPtr minPeriod);
        int SetProcessingPeriod(string deviceId, IntPtr period);
        int GetShareMode(string deviceId, IntPtr shareMode);
        int SetShareMode(string deviceId, IntPtr shareMode);
        int GetPropertyValue(string deviceId, IntPtr key, IntPtr value);
        int SetPropertyValue(string deviceId, IntPtr key, IntPtr value);
        int SetDefaultEndpoint(string deviceId, int role);
        int SetEndpointVisibility(string deviceId, int visible);
    }

    // 默认设备角色（对应 Windows ERole 的实际取值）
    private const int ROLE_CONSOLE = 0;        // eConsole：默认设备
    private const int ROLE_MULTIMEDIA = 1;     // eMultimedia：默认多媒体设备
    private const int ROLE_COMMUNICATIONS = 2; // eCommunications：默认通信设备

    /// <summary>
    /// 把某个设备（用其 ID 指定）设为 Windows 默认设备。
    /// 三个角色都尝试设置，仅全部失败时才抛出异常。
    /// </summary>
    public static void SetDefault(string deviceId)
    {
        var config = (IPolicyConfig)new CPolicyConfigClient();
        try
        {
            int[] roles = { ROLE_CONSOLE, ROLE_MULTIMEDIA, ROLE_COMMUNICATIONS };
            int okCount = 0;
            Exception? lastError = null;

            foreach (var role in roles)
            {
                try
                {
                    Marshal.ThrowExceptionForHR(config.SetDefaultEndpoint(deviceId, role));
                    okCount++;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Logger.Error($"设置角色 {role} 失败", ex);
                }
            }

            // 仅当三个角色全部失败时才抛出异常
            if (okCount == 0 && lastError != null) throw lastError;
        }
        finally
        {
            // RCW 不释放会在托盘常驻场景下缓慢累积
            Marshal.FinalReleaseComObject(config);
        }
    }
}
