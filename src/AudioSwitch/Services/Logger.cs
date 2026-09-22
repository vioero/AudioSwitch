using System.IO;

namespace AudioSwitch.Services;

/// <summary>
/// 轻量日志：写入 %AppData%\AudioSwitch\logs\audioswitch-yyyyMMdd.log。
/// 修复 L5：Release 版本也需要留痕，便于用户反馈问题时定位。
/// 日志自身失败必须静默，绝不能因为写日志而影响主流程。
/// </summary>
public static class Logger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudioSwitch", "logs");

    private static readonly object _sync = new();

    public static void Error(string message, Exception? ex = null)
    {
        Write("ERROR", ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");
    }

    public static void Warn(string message) => Write("WARN", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(LogDir);
                var file = Path.Combine(LogDir, $"audioswitch-{DateTime.Now:yyyyMMdd}.log");
                var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch
        {
            // 日志失败静默：不能影响主流程
        }
    }
}