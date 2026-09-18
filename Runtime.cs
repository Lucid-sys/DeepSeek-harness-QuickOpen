using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace DshLauncher;

internal enum PortState
{
    /// <summary>端口空闲,可以启动。</summary>
    Free,
    /// <summary>端口上已有 DSH 实例(裸 GET / 返回 401,这是它的认证栅栏)。</summary>
    DshRunning,
    /// <summary>端口被别的程序占用。</summary>
    Occupied,
}

/// <summary>
/// 启动前的端口探测,用来区分"已经有 DSH 在跑"(不重复启动,直接开浏览器)
/// 和"端口被别人占了"(报错并让用户换端口)。
///
/// 判定依据是实测出来的:DSH 的 Web 服务在没有 token、没有 cookie 时对 GET / 返回
/// 极简 401,页面里没有二次登录入口。
/// </summary>
internal static class InstanceProbe
{
    /// <summary>
    /// 连接超时。必须给足余量:这台机器上连接一个**空闲**回环端口大约 2.0-2.1 秒才返回拒绝
    /// (有安全软件/WFP 在延迟 RST),如果超时设得比它短,空闲端口就会被误判成"被占用"。
    /// 监听中的端口则是几十毫秒内连上。
    /// </summary>
    private const int ConnectTimeoutMs = 5000;

    private enum ConnectOutcome
    {
        Connected,
        Refused,
        Timeout,
    }

    public static PortState Probe(int port, out string detail)
    {
        detail = string.Empty;

        switch (TryConnect(port))
        {
            case ConnectOutcome.Refused:
                detail = "端口空闲";
                return PortState.Free;

            case ConnectOutcome.Timeout:
                // 回环上真正在监听时连接是瞬间完成的,超时只可能意味着"没有监听者 + 拒绝被延迟"。
                // 按空闲处理,万一真有冲突,DSH 自己会报出一个明确得多的绑定失败。
                detail = "连接超时(按空闲处理,由 DSH 自行判断端口冲突)";
                return PortState.Free;
        }

        // 有人监听 —— 用 HTTP 应答区分"这是 DSH" 还是"别的程序"。
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
            using var response = http.GetAsync($"http://127.0.0.1:{port}/").GetAwaiter().GetResult();
            var code = (int)response.StatusCode;

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                detail = $"HTTP {code} — DSH 认证栅栏";
                return PortState.DshRunning;
            }

            detail = $"HTTP {code}";
            return PortState.Occupied;
        }
        catch (Exception ex)
        {
            detail = $"有程序在监听但应答无法识别({ex.GetType().Name})";
            return PortState.Occupied;
        }
    }

    private static ConnectOutcome TryConnect(int port)
    {
        var client = new TcpClient();
        try
        {
            var connect = client.ConnectAsync(IPAddress.Loopback, port);
            if (!connect.Wait(ConnectTimeoutMs))
            {
                // 超时后让连接任务自己收尾,避免留下未观察的任务异常。
                _ = connect.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
                return ConnectOutcome.Timeout;
            }
            return client.Connected ? ConnectOutcome.Connected : ConnectOutcome.Refused;
        }
        catch
        {
            return ConnectOutcome.Refused;
        }
        finally
        {
            client.Dispose();
        }
    }
}

/// <summary>
/// Windows 作业对象:把 DSH 子进程放进一个带 KILL_ON_JOB_CLOSE 的作业里。
/// 这样无论启动器是正常退出、被 Ctrl+C 还是被任务管理器强杀,
/// 内核都会连带干掉整棵子进程树 —— 不会留下占着 3080 端口的孤儿 node 进程。
/// </summary>
internal sealed class JobObject : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation_
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob, int jobObjectInformationClass, IntPtr lpJobObjectInformation, uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private IntPtr _handle;

    /// <summary>创建作业并绑定子进程;失败不致命(退出时会退回 taskkill 兜底)。</summary>
    public bool TryCreateAndAssign(Process process)
    {
        try
        {
            _handle = CreateJobObjectW(IntPtr.Zero, null);
            if (_handle == IntPtr.Zero) return false;

            var info = new JobObjectExtendedLimitInformation_
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose,
                },
            };

            var size = Marshal.SizeOf<JobObjectExtendedLimitInformation_>();
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, buffer, fDeleteOld: false);
                if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, buffer, (uint)size))
                    return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return AssignProcessToJobObject(_handle, process.Handle);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }
}

/// <summary>
/// 记住最近一次带 token 的启动地址,让"已经有实例在跑"这条路径也能一步进 GUI
/// (浏览器 cookie 有 30 天有效期,过期后靠这条记录仍能免登录)。
/// token 只在本机 loopback 上有效,写入 %LOCALAPPDATA%;--no-remember 可关闭。
/// </summary>
internal static class RememberedUrl
{
    private static string StoreDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeepSeekHarness");

    private static string StorePath => Path.Combine(StoreDirectory, "last-urls.txt");

    public static void Write(int port, string url)
    {
        try
        {
            System.IO.Directory.CreateDirectory(StoreDirectory);
            var map = ReadAll();
            map[port] = url;
            File.WriteAllLines(
                StorePath,
                map.Select(pair => $"{pair.Key}\t{pair.Value}"),
                new UTF8Encoding(false));
        }
        catch
        {
            // 记不住不影响启动。
        }
    }

    public static string? TryRead(int port)
    {
        try
        {
            return ReadAll().TryGetValue(port, out var url) ? url : null;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<int, string> ReadAll()
    {
        var map = new Dictionary<int, string>();
        if (!File.Exists(StorePath)) return map;

        foreach (var line in File.ReadAllLines(StorePath))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            if (int.TryParse(line[..tab], out var port)) map[port] = line[(tab + 1)..];
        }
        return map;
    }
}
