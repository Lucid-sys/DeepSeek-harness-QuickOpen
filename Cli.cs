using System.Text.Json;

namespace DshLauncher;

internal enum LaunchMode
{
    /// <summary>优先用已构建的 CLI 入口(cwd 无关,可选任意工作区),缺失时退回源码模式。</summary>
    Auto,
    /// <summary>apps/cli/lib/bin.js —— 已构建形态,等价于"已安装形态",启动快且与工作区无关。</summary>
    Built,
    /// <summary>apps/cli/src/bin.ts + tsx —— 等价于 pnpm dsh web,必须从仓库根启动(tsconfig paths)。</summary>
    Source,
}

internal sealed class Options
{
    public string? Repo;
    public string? Workspace;
    public int? Port;
    public string? Host;
    public LaunchMode Mode = LaunchMode.Auto;
    public bool NoBrowser;
    public bool NoRemember;
    public bool Rebuild;
    public bool Install;
    public bool NoPause;
    public bool Help;

    /// <summary>强制走控制台界面(默认是 WPF 窗口);调试和自动化用。</summary>
    public bool ConsoleMode;

    /// <summary>--proxy:显式指定出站代理;给出后 harness 会走它。</summary>
    public string? Proxy;

    /// <summary>--keep-env-proxy:沿袭当前环境里的 HTTP_PROXY 等变量,不做清理。</summary>
    public bool KeepEnvProxy;

    /// <summary>--print-env:打印 harness 子进程将看到的环境与命令行,然后退出。</summary>
    public bool PrintEnv;

    /// <summary>--no-console-window:不要那个日志控制台(现在这是默认行为,写出便于自我说明)。</summary>
    public bool NoConsoleWindow;

    /// <summary>--console-window:反过来 —— 额外开一个最小化的日志控制台。</summary>
    public bool ConsoleWindow;

    /// <summary>--setup:只做一次性配置(选目录、写 dsh-launcher.json、建快捷方式)然后退出。</summary>
    public bool Setup;

    /// <summary>--no-shortcut:配置时不要建桌面快捷方式。</summary>
    public bool NoShortcut;

