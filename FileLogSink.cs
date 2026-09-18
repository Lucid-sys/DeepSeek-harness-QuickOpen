namespace DshLauncher;

/// <summary>
/// 把整条进度流追加写到日志文件。
///
/// 有了它,就可以彻底不要那个控制台窗口了 —— 内容依然事后可查,而且不受
/// "最小化的窗口会不会挡路 / 会不会被鼠标选中而阻塞"这些控制台特性的影响。
/// 位置:%LOCALAPPDATA%\DeepSeekHarness\launcher.log
/// </summary>
internal sealed class FileLogSink : ILaunchSink
{
    private const long MaxBytes = 2 * 1024 * 1024;

    private readonly object _gate = new();

    /// <summary>日志文件的真实路径(实际写入用)。</summary>
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeepSeekHarness",
        "launcher.log");

    /// <summary>
    /// 展示用的路径。<b>故意写 %LOCALAPPDATA% 占位符而不是解析后的绝对路径</b> ——
    /// 绝对路径里带着用户名,而这个字符串会出现在界面上(也就出现在别人的截图里)。
    /// </summary>
    public static string LogPathDisplay => @"%LOCALAPPDATA%\DeepSeekHarness\launcher.log";

    public FileLogSink()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(dir);

            // 单次运行一个文件:太大就先清掉,避免无限增长。
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes) File.Delete(LogPath);

            Write($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} 启动 =====");
        }
        catch
        {
            // 写不进日志不该拦住启动
        }
    }

    private void Write(string text)
    {
        try
        {
            lock (_gate) File.AppendAllText(LogPath, text + Environment.NewLine);
        }
        catch
        {
            // 同上
        }
    }

    public void StepResult(string label, bool ok, string detail, string? fix = null)
    {
        Write($"[{(ok ? " OK " : "FAIL")}] {label,-10} {detail}");
        if (!ok && fix is not null) Write($"           修复:{fix}");
    }

    public void StepRunning(string label, string detail = "") => Write($"[ .. ] {label} {detail}".TrimEnd());

    public void Phase(string title) => Write($"\n--- {title} ---");

    public void Info(string text) => Write("       " + text);

    public void Note(string text) => Write("  注意:" + text);

    public void Warn(string text) => Write("  警告:" + text);

    public void Error(string text) => Write("  错误:" + text);

    public void Plan(LaunchPlan plan)
    {
        Write($"       运行方式  {(plan.Mode == LaunchMode.Built ? "built" : "source")}");
        Write($"       仓库      {plan.Repo}");
        Write($"       工作目录  {plan.Cwd}");
        Write($"       监听      {(plan.Host ?? "127.0.0.1")}:{plan.Port}");
        Write($"       Node      {plan.NodeVersion}");
        // 代理策略不在这里打印;--print-env 会完整列出子进程环境。
    }

    public void ChildOutput(string line, bool isError) => Write((isError ? " ! " : " | ") + line);

    public void Ready(string url) => Write("\n已就绪:" + url);

    public void Completed(string summary, bool stopped, int exitCode)
        => Write($"\n===== {DateTime.Now:HH:mm:ss} {summary}(退出码 {exitCode}) =====");

    public void Fatal(string title, string detail)
    {
        Write($"\n!!! {title}");
        foreach (var line in detail.Split('\n')) Write("    " + line);
    }
}
