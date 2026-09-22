using System.Windows;

namespace AudioSwitch;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private System.Threading.Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例检测：用一个全局互斥体判断"是否已有实例在运行"
        _mutex = new System.Threading.Mutex(true, @"Local\AudioSwitch_SingleInstance_Mutex", out bool createdNew);

        if (!createdNew)
        {
            // 已有实例在运行：通知它唤起窗口，然后当前实例退出
            NotifyExistingInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    /// <summary>
    /// 通知已运行的实例唤起主窗口。
    /// 通过一个命名事件（EventWaitHandle）实现跨进程通知。
    /// </summary>
    private static void NotifyExistingInstance()
    {
        try
        {
            using var evt = System.Threading.EventWaitHandle.OpenExisting(@"Local\AudioSwitch_WakeUp_Event");
            evt.Set();
        }
        catch
        {
            // 已有实例可能还没创建事件（极端时序），忽略即可
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }
}