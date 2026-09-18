namespace DshLauncher;

/// <summary>
/// 无窗口程序的崩溃可见性。
///
/// WinExe 没有控制台,一旦启动期抛异常,用户只会看到"双击没反应"。
/// 所以这里把异常写进 %LOCALAPPDATA%\DeepSeekHarness\launcher-error.log,
/// 并在启动失败时弹一个消息框指出日志位置。
/// </summary>
internal static class CrashLog
{
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeepSeekHarness",
        "launcher-error.log");

    /// <summary>展示用路径:用占位符,避免把用户名写进界面/截图。</summary>
    public static string LogPathDisplay => @"%LOCALAPPDATA%\DeepSeekHarness\launcher-error.log";

    public static void Hook()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Write("AppDomain", ex);
            else Write("AppDomain", new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("Task", e.Exception);
            e.SetObserved();
        };
    }

    public static void Write(string context, Exception exception)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(dir);
            var text = $"""

                ===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{context}] =====
                {exception}

                """;
            File.AppendAllText(LogPath, text);
        }
        catch
        {
            // 日志写不进去也没别的办法了
        }
    }

    /// <summary>启动彻底失败时的最后一道提示:弹框 + 留下日志。</summary>
    public static void ReportStartupFailure(Exception exception)
    {
        Write("WpfHost", exception);

        try
        {
            System.Windows.MessageBox.Show(
                "启动器界面初始化失败,无法继续。\n\n"
                + exception.GetType().Name + ": " + exception.Message
                + "\n\n详细堆栈已写入:\n" + LogPathDisplay,
                "DeepSeek Harness 启动器错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        catch
        {
            // 连消息框都起不来就只剩日志了
        }
    }
}
