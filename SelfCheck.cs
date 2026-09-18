using System.Diagnostics;
using System.Text;

namespace DshLauncher;

internal sealed record CheckItem(string Label, bool Ok, bool Fatal, string Detail, string? Fix);

/// <summary>命令行 + 环境变量 + 配置文件合成后的运行配置。</summary>
internal sealed class LauncherConfig
{
    public string Repo = string.Empty;
    public string Workspace = string.Empty;
    public int Port = 3080;
    public string? Host;
    public LaunchMode Mode = LaunchMode.Auto;
    public bool NoBrowser;
    public bool NoRemember;
    public bool Install;
    public bool Rebuild;
    /// <summary>用控制台界面而不是 WPF 窗口。</summary>
    public bool ConsoleMode;
    /// <summary>出错/结束时不要等待按键(仅控制台形态有意义)。</summary>
    public bool NoPause;
    /// <summary>显式指定的出站代理;为空 = 直连。</summary>
    public string? Proxy;
    /// <summary>沿袭当前环境里的代理变量,不做清理。</summary>
    public bool KeepEnvProxy;
    /// <summary>额外开一个最小化到任务栏的日志控制台(--console-window;默认关,完全不弹窗口)。</summary>
    public bool ShowConsoleWindow;
}

/// <summary>自检通过后,真正用于启动子进程的计划。</summary>
internal sealed class LaunchPlan
{
    public string Repo = string.Empty;
    /// <summary>子进程的工作目录 —— 也就是 DSH 的 workspace root。</summary>
    public string Cwd = string.Empty;
    public int Port;
    public string? Host;
    public LaunchMode Mode;
    public string NodePath = string.Empty;
    public string NodeVersion = string.Empty;
    public string? BuiltEntry;
    public string? LoaderUri;
    public string? SourceEntry;
    public bool NoBrowser;
    public bool NoRemember;
    public string? Proxy;
    public bool KeepEnvProxy;
}

internal static class SelfCheck
{
    private const string FixInstall = "在仓库目录执行:pnpm install";
    private const string FixBuild = "在仓库目录执行:pnpm run build";

