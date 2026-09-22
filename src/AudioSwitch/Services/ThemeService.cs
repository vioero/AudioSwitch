using System.Windows;
using System.Windows.Media;

namespace AudioSwitch.Services;

/// <summary>
/// 主题管理服务：负责浅色/深色主题的切换。
/// 通过替换 DynamicResource 里的颜色画刷和渐变，实现全界面主题跟随。
/// </summary>
public static class ThemeService
{
    /// <summary>
    /// 根据当前设置切换浅色/深色主题，替换窗口资源里的颜色画刷和渐变。
    /// </summary>
    public static void Apply(Window window)
    {
        try
        {
            var theme = SettingsManager.Current.Theme;
            bool dark = IsDark(theme);

            // 纯色资源
            var newBrushes = new Dictionary<string, SolidColorBrush>
            {
                ["WindowBg"]      = MakeBrush(dark ? "#1E1E1E" : "#F3F3F3"),
                ["PanelBg"]       = MakeBrush(dark ? "#1E1E1E" : "#F3F3F3"),
                ["CardBg"]        = MakeBrush(dark ? "#2D2D2D" : "#FFFFFF"),
                ["CardBorder"]    = MakeBrush(dark ? "#3A3A3A" : "#E8E8E8"),
                ["SettingCardBg"] = MakeBrush(dark ? "#2D2D2D" : "#FFFFFF"),
                ["MainText"]      = MakeBrush(dark ? "#F0F0F0" : "#1B1B1B"),
                ["SubText"]       = MakeBrush(dark ? "#999999" : "#888888"),
                ["DisabledText"]  = MakeBrush(dark ? "#555555" : "#B0B0B0"),
                ["HoverBg"]       = MakeBrush(dark ? "#3D3D3D" : "#E9E9E9"),
                ["InputFieldBg"]  = MakeBrush(dark ? "#3D3D3D" : "#F5F5F5"),
            };
            foreach (var kv in newBrushes)
                window.Resources[kv.Key] = kv.Value;

            // 渐变资源（从上到下，subtle depth 感）
            var topColor = MakeColor(dark ? "#1E1E1E" : "#F3F3F3");
            var bottomColor = MakeColor(dark ? "#161616" : "#E8E8E8");
            var gradient = new LinearGradientBrush(topColor, bottomColor, new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));
            window.Resources["WindowBgGradient"] = gradient;
        }
        catch (Exception ex)
        {
            Logger.Error("主题应用失败", ex);
        }
    }

    /// <summary>
    /// 判断当前是否应该使用深色模式。
    /// </summary>
    public static bool IsDark(ThemeMode theme)
    {
        if (theme == ThemeMode.System)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                return value is int v && v == 0;
            }
            catch { return false; }
        }
        return theme == ThemeMode.Dark;
    }

    private static SolidColorBrush MakeBrush(string hex)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return new SolidColorBrush(color);
    }

    private static System.Windows.Media.Color MakeColor(string hex)
    {
        return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
    }
}