    /// <summary>
    /// 检出目录的默认猜测:用户主目录下的 deepseek-harness。
    /// 它只是"猜",不是要求 —— 目录不存在时会走 <see cref="SetupFlow"/> 让用户选,
    /// 所以这里绝不能写死任何一台机器的绝对路径。
    /// </summary>
    public static string DefaultRepo => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "deepseek-harness");

    public static Options Parse(string[] args, out List<string> errors)
    {
        var opt = new Options();
        errors = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;

            // 拖放到 exe 上的路径可能带引号
            var token = raw.Trim();
            if (token.Length >= 2 && token[0] == '"' && token[^1] == '"') token = token[1..^1];
            if (token.Length == 0) continue;

            if (!token.StartsWith('-'))
            {
                if (opt.Workspace is null) opt.Workspace = token;
                else errors.Add($"多余的参数:{token}");
                continue;
            }

            string name;
            string? inlineValue = null;
            var eq = token.IndexOf('=');
            if (eq > 0)
            {
                name = token[..eq];
                inlineValue = token[(eq + 1)..];
            }
            else
            {
                name = token;
            }

            string? TakeValue()
            {
                if (inlineValue is not null) return inlineValue;
                if (i + 1 < args.Length)
                {
                    i++;
                    var v = args[i].Trim();
                    if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1];
                    return v;
                }
                return null;
            }

            switch (name.ToLowerInvariant())
            {
                case "-h":
                case "--help":
                case "/?":
                    opt.Help = true;
                    break;

                case "--repo":
                    var repo = TakeValue();
                    if (string.IsNullOrWhiteSpace(repo)) errors.Add("--repo 需要一个目录参数");
                    else opt.Repo = repo;
                    break;

                case "--workspace":
                case "--project":
                    var ws = TakeValue();
                    if (string.IsNullOrWhiteSpace(ws)) errors.Add("--workspace 需要一个目录参数");
                    else opt.Workspace = ws;
                    break;

                case "--port":
                    var portText = TakeValue();
                    if (!int.TryParse(portText, out var port) || port < 0 || port > 65535)
                        errors.Add($"--port 需要一个 0-65535 的数字,收到 {portText ?? "(空)"}");
                    else opt.Port = port;
                    break;

                case "--host":
                    var host = TakeValue();
                    if (string.IsNullOrWhiteSpace(host)) errors.Add("--host 需要一个地址参数");
                    else opt.Host = host;
                    break;

                case "--mode":
                    var mode = TakeValue();
                    switch (mode?.ToLowerInvariant())
                    {
                        case "auto": opt.Mode = LaunchMode.Auto; break;
                        case "built": opt.Mode = LaunchMode.Built; break;
                        case "source": opt.Mode = LaunchMode.Source; break;
                        default:
                            errors.Add($"--mode 只接受 auto/built/source,收到 {mode ?? "(空)"}");
                            break;
                    }
                    break;

                case "--no-browser":
                    opt.NoBrowser = true;
                    break;

                case "--no-remember":
                    opt.NoRemember = true;
                    break;

                case "--install":
                    opt.Install = true;
                    break;

                case "--rebuild":
                case "--build":
                    opt.Rebuild = true;
                    break;

                case "--no-pause":
                    opt.NoPause = true;
                    break;

                case "--console":
                    opt.ConsoleMode = true;
                    break;

                case "--proxy":
                    var proxy = TakeValue();
                    if (string.IsNullOrWhiteSpace(proxy)) errors.Add("--proxy 需要一个代理地址,例如 http://127.0.0.1:7890");
                    else if (!Uri.TryCreate(proxy, UriKind.Absolute, out _)) errors.Add($"--proxy 不是合法的绝对 URL:{proxy}");
                    else opt.Proxy = proxy;
                    break;

                case "--keep-env-proxy":
                    opt.KeepEnvProxy = true;
                    break;

                case "--print-env":
                    opt.PrintEnv = true;
                    break;

                case "--no-console-window":
                    opt.NoConsoleWindow = true;
                    break;

                case "--console-window":
                    opt.ConsoleWindow = true;
                    break;

                case "--setup":
                    opt.Setup = true;
                    break;

                case "--no-shortcut":
                    opt.NoShortcut = true;
                    break;

                case "--no-proxy":
                    opt.Proxy = null;
                    opt.KeepEnvProxy = false;
                    break;

                default:
                    errors.Add($"未知参数:{token}");
                    break;
            }
        }

        return opt;
    }

    /// <summary>
    /// 把命令行 + 环境变量 + exe 同目录的 dsh-launcher.json 合成最终配置。
    /// 优先级:命令行 &gt; 环境变量 &gt; 配置文件 &gt; 内置默认。
    /// </summary>
    public static LauncherConfig Resolve(Options opt, out List<string> errors)
    {
        errors = new List<string>();
        var file = ReadConfigFile();

        var cfg = new LauncherConfig
        {
            Repo = FirstNonEmpty(
                opt.Repo,
                Environment.GetEnvironmentVariable("DSH_REPO"),
                file?.Repo,
                DefaultRepo)!,
            Port = opt.Port
                ?? ParseIntOrNull(Environment.GetEnvironmentVariable("DSH_PORT"))
                ?? file?.Port
                ?? 3080,
            Host = FirstNonEmpty(opt.Host, Environment.GetEnvironmentVariable("DSH_HOST"), file?.Host),
            Mode = opt.Mode != LaunchMode.Auto ? opt.Mode : file?.Mode ?? LaunchMode.Auto,
            NoBrowser = opt.NoBrowser || file?.NoBrowser == true,
            NoRemember = opt.NoRemember || file?.NoRemember == true,
            Install = opt.Install,
            Rebuild = opt.Rebuild,
            ConsoleMode = opt.ConsoleMode || file?.Console == true,
            NoPause = opt.NoPause,
            // 默认**不弹任何窗口**:进度界面之外不留控制台。日志写文件(FileLogSink)。
            ShowConsoleWindow = opt.ConsoleWindow || file?.ConsoleWindow == true,
            // 默认直连:不显式给 --proxy 就不让 harness 看到任何代理变量,
            // 这样它不会因为用户环境里的 HTTP_PROXY 被绑到 Clash 之类的代理上。
            Proxy = FirstNonEmpty(opt.Proxy, Environment.GetEnvironmentVariable("DSH_PROXY"), file?.Proxy),
            KeepEnvProxy = opt.KeepEnvProxy || file?.KeepEnvProxy == true,
        };

        cfg.Repo = NormalizePath(cfg.Repo);
        if (!Directory.Exists(cfg.Repo))
            errors.Add($"仓库目录不存在:{cfg.Repo}(用 --repo 指定,或设置 DSH_REPO)");

        var workspace = FirstNonEmpty(
            opt.Workspace,
            Environment.GetEnvironmentVariable("DSH_WORKSPACE"),
            file?.Workspace,
            cfg.Repo);
        cfg.Workspace = NormalizePath(workspace!);
        if (!Directory.Exists(cfg.Workspace))
            errors.Add($"工作目录不存在:{cfg.Workspace}(用 --workspace 指定,或直接把文件夹拖到 exe 上)");

        return cfg;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static int? ParseIntOrNull(string? text)
        => int.TryParse(text, out var value) ? value : null;

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return path;
        }
    }

    private static ConfigFile? ReadConfigFile()
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            foreach (var fileName in new[] { "dsh-launcher.json", "启动DSH.json" })
            {
                var path = Path.Combine(exeDir, fileName);
                if (!File.Exists(path)) continue;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                var cfg = new ConfigFile
                {
                    Repo = ReadString(root, "repo"),
                    Workspace = ReadString(root, "workspace"),
                    Host = ReadString(root, "host"),
                    Port = root.TryGetProperty("port", out var portEl) && portEl.TryGetInt32(out var p) ? p : null,
                    NoBrowser = root.TryGetProperty("noBrowser", out var nb) && nb.ValueKind == JsonValueKind.True,
                    NoRemember = root.TryGetProperty("noRemember", out var nr) && nr.ValueKind == JsonValueKind.True,
                    Console = root.TryGetProperty("console", out var ce) && ce.ValueKind == JsonValueKind.True,
                    KeepEnvProxy = root.TryGetProperty("keepEnvProxy", out var kp) && kp.ValueKind == JsonValueKind.True,
                    NoConsoleWindow = root.TryGetProperty("noConsoleWindow", out var nc) && nc.ValueKind == JsonValueKind.True,
                    ConsoleWindow = root.TryGetProperty("consoleWindow", out var cw) && cw.ValueKind == JsonValueKind.True,
                    Proxy = ReadString(root, "proxy"),
                };
                cfg.Mode = ReadString(root, "mode")?.ToLowerInvariant() switch
                {
                    "built" => LaunchMode.Built,
                    "source" => LaunchMode.Source,
                    _ => LaunchMode.Auto,
                };
                return cfg;
            }
        }
        catch
        {
            // 配置文件坏了就忽略,不要因此拦住启动。
        }
        return null;
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private sealed class ConfigFile
    {
        public string? Repo;
        public string? Workspace;
        public string? Host;
        public int? Port;
        public LaunchMode Mode = LaunchMode.Auto;
        public bool NoBrowser;
        public bool NoRemember;
        public bool Console;
        public bool KeepEnvProxy;
        public bool NoConsoleWindow;
        public bool ConsoleWindow;
        public string? Proxy;
    }

    public static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("  DeepSeek Harness 一键启动器 —— 双击即可,不用打开终端敲命令。");
        Console.WriteLine();
        Console.WriteLine("  用法:");
        Console.WriteLine("    启动DSH.exe [选项] [工作目录]");
        Console.WriteLine();
        Console.WriteLine("  选项:");
        Console.WriteLine("    --port <N>          监听端口(默认 3080;0 表示让系统随机分配)");
        Console.WriteLine("    --host <地址>       绑定地址(默认 127.0.0.1)");
        Console.WriteLine("    --workspace <目录>  起始工作目录,即会话的 workspace root");
        Console.WriteLine("    --repo <目录>       deepseek-harness 检出目录");
        Console.WriteLine("    --mode <方式>       auto(默认) / built(用已构建产物) / source(跑源码)");
        Console.WriteLine("    --no-browser        不自动打开浏览器(仍会打印带 token 的地址)");
        Console.WriteLine("    --no-remember       不把带 token 的地址存到本机");
        Console.WriteLine("    --install           启动前先跑 pnpm install");
        Console.WriteLine("    --rebuild           启动前先跑 pnpm run build");
        Console.WriteLine("    --proxy <地址>      显式让 harness 走这个出站代理");
        Console.WriteLine("    --no-proxy          强制直连(默认行为,写出便于自我说明)");
        Console.WriteLine("    --keep-env-proxy    沿袭当前环境里的 HTTP_PROXY/HTTPS_PROXY,不做清理");
        Console.WriteLine("    --setup             在换机器时做一次性配置:选检出目录、写 dsh-launcher.json、建桌面快捷方式");
        Console.WriteLine("    --no-shortcut       配置时不要建桌面快捷方式");
        Console.WriteLine("    --console           用控制台界面而不是 WPF 窗口(调试用)");
        Console.WriteLine("    --console-window    额外开一个最小化到任务栏的日志控制台(默认不弹任何窗口)");
        Console.WriteLine("    --no-console-window 明确表示不要日志控制台(默认行为)");
        Console.WriteLine("    --print-env         打印 harness 子进程将看到的环境与命令行后退出(诊断)");
        Console.WriteLine("    --no-pause          出错/结束时不要等待按键");
        Console.WriteLine("    -h, --help          显示这份帮助");
        Console.WriteLine();
        Console.WriteLine("  也可以直接把一个文件夹拖到 exe 上,它就成为本次会话的工作目录。");
        Console.WriteLine();
        Console.WriteLine("  网络:默认给 harness 一个不含任何代理变量的环境(直连)。");
        Console.WriteLine("        当前环境里的 HTTP_PROXY / HTTPS_PROXY 不会被改动,只是不传给 harness。");
        Console.WriteLine();
        Console.WriteLine("  环境变量:DSH_REPO / DSH_WORKSPACE / DSH_PORT / DSH_HOST / DSH_PROXY");
        Console.WriteLine("  配置文件:exe 同目录的 dsh-launcher.json");
        Console.WriteLine();
    }
}