    /// <summary>
    /// 跑完整自检。
    /// </summary>
    /// <param name="cfg">合成后的运行配置。</param>
    /// <param name="items">收齐所有检查结果(供失败时汇总)。</param>
    /// <param name="notes">非致命的提醒。</param>
    /// <param name="onStepResult">每完成一项就回调一次,用来驱动实时进度界面。</param>
    /// <returns>全部通过时返回启动计划,否则 null。</returns>
    public static LaunchPlan? Run(
        LauncherConfig cfg,
        List<CheckItem> items,
        List<string> notes,
        Action<CheckItem>? onStepResult = null)
    {
        var fatal = false;

        void Add(CheckItem item)
        {
            items.Add(item);
            onStepResult?.Invoke(item);
            if (!item.Ok && item.Fatal) fatal = true;
        }

        // ---------- 1. 仓库形态 ----------
        var pkgJson = Path.Combine(cfg.Repo, "package.json");
        var sourceEntry = Path.Combine(cfg.Repo, "apps", "cli", "src", "bin.ts");
        var repoOk = File.Exists(pkgJson) && File.Exists(sourceEntry);
        Add(new CheckItem("仓库", repoOk, true,
            repoOk ? cfg.Repo : $"这里不像 deepseek-harness 检出(缺 package.json 或 apps/cli/src/bin.ts):{cfg.Repo}",
            repoOk ? null : "用 --repo <目录> 指向正确的检出目录"));

        // ---------- 2. Node ----------
        var node = FindNode(out var nodeHow);
        Add(new CheckItem("Node", node is not null, true,
            node is not null ? $"{node}(来自 {nodeHow})" : "PATH、nvm 目录和 Program Files 里都没找到 node.exe",
            node is not null ? null : "安装 Node.js 22.19+ 或 24+ 后重新打开本程序"));

        var nodeVersion = string.Empty;
        if (node is not null)
        {
            var probe = RunCapture(node, ["--version"]);
            nodeVersion = probe.Stdout.Trim();
            var versionOk = TryCheckNodeVersion(nodeVersion, out var reason);
            Add(new CheckItem("Node 版本", versionOk, true,
                string.IsNullOrEmpty(nodeVersion) ? $"无法读取 node --version:{probe.Stderr.Trim()}" : $"{nodeVersion} — {reason}",
                versionOk ? null : "本项目要求 Node ^22.19.0 || >=24.0.0(23.x 不在支持范围内)"));
        }

        // ---------- 3. 依赖 ----------
        var storeOk = Directory.Exists(Path.Combine(cfg.Repo, "node_modules", ".pnpm"));
        Add(new CheckItem("依赖", storeOk, true,
            storeOk ? @"存在 node_modules\.pnpm" : @"缺少 node_modules\.pnpm,依赖尚未安装",
            storeOk ? null : FixInstall));

        // ---------- 4. 运行方式与构建产物 ----------
        var builtEntry = Path.Combine(cfg.Repo, "apps", "cli", "lib", "bin.js");
        var distIndex = Path.Combine(cfg.Repo, "apps", "web", "dist", "index.html");
        var loaderPath = Path.Combine(cfg.Repo, "node_modules", "tsx", "dist", "esm", "index.mjs");

        var builtReady = File.Exists(builtEntry) && File.Exists(distIndex);
        var sourceReady = File.Exists(loaderPath) && File.Exists(sourceEntry);

        var mode = cfg.Mode switch
        {
            LaunchMode.Built => LaunchMode.Built,
            LaunchMode.Source => LaunchMode.Source,
            _ => builtReady ? LaunchMode.Built : LaunchMode.Source,
        };

        if (mode == LaunchMode.Built)
        {
            Add(new CheckItem("CLI 入口", File.Exists(builtEntry), true,
                File.Exists(builtEntry) ? builtEntry : "缺少 apps/cli/lib/bin.js",
                File.Exists(builtEntry) ? null : FixBuild));
            Add(new CheckItem("前端产物", File.Exists(distIndex), true,
                File.Exists(distIndex) ? distIndex : "缺少 apps/web/dist/index.html",
                File.Exists(distIndex) ? null : FixBuild));
        }
        else
        {
            Add(new CheckItem("tsx 加载器", File.Exists(loaderPath), true,
                File.Exists(loaderPath) ? loaderPath : @"缺少 node_modules\tsx(tsx 是根 devDependency)",
                File.Exists(loaderPath) ? null : FixInstall));
            Add(new CheckItem("源码入口", File.Exists(sourceEntry), true,
                File.Exists(sourceEntry) ? sourceEntry : "缺少 apps/cli/src/bin.ts",
                File.Exists(sourceEntry) ? null : FixInstall));
        }

        // ---------- 5. 工作目录 ----------
        var workspaceOk = Directory.Exists(cfg.Workspace);
        Add(new CheckItem("工作目录", workspaceOk, true,
            workspaceOk ? cfg.Workspace : $"目录不存在:{cfg.Workspace}",
            workspaceOk ? null : "用 --workspace <目录> 指定,或把文件夹拖到 exe 上"));

        // ---------- 6. pnpm(仅 --install / --rebuild 需要,非致命)----------
        var pnpm = FindPnpm(out var pnpmHow);
        Add(new CheckItem("pnpm", pnpm is not null, false,
            pnpm is not null ? $"{pnpm}(来自 {pnpmHow})" : "未找到(只有 --install / --rebuild 需要它)",
            null));

        if (fatal) return null;

        // ---------- 组装计划 ----------
        var cwd = cfg.Workspace;
        if (mode == LaunchMode.Source && !PathEquals(cwd, cfg.Repo))
        {
            // 源码模式的模块解析依赖仓库根的 tsconfig paths,所以 cwd 必须是仓库根。
            notes.Add($"源码模式(source)必须从仓库根启动,本次工作目录改用 {cfg.Repo}。");
            notes.Add("要指定别的工作目录,请用 --mode built(已构建产物与 cwd 无关),或在 GUI 里切换项目。");
            cwd = cfg.Repo;
        }

        return new LaunchPlan
        {
            Repo = cfg.Repo,
            Cwd = cwd,
            Port = cfg.Port,
            Host = cfg.Host,
            Mode = mode,
            NodePath = node!,
            NodeVersion = nodeVersion,
            BuiltEntry = builtEntry,
            LoaderUri = new Uri(loaderPath).AbsoluteUri,
            SourceEntry = sourceEntry,
            NoBrowser = cfg.NoBrowser,
            NoRemember = cfg.NoRemember,
            Proxy = cfg.Proxy,
            KeepEnvProxy = cfg.KeepEnvProxy,
        };
    }

