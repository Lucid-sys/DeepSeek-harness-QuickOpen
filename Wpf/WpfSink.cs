namespace DshLauncher.Wpf;

/// <summary>
/// 把 <see cref="ILaunchSink"/> 的调用搬到 WPF 的 UI 线程上。
/// 启动流程跑在后台任务里,所以每个回调都必须经 Dispatcher 回到界面线程。
/// </summary>
internal sealed class WpfSink : ILaunchSink
{
    private readonly MainWindow _window;

    public WpfSink(MainWindow window) => _window = window;

    private void Post(Action action)
    {
        var dispatcher = _window.Dispatcher;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    public void StepResult(string label, bool ok, string detail, string? fix = null)
        => Post(() => _window.UiStepResult(label, ok, detail, fix));

    public void StepRunning(string label, string detail = "")
        => Post(() => _window.UiStepRunning(label, detail));

    public void Phase(string title) => Post(() => _window.UiPhase(title));

    public void Info(string text) => Post(() => _window.UiInfo(text));

    public void Note(string text) => Post(() => _window.UiNote(text));

    public void Warn(string text) => Post(() => _window.UiWarn(text));

    public void Error(string text) => Post(() => _window.UiError(text));

    public void Plan(LaunchPlan plan) => Post(() => _window.UiPlan(plan));

    public void ChildOutput(string line, bool isError) => Post(() => _window.UiChildOutput(line, isError));

    public void Ready(string url) => Post(() => _window.UiReady(url));

    public void Completed(string summary, bool stopped, int exitCode)
        => Post(() => _window.UiCompleted(summary, stopped, exitCode));

    public void Fatal(string title, string detail) => Post(() => _window.UiFatal(title, detail));
}
