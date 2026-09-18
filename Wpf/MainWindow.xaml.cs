using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DshLauncher.Wpf;

/// <summary>
/// 静默启动的进度窗口:取代原来的 cmd 黑窗,内容与原控制台输出等价(自检逐项 +
/// harness 子进程日志),另外多了一个进度条和几个操作按钮。
///
/// 界面更新全部走 <see cref="WpfSink"/>,它把后台线程的回调搬到 Dispatcher 上。
/// </summary>
public partial class MainWindow : Window
{
    private const int MaxLogRows = 600;
    private const double ProgressCeiling = 0.85;

    private readonly LauncherConfig _cfg;
    private readonly List<string> _configErrors;
    /// <summary>并行的输出目标(默认是那个最小化的日志控制台),让两份内容完全同源。</summary>
    private readonly ILaunchSink? _mirror;
    private readonly ObservableCollection<StepRow> _steps = new();
    private readonly ObservableCollection<LogRow> _logs = new();
    private readonly Dictionary<string, StepRow> _stepIndex = new(StringComparer.Ordinal);

    private LaunchRunner? _runner;
    private TrayIcon? _tray;
    private bool _failed;
    private bool _hideToTray;
    private double _progress;
    private bool _waiting;

    public int ExitCode { get; private set; }

    /// <summary>
    /// 构造函数是 internal:XAML 生成的 partial 类要求类型是 public,
    /// 但参数类型 LauncherConfig 是 internal,所以入口留在程序集内部。
    /// </summary>
    internal MainWindow(LauncherConfig cfg, List<string> configErrors, ILaunchSink? mirror = null)
    {
        _cfg = cfg;
        _configErrors = configErrors;
        _mirror = mirror;

        InitializeComponent();
        StepsList.ItemsSource = _steps;
        LogList.ItemsSource = _logs;

        Loaded += OnLoaded;
        Closing += OnClosing;

        // 资源 URI 写错时 WPF 是**静默**失败的(窗口照常显示,图就是不出来),
        // 所以这里必须有人喊一声,否则只能靠数像素才发现。
        LogoImage.ImageFailed += (_, e) => AddLog(
            "品牌标记加载失败:" + (e.ErrorException?.Message ?? e.ErrorException?.GetType().Name ?? "unknown"),
            Palette.Error);

        SubtitleText.Text = cfg.ConsoleMode ? "控制台模式" : "正在启动…";
        AddLog($"DeepSeek Harness 一键启动器 v{Program.Version}", Palette.Accent);
        AddLog($"工作目录  {cfg.Workspace}", Palette.LogText);
        AddLog($"仓库      {cfg.Repo}", Palette.LogText);
        AddLog($"监听      127.0.0.1:{cfg.Port}", Palette.LogText);
        // 没有控制台窗口了,至少让人知道日志落在哪儿。
        // 用 %LOCALAPPDATA% 占位符:展开后的绝对路径里带着用户名,不该出现在界面上。
        AddLog($"日志文件  {FileLogSink.LogPathDisplay}", Palette.Dim);
        AddLog(string.Empty, Palette.LogText);
    }

    // ================= 生命周期 =================

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();

        if (_configErrors.Count > 0)
        {
            UiFatal("配置有误", string.Join("\n", _configErrors));
            return;
        }

        // 进度流同时写进日志文件:这是那个最小化控制台的替代品,
        // 让内容事后依然可查(尤其失败时)。
        var sinks = new List<ILaunchSink> { new WpfSink(this), new FileLogSink() };
        if (_mirror is not null) sinks.Add(_mirror);
        _runner = new LaunchRunner(_cfg, sinks.Count == 1 ? sinks[0] : new TeeSink(sinks.ToArray()));
        try
        {
            // 放后台线程:自检里有若干进程调用,不能让界面卡住。
            ExitCode = await Task.Run(async () => await _runner.RunAsync());
        }
        catch (Exception ex)
        {
            UiFatal("启动器内部错误", ex.ToString());
            ExitCode = 9;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_hideToTray) return;

