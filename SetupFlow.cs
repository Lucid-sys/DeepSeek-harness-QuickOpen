using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using System.Windows;
using Microsoft.Win32;

namespace DshLauncher;

/// <summary>
/// 换一台机器时的一次性配置:选 deepseek-harness 目录 → 校验 → 写 exe 同目录的
/// dsh-launcher.json → 建桌面快捷方式。
///
/// 存在的理由:exe 本身与路径无关,唯一随机器变的就是"检出目录在哪"。
/// 与其让人手改 JSON,不如让程序自己问一次、写一次。
/// </summary>
internal static class SetupFlow
{
    /// <summary>
    /// 跑一次交互式配置。
    /// </summary>
    /// <param name="cfg">当前解析出的配置(用于预填目录)。</param>
    /// <param name="createShortcut">是否创建桌面快捷方式。</param>
    /// <param name="explicitRepo">命令行已给的目录;给了就不再弹选择框。</param>
    /// <param name="showSummary">是否弹"配置完成"的提示框。</param>
    /// <returns>配置成功返回检出目录,用户取消返回 null。</returns>
    public static string? Run(LauncherConfig cfg, bool createShortcut, string? explicitRepo, bool showSummary = true)
    {
        var repo = explicitRepo;

        while (true)
        {
            if (string.IsNullOrWhiteSpace(repo))
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "选择 deepseek-harness 检出目录",
                    Multiselect = false,
                };
                if (Directory.Exists(cfg.Repo)) dialog.InitialDirectory = cfg.Repo;
                if (dialog.ShowDialog() != true) return null;
                repo = dialog.FolderName;
            }

            var (ok, error, warning) = Inspect(repo!);
            if (ok)
            {
                if (warning is not null
                    && MessageBox.Show(
                        $"这个检出能认出,但还缺构建产物:{warning}\n\n" +
                        "启动器会明确提示你先运行 pnpm run build。继续吗?",
                        "缺少构建产物", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return null;
                }
                break;
            }

            if (MessageBox.Show(
                    $"这个目录不像 deepseek-harness 检出:\n{repo}\n\n{error}\n\n重新选择?",
                    "目录不正确", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return null;
            }
            repo = null;
        }

        string configPath;
        try
        {
            configPath = WriteConfig(repo!);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"没法把配置写到 exe 同目录:\n{ex.Message}\n\n" +
                "把启动器放到一个可写目录(比如桌面或 D 盘的某个文件夹)再试一次。",
                "写入失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        var shortcut = createShortcut ? CreateShortcut() : null;

        if (showSummary)
        {
            var lines = $"检出目录    {repo}\n配置文件    {configPath}";
            lines += shortcut is null ? string.Empty : $"\n快捷方式    {shortcut}";
            MessageBox.Show(
                $"配置完成。\n\n{lines}\n\n" +
                (shortcut is null
                    ? "之后直接运行这个 exe 即可。"
                    : "现在可以双击桌面上的「启动 DeepSeek Harness」。"),
                "DeepSeek Harness 一键启动器", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        return repo;
    }

    /// <summary>校验一个目录是不是可用的检出。</summary>
    public static (bool Ok, string? Error, string? Warning) Inspect(string repo)
    {
        if (!Directory.Exists(repo)) return (false, "目录不存在。", null);

        var packageJson = Path.Combine(repo, "package.json");
        var sourceEntry = Path.Combine(repo, "apps", "cli", "src", "bin.ts");
        if (!File.Exists(packageJson) || !File.Exists(sourceEntry))
            return (false, "缺 package.json 或 apps/cli/src/bin.ts。", null);

        var missing = new List<string>();
        if (!Directory.Exists(Path.Combine(repo, "node_modules", ".pnpm"))) missing.Add("依赖(pnpm install)");
        if (!File.Exists(Path.Combine(repo, "apps", "cli", "lib", "bin.js"))) missing.Add("apps/cli/lib/bin.js");
        if (!File.Exists(Path.Combine(repo, "apps", "web", "dist", "index.html"))) missing.Add("apps/web/dist/index.html");

        return (true, null, missing.Count == 0 ? null : string.Join("、", missing));
    }

    /// <summary>把 repo 写进 exe 同目录的 dsh-launcher.json,保留已有字段。</summary>
    private static string WriteConfig(string repo)
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var path = Path.Combine(exeDir, "dsh-launcher.json");

        JsonObject root;
        if (File.Exists(path))
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        root["repo"] = repo;

        // 工作目录若没写过,就跟着检出目录走(和默认行为一致)。
        if (root["workspace"] is null) root["workspace"] = repo;

        File.WriteAllText(
            path,
            root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                // 默认编码器会把非 ASCII 转义成 \u5915 这种形式。JSON 解析器能还原,
                // 但这是给人看、甚至给人手改的配置文件 —— 中文路径要原样写出来。
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            }),
            new System.Text.UTF8Encoding(false));

        return path;
    }

    /// <summary>在当前 exe 旁边建桌面快捷方式(指向这个 exe)。</summary>
    private static string? CreateShortcut()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return null;

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var link = Path.Combine(desktop, "启动 DeepSeek Harness.lnk");

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
            shortcut.IconLocation = exe + ",0";
            shortcut.Description = "一键启动 DeepSeek Harness Web GUI";
            shortcut.Save();

            return link;
        }
        catch
        {
            return null;
        }
    }
}
