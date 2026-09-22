using System.Windows;
using System.Windows.Media;
using AudioSwitch.Services;

namespace AudioSwitch;

/// <summary>
/// 关闭询问窗口：询问用户"完全退出"还是"最小化到托盘"，
/// 带"记住我的选择"勾选项。
/// 返回结果通过 DialogResult 或属性传递给主窗口。
/// </summary>
public partial class ClosePromptWindow : Window
{
    /// <summary>
    /// 用户的选择结果：true=完全退出，false=最小化到托盘，null=未选择。
    /// </summary>
    public bool? ExitChosen { get; private set; }

    /// <summary>
    /// 用户是否勾选了"记住我的选择"。
    /// </summary>
    public bool RememberChoice => RememberCheckBox.IsChecked == true;

    public ClosePromptWindow()
    {
        InitializeComponent();
        ApplyTheme();
    }

    /// <summary>
    /// 根据当前主题设置，更新弹窗颜色（浅色/深色）。
    /// 修复 S3：复用 ThemeService.IsDark，避免自行读注册表路径出错。
    /// </summary>
    private void ApplyTheme()
    {
        bool dark = ThemeService.IsDark(SettingsManager.Current.Theme);
        SetBrush("WindowBg", dark ? "#1E1E1E" : "#F3F3F3");
        SetBrush("MainText", dark ? "#F0F0F0" : "#1B1B1B");
        SetBrush("SubText",  dark ? "#B0B0B0" : "#666666");
    }

    private void SetBrush(string key, string hex)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            Resources[key] = new SolidColorBrush(color);
        }
        catch (Exception ex)
        {
            Logger.Error($"颜色解析失败 {key}={hex}", ex);
        }
    }

    private void TrayButton_Click(object sender, RoutedEventArgs e)
    {
        ExitChosen = false; // 最小化到托盘
        DialogResult = true;
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        ExitChosen = true; // 完全退出
        DialogResult = true;
    }
}
