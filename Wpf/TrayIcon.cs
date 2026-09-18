using System.Drawing;
using System.Windows.Forms;

namespace DshLauncher.Wpf;

/// <summary>
/// 托盘图标。窗口关掉之后服务还要继续跑,托盘就是它的落脚点:
/// 双击回到窗口,右键菜单里能开 GUI 或停止服务并退出。
/// 图标直接取自身 exe 的资源 —— 也就是嵌进去的 DeepSeek 标记,不用额外带资源。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action onOpenGui, Action onShowWindow, Action onStopAndExit)
    {
        Icon? icon = null;
        try
        {
            if (Environment.ProcessPath is { } exe) icon = Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
            // 取不到就用系统默认图标
        }

        icon ??= SystemIcons.Application;

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 DeepSeek Harness", null, (_, _) => onOpenGui());
        menu.Items.Add("显示状态窗口", null, (_, _) => onShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("停止服务并退出", null, (_, _) => onStopAndExit());

        _icon = new NotifyIcon
        {
            Icon = icon,
            Text = "DeepSeek Harness",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => onShowWindow();
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);
        }
        catch
        {
            // 通知被系统策略禁用时忽略
        }
    }

    public void Dispose()
    {
        try
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        catch
        {
            // 退出路径上忽略
        }
    }
}