        // 服务还在跑就收进托盘 —— 别让"关窗口"顺手把服务杀掉。
        if (_runner is { ServiceRunning: true })
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        Shutdown();
    }

    private void HideToTray()
    {
        _tray ??= new TrayIcon(OpenGui, ShowFromTray, StopAndExit);
        ShowInTaskbar = false;
        Hide();
        _tray.ShowBalloon(
            "DeepSeek Harness 仍在运行",
            "双击托盘图标可以重新打开这个窗口;右键菜单里可以停止服务。");
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void StopAndExit()
    {
        _hideToTray = true;
        if (_runner is { } running)
        {
            running.RequestStop();
            for (var i = 0; i < 20 && running.ServiceRunning; i++) Thread.Sleep(50);
        }
        Shutdown();
    }

    private void Shutdown()
    {
        _hideToTray = true;
        _tray?.Dispose();
        _tray = null;

        if (_runner is { } runner)
        {
            runner.RequestStop();
            // 等一下端口真正释放,避免立刻重开时撞上"端口被占用"。
            for (var i = 0; i < 20 && runner.ServiceRunning; i++) Thread.Sleep(50);
            runner.Dispose();
        }

        System.Windows.Application.Current?.Shutdown();
    }

    // ================= 交互 =================

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Open_Click(object sender, RoutedEventArgs e) => OpenGui();

    private void OpenGui()
    {
        if (_runner?.ReadyUrl is { Length: > 0 } url && !LaunchRunner.OpenBrowser(url))
            UiWarn("没能打开浏览器,请手动复制地址:" + url);
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        SubtitleText.Text = "正在停止…";
        StatusText.Text = "正在停止 DeepSeek Harness…";
        _runner?.RequestStop();
    }

    // ================= 由 WpfSink 调用的界面更新(都在 UI 线程上)=================

    internal void UiStepRunning(string label, string detail = "")
    {
        var row = EnsureStep(label);
        row.Glyph = "●";
        row.Brush = Palette.Accent;
        row.Detail = detail;
        SetWaiting(true);
    }

    internal void UiStepResult(string label, bool ok, string detail, string? fix)
    {
        var row = EnsureStep(label);
        row.Glyph = ok ? "✓" : "✕";
        row.Brush = ok ? Palette.Ok : Palette.Error;
        row.Detail = detail;

        if (!ok && fix is not null) AddLog("   修复:" + fix, Palette.Warn);

        SetWaiting(false);
        RecomputeProgress();
    }

    internal void UiPhase(string title)
    {
        SubtitleText.Text = title + "…";
        AddLog(string.Empty, Palette.LogText);
        AddLog("── " + title + " " + new string('─', Math.Max(0, 46 - title.Length)), Palette.LogLauncher);
    }

    internal void UiInfo(string text) => AddLog("   " + text, Palette.LogText);

    internal void UiNote(string text) => AddLog("   注意:" + text, Palette.Warn);

    internal void UiWarn(string text) => AddLog("   " + text, Palette.Warn);

    internal void UiError(string text) => AddLog("   " + text, Palette.Error);

    internal void UiPlan(LaunchPlan plan)
    {
        AddLog(string.Empty, Palette.LogText);
        AddLog($"   运行方式  {(plan.Mode == LaunchMode.Built ? "built(已构建产物)" : "source(源码 + tsx)")}", Palette.LogText);
        AddLog($"   仓库      {plan.Repo}", Palette.LogText);
        AddLog($"   工作目录  {plan.Cwd}", Palette.LogText);
        AddLog($"   监听      {(plan.Host ?? "127.0.0.1")}:{plan.Port}", Palette.LogText);
        AddLog($"   Node      {plan.NodeVersion}", Palette.LogText);
    }

    internal void UiChildOutput(string line, bool isError)
        => AddLog((isError ? " ! " : " | ") + line, isError ? Palette.LogError : Palette.LogText);

    internal void UiReady(string url)
    {
        SetWaiting(false);
        SetProgress(1);
        SubtitleText.Text = "服务已就绪,正在运行";
        StatusText.Text = "服务运行中 · 关窗口会最小化到托盘";
        OpenButton.IsEnabled = true;
        StopButton.IsEnabled = true;

        var step = EnsureStep("就绪");
        step.Glyph = "✓";
        step.Brush = Palette.Ok;
        step.Detail = url;

        AddLog(string.Empty, Palette.LogText);
        AddLog("✓ 已就绪:" + url, Palette.Ok);
    }

    internal void UiCompleted(string summary, bool stopped, int exitCode)
    {
        SetWaiting(false);
        StopButton.IsEnabled = false;
        OpenButton.IsEnabled = exitCode == 0 && !stopped && !_failed;

        SubtitleText.Text = _failed ? "启动失败" : stopped ? "已停止" : "服务已退出";
        StatusText.Text = summary;

        AddLog(string.Empty, Palette.LogText);
        AddLog(summary, stopped ? Palette.Warn : Palette.Error);

        // 用户主动点了"停止服务":服务确认停好后这个界面就没用了,自己关掉。
        // 这里能确信服务真的停了 —— Completed 是 LaunchRunner 在
        // `await child.WaitForExitAsync()` 之后才发出来的。
        if (stopped && !_failed) CloseAfterStop();
    }

    /// <summary>留一点时间让用户看到"已停止",然后关闭窗口(=> 退出整个程序)。</summary>
    private void CloseAfterStop()
    {
        StatusText.Text = "服务已停止 · 立即关闭窗口";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
        };
        timer.Start();
    }

    internal void UiFatal(string title, string detail)
    {
        _failed = true;
        SetWaiting(false);
        StopButton.IsEnabled = false;
        OpenButton.IsEnabled = false;

        ErrorPanel.Visibility = Visibility.Visible;
        ErrorTitleText.Text = title;
        ErrorDetailText.Text = detail;

        SubtitleText.Text = "启动失败";
        StatusText.Text = "失败 · 修好后重新双击即可";

        AddLog(string.Empty, Palette.LogText);
        AddLog("✕ " + title, Palette.Error);
        foreach (var line in detail.Split('\n')) AddLog("   " + line, Palette.LogError);

        Activate();
    }

    // ================= 内部工具 =================


    private StepRow EnsureStep(string label)
    {
        if (_stepIndex.TryGetValue(label, out var existing)) return existing;

        var row = new StepRow(label);
        _stepIndex[label] = row;
        _steps.Add(row);
        return row;
    }

    private void AddLog(string text, Brush brush)
    {
        _logs.Add(new LogRow(text, brush));
        while (_logs.Count > MaxLogRows) _logs.RemoveAt(0);

        // 等布局跑完再滚,否则新行还没量出高度,滚不到底。
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => LogScroll.ScrollToEnd()));
    }

    private void RecomputeProgress()
    {
        // 自检 + 端口 + 启动大致十来项;就绪时才跳到 100%。
        var total = Math.Max(10, _steps.Count + 2);
        SetProgress(Math.Min(ProgressCeiling, _steps.Count / (double)total));
    }

    private void SetProgress(double value)
    {
        _progress = Math.Clamp(value, 0, 1);
        if (!_waiting) ApplyProgress();
    }

    /// <summary>true = 有事情在飞行中,进度条改为来回扫动;false = 显示确定进度。</summary>
    private void SetWaiting(bool waiting)
    {
        _waiting = waiting;
        ApplyProgress();
    }

    private void ApplyProgress()
    {
        var width = TrackHost.ActualWidth;
        if (width <= 0) return;

        if (_waiting)
        {
            // 扫动:指示条从左侧外滑入、右侧外滑出。TrackHost 设了 ClipToBounds,
            // 所以超出轨道的部分会被裁掉,不会画到卡片外面。
            var sweepWidth = Math.Max(56, width * 0.3);
            Indicator.Width = sweepWidth;
            IndicatorTransform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(-sweepWidth, width, new Duration(TimeSpan.FromMilliseconds(1300)))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
        }
        else
        {
            IndicatorTransform.BeginAnimation(TranslateTransform.XProperty, null);
            IndicatorTransform.X = 0;
            // 刚开始时给一个最小可见宽度,否则进度条看着像空的。
            Indicator.Width = _progress <= 0 ? 0 : Math.Max(6, width * _progress);
        }
    }

    private void TrackHost_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyProgress();
}
