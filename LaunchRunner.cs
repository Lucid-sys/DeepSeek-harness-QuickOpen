using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace DshLauncher;

/// <summary>
/// 一键启动的完整流程:自检 → 端口探测 → 启动 harness → 等就绪 → 监护。
///
/// 不碰任何界面 API,只通过 <see cref="ILaunchSink"/> 汇报进度,因此同一份逻辑
/// 既能驱动 WPF 窗口,也能用 --console 走终端。所有等待都是异步的,UI 线程不会被卡住。
/// </summary>
internal sealed class LaunchRunner : IDisposable
{
    /// <summary>
    /// DSH Web 应用的就绪信号。源码注释明确这是给 supervisor 用的:
    /// 一旦观察到这一行,路由就挂载完毕、可以连了,而且它带着认证 token。
    /// 形如:  dsh web: http://127.0.0.1:3080/?token=xxx (LAN: http://192.168.1.5:3080/?token=xxx)
    /// </summary>
    private static readonly Regex ReadyLine = new(
        @"^\s*dsh web:\s*(?<url>http\S+)", RegexOptions.Compiled);

    private const int ReadyTimeoutSeconds = 180;

    private readonly LauncherConfig _cfg;
    private readonly ILaunchSink _sink;
    private Process? _child;
    private JobObject? _job;
    private bool _started;
    private volatile bool _stopping;

    public LaunchRunner(LauncherConfig cfg, ILaunchSink sink)
    {
        _cfg = cfg;
        _sink = sink;
    }

    public LaunchPlan? Plan { get; private set; }

    public string? ReadyUrl { get; private set; }

    public bool ServiceRunning => _started && _child is { HasExited: false };

    /// <summary>停止 harness(连带整棵子进程树),供界面上的"停止服务"调用。</summary>
    public void RequestStop()
    {
        _stopping = true;
        try
        {
            if (_started && _child is { HasExited: false }) _child.Kill(entireProcessTree: true);
        }
        catch
        {
            // 已经退出了
        }
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        // ---------- 可选的 pnpm install / build ----------
        if (_cfg.Install || _cfg.Rebuild)
        {
            var pnpm = SelfCheck.FindPnpm(out _);
            if (pnpm is null)
            {
                _sink.Fatal("找不到 pnpm", "请安装 pnpm 11 并确保它在 PATH 上,再重试。");
                return 3;
            }

            if (_cfg.Install && !await RunPnpmAsync(pnpm, "install", ct)) return 3;
            if (_cfg.Rebuild && !await RunPnpmAsync(pnpm, "run build", ct)) return 3;
        }

        // ---------- 自检 ----------
        _sink.Phase("自检");
        var items = new List<CheckItem>();
        var notes = new List<string>();
        var plan = SelfCheck.Run(
            _cfg, items, notes,
            item => _sink.StepResult(item.Label, item.Ok, item.Detail, item.Fix));

        foreach (var note in notes) _sink.Note(note);

        if (plan is null)
        {
            var detail = string.Join("\n", items
                .Where(i => !i.Ok)
                .Select(i => $"· {i.Label}:{i.Detail}" + (i.Fix is null ? string.Empty : $"\n   修复:{i.Fix}")));
            _sink.Fatal("自检未通过,无法启动 DeepSeek Harness", detail);
            return 4;
        }

        Plan = plan;
        _sink.Plan(plan);

        // ---------- 端口探测 ----------
        if (plan.Port > 0)
        {
            _sink.StepRunning("端口", $"探测 {plan.Port}");
            var state = InstanceProbe.Probe(plan.Port, out var detail);

            if (state == PortState.DshRunning)
            {
                _sink.StepResult("端口", true, $"{plan.Port} 上已有 DSH 实例,复用它");
                var url = RememberedUrl.TryRead(plan.Port) ?? $"http://127.0.0.1:{plan.Port}/";
                ReadyUrl = url;
                _sink.Info($"地址:{url}");
                if (plan.NoBrowser) _sink.Info("--no-browser 已指定,请手动打开上面的地址。");
                else if (OpenBrowser(url)) _sink.Info("已在浏览器中打开。");
                _sink.Completed("已交给正在运行的实例,没有启动第二个。", false, 0);
                return 0;
            }

            if (state == PortState.Occupied)
            {
                _sink.StepResult("端口", false, $"{plan.Port} 被其他程序占用({detail})");
                _sink.Fatal(
                    $"端口 {plan.Port} 被占用",
                    $"探测结果:{detail}\n\n换一个端口再试,例如:\n    启动DSH.exe --port 3099");
                return 5;
            }

            _sink.StepResult("端口", true, $"{plan.Port} 空闲");
        }

        return await StartAndSuperviseAsync(plan, ct);
    }

    private async Task<int> StartAndSuperviseAsync(LaunchPlan plan, CancellationToken ct)
    {
        var args = BuildArguments(plan);
        var psi = BuildStartInfo(plan);

        _sink.Phase("启动服务");
        _sink.StepRunning("启动", string.Join(' ', args));

        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tail = new Queue<string>();
        var gate = new object();

        void OnLine(string? line, bool isError)
        {
            if (line is null) return;
            lock (gate)
            {
                tail.Enqueue(line);
                while (tail.Count > 80) tail.Dequeue();
            }
            _sink.ChildOutput(line, isError);
            var match = ReadyLine.Match(line);
            if (match.Success) ready.TrySetResult(match.Groups["url"].Value);
        }

        var child = new Process { StartInfo = psi, EnableRaisingEvents = true };
        child.OutputDataReceived += (_, e) => OnLine(e.Data, isError: false);
        child.ErrorDataReceived += (_, e) => OnLine(e.Data, isError: true);

        try
        {
            child.Start();
        }
        catch (Exception ex)
        {
            _sink.Fatal("无法启动 DSH 进程", $"{plan.NodePath}\n{ex.Message}");
            return 6;
        }

        _child = child;
        _started = true;

        // 绑进作业对象:启动器一死,内核连带干掉整棵子进程树。
        _job = new JobObject();
        if (!_job.TryCreateAndAssign(child))
            _sink.Warn("未能把子进程绑定到作业对象;退出时会改用 taskkill 兜底清理。");

        child.BeginOutputReadLine();
        child.BeginErrorReadLine();

        var exitTask = child.WaitForExitAsync(CancellationToken.None);
        // 超时不参与取消:停止请求是靠杀掉子进程让 exitTask 先完成来表达的。
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(ReadyTimeoutSeconds), CancellationToken.None);
        var winner = await Task.WhenAny(ready.Task, exitTask, timeoutTask);

