using Microsoft.Win32;

namespace AudioSwitch.Services;

/// <summary>
/// 开机自启动服务：把程序写进 / 移出 Windows 的开机启动清单（注册表 Run 键）。
/// </summary>
public static class AutoStartService
{
    // Windows 存放"开机自动运行程序清单"的注册表位置
    private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string APP_NAME = "AudioSwitch";

    /// <summary>
    /// 查询当前是否已开启开机自启动。
    /// </summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY);
        return key?.GetValue(APP_NAME) != null;
    }

    /// <summary>
    /// 开启或关闭开机自启动。
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RUN_KEY);

        if (enabled)
        {
            // 把当前程序的完整路径写入清单（加引号，防止路径含空格出错）
            var exePath = Environment.ProcessPath;
            if (exePath != null)
                key.SetValue(APP_NAME, "\"" + exePath + "\"");
        }
        else
        {
            // 从清单中删除
            key.DeleteValue(APP_NAME, throwOnMissingValue: false);
        }
    }
}
