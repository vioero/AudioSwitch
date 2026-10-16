using System.Runtime.InteropServices;

namespace AudioSwitch.Services;

/// <summary>
/// 系统「减少动画」检测（SPI_GETCLIENTAREAANIMATION）。
/// 用户开启「设置 → 辅助功能 → 视觉效果 → 动画效果」关闭时，
/// 抽屉滑入/滑出、遮罩淡入等面板级过渡降级为瞬时切换。
/// </summary>
public static class MotionService
{
    private const uint SPI_GETCLIENTAREAANIMATION = 0x1002;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref bool pvParam, uint fWinIni);

    /// <summary>
    /// 系统是否开启了「减少动画」（关闭了动画效果）。
    /// 读取失败时按「未开启」处理，保留默认动效。
    /// </summary>
    public static bool IsReducedMotion
    {
        get
        {
            try
            {
                bool animationsEnabled = true;
                if (SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref animationsEnabled, 0))
                    return !animationsEnabled;
            }
            catch { }
            return false;
        }
    }
}
