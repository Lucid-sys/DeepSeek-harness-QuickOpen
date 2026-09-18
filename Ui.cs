using System.Runtime.InteropServices;
using System.Text;

namespace DshLauncher;

/// <summary>
/// 控制台输出与"双击场景"的交互辅助。
///
/// 关键点:<see cref="OwnsConsole"/> 用 GetConsoleProcessList 判断这个控制台窗口是不是本进程独占的。
/// 双击 exe 时 Windows 会新建一个控制台,列表里只有自己 → 需要 MessageBox + 等待按键,
/// 否则报错窗口一闪而过;从终端里运行时控制台是共享的 → 不要弹窗也不要阻塞。
/// </summary>
internal static class Ui
{
    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const uint MB_TOPMOST = 0x00040000;
    private const uint MB_SETFOREGROUND = 0x00010000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] lpdwProcessList, uint dwProcessCount);

    /// <summary>--no-pause 时禁止一切等待按键的行为。</summary>
    public static bool PauseAllowed { get; set; } = true;

    private static bool? _ownsConsole;

    /// <summary>本进程是否独占自己的控制台窗口(即被 Exploer 双击启动)。</summary>
    public static bool OwnsConsole
    {
        get
        {
            if (_ownsConsole is { } cached) return cached;
            bool result;
            try
            {
                var list = new uint[8];
                var count = GetConsoleProcessList(list, (uint)list.Length);
                result = count == 1;
            }
            catch
            {
                result = false;
            }
            _ownsConsole = result;
            return result;
        }
    }

    public static void Init()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.Title = "DeepSeek Harness 一键启动";
        }
        catch
        {
            // stdout 被重定向时设置编码可能失败;不影响功能。
        }
    }

    public static void Banner()
    {
        Console.WriteLine();
        WriteColor(ConsoleColor.DarkCyan, "  DeepSeek Harness 一键启动器");
        WriteColor(ConsoleColor.DarkGray, $"  v{Program.Version}   ·   双击启动 · 关闭窗口即停止服务");
        Console.WriteLine();
    }

    public static void Separator() => WriteColor(ConsoleColor.DarkGray, new string('-', 68));

    public static void Info(string text) => WriteColor(ConsoleColor.Gray, "  " + text);

    public static void Ok(string text) => WriteColor(ConsoleColor.Green, "  " + text);

    public static void Warn(string text) => WriteColor(ConsoleColor.Yellow, "  " + text);

    public static void Error(string text) => WriteColor(ConsoleColor.Red, "  " + text);

    /// <summary>子进程 stdout 原样转发(带缩进,便于和启动器自己的输出区分)。</summary>
    public static void ChildOut(string line) => Console.WriteLine("  | " + line);

    /// <summary>子进程 stderr 用暗黄色转发。</summary>
    public static void ChildErr(string line) => WriteColor(ConsoleColor.DarkYellow, "  ! " + line);

    public static void Check(string label, bool ok, string detail)
    {
        var tag = ok ? "[ OK ]" : "[FAIL]";
        var color = ok ? ConsoleColor.Green : ConsoleColor.Red;
        var pad = Math.Max(label.Length, 12 - MeasureWide(label));
        Console.Write("  ");
        WriteColorInline(color, tag);
        Console.Write(" ");
        WriteColorInline(ConsoleColor.White, label.PadRight(pad));
        WriteColorInline(ConsoleColor.DarkGray, detail);
        Console.WriteLine();
    }

    public static void WritePlan(string repo, string cwd, int port, string? host, string mode, string node, string nodeVersion)
    {
        Separator();
        Info($"运行方式 : {mode}");
        Info($"仓库     : {repo}");
        Info($"工作目录 : {cwd}");
        Info($"监听     : {(host ?? "127.0.0.1")}:{port}");
        Info($"Node     : {node} ({nodeVersion})");
        Separator();
    }

    /// <summary>
    /// 失败路径:总是(在双击场景下)弹一个消息框,并阻塞等待按键,保证用户读得到。
    /// </summary>
    public static void Fail(string summary, string detail)
    {
        Console.WriteLine();
        Error(summary);
        if (OwnsConsole)
        {
            try
            {
                MessageBoxW(IntPtr.Zero, $"{summary}\n\n{detail}", "DeepSeek Harness 启动失败",
                    MB_OK | MB_ICONERROR | MB_TOPMOST | MB_SETFOREGROUND);
            }
            catch
            {
                // 没有桌面会话(计划任务/服务)时忽略。
            }
        }
        WaitForKey(autoCloseSeconds: 0);
    }

    /// <summary>
    /// 成功/正常结束路径:双击场景下给一个自动关闭的倒计时,按键可立刻关掉。
    /// </summary>
    public static void Done(string message)
    {
        Console.WriteLine();
        Ok(message);
        WaitForKey(autoCloseSeconds: 8);
    }

    private static void WaitForKey(int autoCloseSeconds)
    {
        if (!PauseAllowed) return;
        if (!OwnsConsole) return; // 从终端运行时不要阻塞调用者

        Console.WriteLine();
        var hint = autoCloseSeconds > 0
            ? $"  按任意键关闭此窗口…({autoCloseSeconds} 秒后自动关闭)"
            : "  按任意键关闭此窗口…";
        WriteColor(ConsoleColor.DarkGray, hint);

        var deadline = Environment.TickCount64 + autoCloseSeconds * 1000L;
        while (true)
        {
            try
            {
                if (Console.KeyAvailable)
                {
                    Console.ReadKey(intercept: true);
                    return;
                }
            }
            catch
            {
                return; // stdin 不可用
            }

            if (autoCloseSeconds > 0 && Environment.TickCount64 >= deadline) return;
            Thread.Sleep(80);
        }
    }

    private static void WriteColor(ConsoleColor color, string text)
    {
        var previous = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
        }
        catch
        {
            // 重定向输出时忽略
        }
        Console.WriteLine(text);
        try
        {
            Console.ForegroundColor = previous;
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>和 <see cref="WriteColor"/> 一样,但不换行(用于拼一行里的多段彩色文本)。</summary>
    private static void WriteColorInline(ConsoleColor color, string text)
    {
        var previous = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
        }
        catch
        {
            // 忽略
        }
        Console.Write(text);
        try
        {
            Console.ForegroundColor = previous;
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>中日韩字符在等宽终端里占两格,用于粗略对齐标签。</summary>
    private static int MeasureWide(string text)
    {
        var extra = 0;
        foreach (var ch in text)
        {
            if (ch >= 0x1100 && (ch <= 0x115F || ch is >= (char)0x2E80 and <= (char)0xA4CF
                || ch is >= (char)0xAC00 and <= (char)0xD7A3 || ch is >= (char)0xF900 and <= (char)0xFAFF
                || ch is >= (char)0xFE30 and <= (char)0xFE6F || ch is >= (char)0xFF00 and <= (char)0xFF60
                || ch is >= (char)0xFFE0 and <= (char)0xFFE6))
            {
                extra++;
            }
        }
        return extra;
    }
}
