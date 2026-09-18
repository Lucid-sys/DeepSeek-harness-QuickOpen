using System.Runtime.InteropServices;
using System.Text;

namespace DshLauncher;

internal static class Program
{
    public const string Version = "1.0.0";

    [STAThread]
    private static int Main(string[] rawArgs)
    {
        CrashLog.Hook();

        var opt = Options.Parse(rawArgs, out var parseErrors);

        // --help 永远走控制台:这时还没有窗口,而且帮助文本本来就是给终端看的。
        if (opt.Help)
        {
            ConsoleBootstrap.EnsureConsole();
            Options.PrintHelp();
            return 0;
        }

        var cfg = Options.Resolve(opt, out var resolveErrors);
        var configErrors = parseErrors.Concat(resolveErrors).ToList();

        if (opt.PrintEnv) return Diagnostic.DumpChildEnvironment(cfg, configErrors);

        // ---------- 换机器时的一次性配置 ----------
        // --setup:显式配置,配完就退出(--no-pause 时连提示框都不弹,便于脚本化)。
        if (opt.Setup)
        {
            var picked = SetupFlow.Run(cfg, !opt.NoShortcut, opt.Repo, showSummary: !opt.NoPause);
            return picked is null ? 2 : 0;
        }

        // 新机器上第一次双击:默认检出路径不存在、也没人显式指定过
        // → 直接弹目录选择,配好之后继续走正常启动。
        var repoGivenExplicitly =
            opt.Repo is not null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DSH_REPO"));
        if (!repoGivenExplicitly && !cfg.ConsoleMode && !Directory.Exists(cfg.Repo))
        {
            var picked = SetupFlow.Run(cfg, !opt.NoShortcut, null, showSummary: false);
            if (picked is null) return 2;

            // 配置已经写到 exe 同目录,重新解析一次即可。
            cfg = Options.Resolve(opt, out resolveErrors);
            configErrors = parseErrors.Concat(resolveErrors).ToList();
        }

        if (cfg.ConsoleMode) return ConsoleHost.Run(cfg, configErrors);

        // 默认形态:只弹进度窗口,不出现任何控制台。
        // 与旧控制台等价的那份日志改写到 %LOCALAPPDATA%\DeepSeekHarness\launcher.log
        // (FileLogSink),事后照样能查,而且不受控制台特性的影响。
        // 想要那个最小化的日志窗口时,用 --console-window。
        ConsoleSink? logWindowMirror = null;
        if (cfg.ShowConsoleWindow)
        {
            ConsoleBootstrap.AllocateMinimizedConsole();
            Ui.PauseAllowed = false;                 // 最小化的窗口没人按键,别在这儿等
            logWindowMirror = new ConsoleSink(interactive: false);
        }

        return WpfHost.Run(cfg, configErrors, logWindowMirror);
    }
}

/// <summary>
/// 这是个 WinExe(为了双击不弹黑窗口),所以默认没有控制台。
/// --console 时把调用方的控制台接回来;默认形态则自己开一个并最小化到任务栏。
/// </summary>
internal static class ConsoleBootstrap
{
    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const int SwShowMinNoActive = 7;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>已经有控制台就接上它(终端里跑),否则新建一个。</summary>
    public static void EnsureConsole()
    {
        if (GetConsoleWindow() == IntPtr.Zero)
        {
            if (!AttachConsole(AttachParentProcess)) AllocConsole();
        }

        RebindStdio();
        Ui.Init();
    }

    /// <summary>
    /// 默认形态:新建一个控制台承载运行日志,然后**不激活地最小化**。
    /// 结果是它出现在任务栏里、随时可点开查看完整日志,却不会抢焦点弹到前面。
    /// </summary>
    public static void AllocateMinimizedConsole()
    {
        if (GetConsoleWindow() == IntPtr.Zero && !AllocConsole()) return;

        RebindStdio();
        Ui.Init();

        try
        {
            Console.Title = "DeepSeek Harness 启动器 — 运行日志";
        }
        catch
        {
            // 无控制台时忽略
        }

        var hwnd = GetConsoleWindow();
        if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SwShowMinNoActive);
    }

    private static void RebindStdio()
    {
        // 进程启动时没有控制台,Console.Out 已经绑定到空流;接上之后必须重建。
        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch
        {
            // 极端情况下失败也不该拦住启动
        }
    }
}

/// <summary>--console:老的纯终端形态,保留给调试和自动化。</summary>
internal static class ConsoleHost
{
    public static int Run(LauncherConfig cfg, List<string> configErrors)
    {
        ConsoleBootstrap.EnsureConsole();
        Ui.PauseAllowed = !cfg.NoPause;
        Ui.Banner();

        if (configErrors.Count > 0)
        {
            foreach (var error in configErrors) Ui.Error(error);
            Ui.Fail("配置有误", string.Join("\n", configErrors));
            return 2;
        }

        using var runner = new LaunchRunner(cfg, new ConsoleSink());
        return runner.RunAsync().GetAwaiter().GetResult();
    }
}

/// <summary>默认形态:最小化的日志控制台 + 弹出的 WPF 进度窗口。</summary>
internal static class WpfHost
{
    public static int Run(LauncherConfig cfg, List<string> configErrors, ILaunchSink? mirror = null)
    {
        try
        {
            var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (_, e) =>
            {
                CrashLog.Write("Dispatcher", e.Exception);
                e.Handled = false;
            };

            var window = new Wpf.MainWindow(cfg, configErrors, mirror);
            app.Run(window);
            return window.ExitCode;
        }
        catch (Exception ex)
        {
            // WinExe 没有控制台:不弹一下就只剩"双击没反应"。
            CrashLog.ReportStartupFailure(ex);
            return 9;
        }
    }
}