        if (winner != ready.Task)
        {
            var wasTimeout = winner == timeoutTask;
            if (wasTimeout) RequestStop();

            string[] lines;
            lock (gate) lines = tail.ToArray();

            if (_stopping)
            {
                _sink.StepResult("启动", false, "已取消");
                _sink.Completed("已取消,DeepSeek Harness 没有启动。", true, 0);
                return 0;
            }

            _sink.StepResult("启动", false, wasTimeout ? $"等待 {ReadyTimeoutSeconds} 秒仍未就绪" : $"进程在就绪前退出(退出码 {child.ExitCode})");
            _sink.Fatal(
                "DeepSeek Harness 启动失败",
                (wasTimeout
                    ? $"{ReadyTimeoutSeconds} 秒内没有出现就绪信号。"
                    : $"进程在就绪前退出,退出码 {child.ExitCode}。")
                + "\n\n最后几行输出:\n" + string.Join("\n", lines));
            return 7;
        }

        var url = ready.Task.Result;
        ReadyUrl = url;
        _sink.StepResult("启动", true, "服务已就绪");
        _sink.Ready(url);

        if (!plan.NoRemember) RememberedUrl.Write(plan.Port, url);
        if (plan.NoBrowser) _sink.Info("--no-browser 已指定,请手动把上面的地址复制到浏览器。");
        else if (OpenBrowser(url)) _sink.Info("已在浏览器中打开。");
        else _sink.Warn("没能自动打开浏览器,请手动把上面的地址复制到浏览器。");

        _sink.Info("服务运行中。可以最小化到托盘,服务会继续跑。");

        await exitTask;

        var code = child.ExitCode;
        var summary = _stopping
            ? "DeepSeek Harness 已停止。"
            : $"DSH 进程已退出(退出码 {code})。";
        _sink.Completed(summary, _stopping, code);
        return _stopping || code == 0 ? 0 : 8;
    }

    /// <summary>
    /// 构造 harness 子进程的启动信息。抽成独立方法是为了让 --print-env 诊断
    /// 走的是**同一条**代码路径,而不是另写一份可能跑偏的说明。
    /// </summary>
    public static ProcessStartInfo BuildStartInfo(LaunchPlan plan)
    {
        var psi = new ProcessStartInfo
        {
            FileName = plan.NodePath,
            WorkingDirectory = plan.Cwd,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            CreateNoWindow = true,
        };

        foreach (var arg in BuildArguments(plan)) psi.ArgumentList.Add(arg);
        SelfCheck.PrependNodeToPath(psi, plan.NodePath);
        SelfCheck.ApplyProxyPolicy(psi, plan);
        return psi;
    }

    private static List<string> BuildArguments(LaunchPlan plan)
    {
        var args = new List<string>();
        if (plan.Mode == LaunchMode.Source)
        {
            // 源码模式必须显式给出 tsx 的 file:// 加载器地址 —— Windows 上 --import
            // 不接受裸绝对路径,而且这样即使 cwd 不是仓库根也能解析到 tsx。
            args.Add("--import");
            args.Add(plan.LoaderUri!);
            args.Add(plan.SourceEntry!);
        }
        else
        {
            args.Add(plan.BuiltEntry!);
        }

        args.Add("web");
        // 浏览器由我们自己开:要先看到带 token 的地址,才能保证免登录进 GUI。
        args.Add("--no-open");
        if (plan.Port > 0)
        {
            args.Add("--port");
            args.Add(plan.Port.ToString());
        }
        if (!string.IsNullOrWhiteSpace(plan.Host))
        {
            args.Add("--host");
            args.Add(plan.Host!);
        }

        return args;
    }

    /// <summary>跑 pnpm,把输出也转给界面(窗口模式下没有控制台可看)。</summary>
    private async Task<bool> RunPnpmAsync(string pnpmPath, string verb, CancellationToken ct)
    {
        _sink.Phase($"pnpm {verb}");
        _sink.StepRunning($"pnpm {verb}", _cfg.Repo);

        var psi = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = $"/d /s /c \"\"{pnpmPath}\" {verb}\"",
            WorkingDirectory = _cfg.Repo,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                _sink.StepResult($"pnpm {verb}", false, "无法启动进程");
                return false;
            }

            process.OutputDataReceived += (_, e) => { if (e.Data is not null) _sink.ChildOutput(e.Data, false); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) _sink.ChildOutput(e.Data, true); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);

            var ok = process.ExitCode == 0;
            _sink.StepResult($"pnpm {verb}", ok, ok ? "完成" : $"失败(退出码 {process.ExitCode})");
            return ok;
        }
        catch (Exception ex)
        {
            _sink.StepResult($"pnpm {verb}", false, ex.Message);
            return false;
        }
    }

    /// <summary>用系统默认浏览器打开地址;失败返回 false(服务继续跑,让用户手动复制)。</summary>
    public static bool OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _job?.Dispose();
        _child?.Dispose();
    }
}
