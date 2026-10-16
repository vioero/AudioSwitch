using System.Windows;
using System.Windows.Media;
// 项目同时启用 WPF 与 WinForms 隐式 using，Application 需显式指向 WPF 的
using Application = System.Windows.Application;

namespace AudioSwitch.Services;

/// <summary>
/// 主题管理服务：负责浅色/深色主题的切换。
/// v2.0 起改为整体替换 App 资源里的颜色 ResourceDictionary（Themes/Colors.*.xaml），
/// 而不是逐个往 window.Resources 里塞画刷——后者在 DynamicResource 已解析过的
/// 场景下容易漏刷新（见 UI设计方案.md 7.1）。
/// </summary>
public static class ThemeService
{
    private const string LightDictionary = "Themes/Colors.Light.xaml";
    private const string DarkDictionary = "Themes/Colors.Dark.xaml";

    /// <summary>
    /// 根据当前设置切换浅色/深色主题：替换 App 级 MergedDictionaries 中的颜色字典。
    /// 界面所有 DynamicResource 引用会自动跟随刷新。
    /// </summary>
    public static void Apply()
    {
        try
        {
            var app = Application.Current;
            if (app == null) return;

            var dark = IsDark(SettingsManager.Current.Theme);
            var target = dark ? DarkDictionary : LightDictionary;

            var dictionaries = app.Resources.MergedDictionaries;
            for (int i = dictionaries.Count - 1; i >= 0; i--)
            {
                var source = dictionaries[i].Source?.OriginalString ?? string.Empty;
                if (source.EndsWith("Colors.Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                    source.EndsWith("Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    dictionaries.RemoveAt(i);
                }
            }

            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(target, UriKind.Relative)
            });
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
}
