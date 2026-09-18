namespace DshLauncher;

/// <summary>
/// 启动流程向界面汇报进度的通道。
///
/// 启动逻辑(<see cref="LaunchRunner"/>)与界面完全解耦:控制台宿主用
/// <see cref="ConsoleSink"/>,WPF 宿主用 <c>WpfSink</c>。同一套流程因此既能
/// 在窗口里跑,也能用 --console 在终端里跑。
/// </summary>
internal interface ILaunchSink
{
    /// <summary>某项检查/阶段出结果。label 同时充当稳定标识,界面按它更新同一行。</summary>
    void StepResult(string label, bool ok, string detail, string? fix = null);

    /// <summary>某项正在执行(界面显示"进行中")。</summary>
    void StepRunning(string label, string detail = "");

    /// <summary>阶段小节,例如"启动服务"。</summary>
    void Phase(string title);

    void Info(string text);

    void Note(string text);

    void Warn(string text);

    void Error(string text);

    /// <summary>自检通过后的最终启动参数。</summary>
    void Plan(LaunchPlan plan);

    /// <summary>harness 子进程的一行输出。</summary>
    void ChildOutput(string line, bool isError);

    /// <summary>服务已就绪,url 带认证 token。</summary>
    void Ready(string url);

    /// <summary>正常收尾(含"已有实例"这条捷径)。</summary>
    void Completed(string summary, bool stopped, int exitCode);

    /// <summary>致命失败,需要用户读:界面要显眼提示并保留窗口。</summary>
    void Fatal(string title, string detail);
}

/// <summary>把进度打到控制台。等价于旧版的输出形态,保留给 --console 与自动化。</summary>
internal sealed class ConsoleSink : ILaunchSink
{
    /// <summary>
    /// true = 这是唯一的界面,失败/收尾要弹消息框并等待按键。
    /// false = 只是和 WPF 窗口并行的"最小化日志窗口",只管打印,不弹任何东西。
    /// </summary>
    private readonly bool _interactive;

    public ConsoleSink(bool interactive = true) => _interactive = interactive;

    public void StepResult(string label, bool ok, string detail, string? fix = null)
    {
        Ui.Check(label, ok, detail);
        if (!ok && fix is not null) Ui.Info($"  修复:{fix}");
    }

    public void StepRunning(string label, string detail = "")
        => Ui.Info($"{label}…{(string.IsNullOrEmpty(detail) ? string.Empty : " " + detail)}");

    public void Phase(string title)
    {
        Console.WriteLine();
        Ui.Separator();
        Ui.Info(title);
    }

    public void Info(string text) => Ui.Info(text);

    public void Note(string text) => Ui.Warn("注意:" + text);

    public void Warn(string text) => Ui.Warn(text);

    public void Error(string text) => Ui.Error(text);

    public void Plan(LaunchPlan plan)
    {
        Console.WriteLine();
        Ui.WritePlan(
            plan.Repo,
            plan.Cwd,
            plan.Port,
            plan.Host,
            plan.Mode == LaunchMode.Built ? "built(已构建产物)" : "source(源码 + tsx)",
            plan.NodePath,
            plan.NodeVersion);
        // 这里曾经打印一行"网络:直连/走代理"。去掉了:日常输出不需要它,
        // 想确认代理策略时用 --print-env(会完整列出子进程环境)。
    }

    public void ChildOutput(string line, bool isError)
    {
        if (isError) Ui.ChildErr(line);
        else Ui.ChildOut(line);
    }

    public void Ready(string url)
    {
        Console.WriteLine();
        Ui.Separator();
        Ui.Ok($"已就绪:{url}");
    }

    public void Completed(string summary, bool stopped, int exitCode)
    {
        Console.WriteLine();
        Ui.Separator();
        if (stopped) Ui.Info(summary);
        else Ui.Warn(summary);
        if (_interactive) Ui.Done("窗口即将关闭。");
    }

    public void Fatal(string title, string detail)
    {
        if (!_interactive)
        {
            // 并行日志窗口:只打印,弹框交给真正的前台界面。
            Console.WriteLine();
            Ui.Error(title);
            foreach (var line in detail.Split('\n')) Ui.Error("  " + line);
            return;
        }

        Ui.Fail(title, detail);
    }
}

/// <summary>
/// 把同一份进度同时送给多个 sink。
/// 默认形态就是这么用的:控制台(最小化留在任务栏,内容与旧版一致)
/// 和 WPF 进度窗口吃的是同一条流。
/// </summary>
internal sealed class TeeSink : ILaunchSink
{
    private readonly ILaunchSink[] _sinks;

    public TeeSink(params ILaunchSink[] sinks) => _sinks = sinks;

    public void StepResult(string label, bool ok, string detail, string? fix = null)
    {
        foreach (var sink in _sinks) sink.StepResult(label, ok, detail, fix);
    }

    public void StepRunning(string label, string detail = "")
    {
        foreach (var sink in _sinks) sink.StepRunning(label, detail);
    }

    public void Phase(string title)
    {
        foreach (var sink in _sinks) sink.Phase(title);
    }

    public void Info(string text)
    {
        foreach (var sink in _sinks) sink.Info(text);
    }

    public void Note(string text)
    {
        foreach (var sink in _sinks) sink.Note(text);
    }

    public void Warn(string text)
    {
        foreach (var sink in _sinks) sink.Warn(text);
    }

    public void Error(string text)
    {
        foreach (var sink in _sinks) sink.Error(text);
    }

    public void Plan(LaunchPlan plan)
    {
        foreach (var sink in _sinks) sink.Plan(plan);
    }

    public void ChildOutput(string line, bool isError)
    {
        foreach (var sink in _sinks) sink.ChildOutput(line, isError);
    }

    public void Ready(string url)
    {
        foreach (var sink in _sinks) sink.Ready(url);
    }

    public void Completed(string summary, bool stopped, int exitCode)
    {
        foreach (var sink in _sinks) sink.Completed(summary, stopped, exitCode);
    }

    public void Fatal(string title, string detail)
    {
        foreach (var sink in _sinks) sink.Fatal(title, detail);
    }
}
