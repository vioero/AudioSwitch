using System.Windows;

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
        // v2.0：颜色全部走 App 级主题字典（Themes/Colors.*.xaml），深浅色自动跟随，
        // 不再需要本地资源与手动 ApplyTheme。
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