    /// <summary>
    /// 决定 harness 子进程看到的代理环境。
    ///
    /// 默认是**直连**:把 DSH 会读的所有代理变量全部摘掉。这不改动用户自己的任何设置
    /// (User 级的 HTTP_PROXY、Clash Verge 的系统代理都原封不动),只是不给 harness。
    /// 原因:DSH 的 http-proxy 包会把 HTTP_PROXY/HTTPS_PROXY 装进自己进程的全局 dispatcher,
    /// 于是所有出站流量(含模型 API)都会走那个代理 —— 用户环境里恰好有 Clash 时就被"绑"上了。
    /// </summary>
    public static void ApplyProxyPolicy(ProcessStartInfo psi, LaunchPlan plan)
    {
        // Windows 上环境变量名不区分大小写,所以按键名扫描删除,不依赖字典的比较器。
        var proxyNames = new[]
        {
            "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY",
            "http_proxy", "https_proxy", "all_proxy", "no_proxy",
            "NODE_USE_ENV_PROXY",
        };

        if (!plan.KeepEnvProxy)
        {
            foreach (var key in psi.Environment.Keys.ToArray())
            {
                if (proxyNames.Any(n => string.Equals(n, key, StringComparison.OrdinalIgnoreCase)))
                    psi.Environment.Remove(key);
            }
        }

        // --proxy:显式指定时才把代理交回给 harness,并保证回环地址绕过它,
        // 否则连本机 GUI 的连接都会被送去代理。
        if (!string.IsNullOrWhiteSpace(plan.Proxy))
        {
            psi.Environment["HTTP_PROXY"] = plan.Proxy;
            psi.Environment["HTTPS_PROXY"] = plan.Proxy;
            psi.Environment["ALL_PROXY"] = plan.Proxy;
            psi.Environment["NO_PROXY"] = "localhost,127.0.0.1,::1,[::1]";
        }
    }

    /// <summary>把 node 所在目录提到子进程 PATH 最前面,保证 DSH 内部再调 node 时也找得到。</summary>
    public static void PrependNodeToPath(ProcessStartInfo psi, string nodePath)
    {
        var dir = Path.GetDirectoryName(nodePath);
        if (string.IsNullOrEmpty(dir)) return;
        var current = psi.Environment.TryGetValue("PATH", out var value)
            ? value
            : Environment.GetEnvironmentVariable("PATH");
        psi.Environment["PATH"] = dir + Path.PathSeparator + (current ?? string.Empty);
    }

    public static string? FindNode(out string how)
    {
        var onPath = FindOnPath("node.exe");
        if (onPath is not null)
        {
            how = "PATH";
            return onPath;
        }

        // nvm-for-windows 装到 %LOCALAPPDATA%\Author Software\nvm\installs\<version>\node.exe
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var nvmRoot = Path.Combine(localAppData, "Author Software", "nvm", "installs");
        if (Directory.Exists(nvmRoot))
        {
            var candidate = Directory.GetDirectories(nvmRoot)
                .OrderByDescending(d => ParseSemver(Path.GetFileName(d)) ?? new Version(0, 0))
                .Select(d => Path.Combine(d, "node.exe"))
                .FirstOrDefault(File.Exists);
            if (candidate is not null)
            {
                how = "nvm";
                return candidate;
            }
        }

        var programFiles = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
        if (File.Exists(programFiles))
        {
            how = "Program Files";
            return programFiles;
        }

        how = string.Empty;
        return null;
    }

    public static string? FindPnpm(out string how)
    {
        var onPath = FindOnPath("pnpm.cmd") ?? FindOnPath("pnpm.exe");
        if (onPath is not null)
        {
            how = "PATH";
            return onPath;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var nvmPnpm = Path.Combine(localAppData, "Author Software", "nvm", ".nodejs", "pnpm.exe");
        if (File.Exists(nvmPnpm))
        {
            how = "nvm";
            return nvmPnpm;
        }

        how = string.Empty;
        return null;
    }

    private static string? FindOnPath(string exeName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        foreach (var entry in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(entry.Trim().Trim('"'), exeName);
            }
            catch
            {
                continue;
            }
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>engines: ^22.19.0 || >=24.0.0</summary>
    private static bool TryCheckNodeVersion(string raw, out string reason)
    {
        var parsed = ParseSemver(raw);
        if (parsed is null)
        {
            reason = "无法解析版本号";
            return false;
        }

        var ok = parsed.Major >= 24 || (parsed.Major == 22 && parsed.Minor >= 19);
        reason = ok ? "满足要求" : "不满足要求";
        return ok;
    }

    private static Version? ParseSemver(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim().TrimStart('v', 'V');
        var core = text.Split('-', '+')[0];
        var parts = core.Split('.');
        if (parts.Length < 2) return null;
        if (!int.TryParse(parts[0], out var major)) return null;
        if (!int.TryParse(parts[1], out var minor)) return null;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;
        return new Version(major, minor, patch);
    }

    private static bool PathEquals(string a, string b)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>跑一个短命令并捕获输出(自检用,不接触控制台)。</summary>
    public static (int Code, string Stdout, string Stderr) RunCapture(
        string exe, string[] args, string? cwd = null, int timeoutMs = 20000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        if (cwd is not null) psi.WorkingDirectory = cwd;

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return (-1, string.Empty, "无法启动进程");

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* 已退出 */ }
                process.WaitForExit(5000);
            }
            return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}
