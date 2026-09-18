namespace DshLauncher;

/// <summary>
/// 诊断输出:打印"harness 子进程将会看到什么"。
///
/// 存在的原因很实际:代理策略是在子进程环境里生效的,肉眼看不见。有了它,
/// "harness 到底走不走代理"这条就能当场证实,而不是靠读代码相信。
/// </summary>
internal static class Diagnostic
{
    private static readonly string[] ProxyNames =
    [
        "HTTP_PROXY", "http_proxy", "HTTPS_PROXY", "https_proxy",
        "ALL_PROXY", "all_proxy", "NO_PROXY", "no_proxy", "NODE_USE_ENV_PROXY",
    ];

    public static int DumpChildEnvironment(LauncherConfig cfg, List<string> configErrors)
    {
        ConsoleBootstrap.EnsureConsole();

        if (configErrors.Count > 0)
        {
            foreach (var error in configErrors) Console.Error.WriteLine("配置有误:" + error);
            return 2;
        }

        var items = new List<CheckItem>();
        var notes = new List<string>();
        var plan = SelfCheck.Run(cfg, items, notes);
        if (plan is null)
        {
            Console.Error.WriteLine("自检未通过,无法给出子进程环境:");
            foreach (var item in items.Where(i => !i.Ok))
                Console.Error.WriteLine($"  · {item.Label}:{item.Detail}");
            return 4;
        }

        var psi = LaunchRunner.BuildStartInfo(plan);

        Console.WriteLine();
        Console.WriteLine("== harness 子进程 ==");
        Console.WriteLine($"  node    : {psi.FileName}");
        Console.WriteLine($"  cwd     : {psi.WorkingDirectory}");
        Console.WriteLine($"  args    : {string.Join(' ', psi.ArgumentList)}");

        Console.WriteLine();
        Console.WriteLine("== 子进程会看到的相关环境变量 ==");
        var childHits = Lookup(psi.Environment.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));
        if (childHits.Count == 0) Console.WriteLine("  (一个都没有 —— harness 直连)");
        foreach (var hit in childHits) Console.WriteLine($"  {hit.Key} = {hit.Value}");

        Console.WriteLine();
        Console.WriteLine("== 启动器自身环境里的同名列(未被改动,只是不传下去)==");
        var ownHits = Lookup(ProxyNames.Select(n => new KeyValuePair<string, string?>(n, Environment.GetEnvironmentVariable(n))));
        if (ownHits.Count == 0) Console.WriteLine("  (本来就没有)");
        foreach (var hit in ownHits) Console.WriteLine($"  {hit.Key} = {hit.Value}");

        Console.WriteLine();
        Console.WriteLine(plan.KeepEnvProxy
            ? "策略:沿袭环境代理(--keep-env-proxy)"
            : string.IsNullOrWhiteSpace(plan.Proxy)
                ? "策略:直连(默认,已摘掉代理变量)"
                : $"策略:走代理 {plan.Proxy}");
        Console.WriteLine();
        return 0;
    }

    private static List<KeyValuePair<string, string>> Lookup(IEnumerable<KeyValuePair<string, string?>> source)
    {
        var hits = new List<KeyValuePair<string, string>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in source)
        {
            if (string.IsNullOrEmpty(pair.Value)) continue;
            if (!ProxyNames.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)) continue;
            if (seen.Add(pair.Key)) hits.Add(new KeyValuePair<string, string>(pair.Key, pair.Value));
        }

        return hits;
    }
}
