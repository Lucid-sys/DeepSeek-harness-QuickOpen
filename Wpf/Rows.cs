using System.ComponentModel;
using System.Windows.Media;

namespace DshLauncher.Wpf;

/// <summary>进度列表里的一行:✓/●/○ + 标签 + 结果说明。<see cref="Label"/> 充当稳定标识。</summary>
internal sealed class StepRow : INotifyPropertyChanged
{
    private string _glyph = "○";
    private Brush _brush = Palette.Dim;
    private string _detail = string.Empty;

    public StepRow(string label) => Label = label;

    public string Label { get; }

    public string Glyph
    {
        get => _glyph;
        set { _glyph = value; Raise(nameof(Glyph)); }
    }

    public Brush Brush
    {
        get => _brush;
        set { _brush = value; Raise(nameof(Brush)); }
    }

    public string Detail
    {
        get => _detail;
        set { _detail = value; Raise(nameof(Detail)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>日志区的一行(harness 子进程输出或启动器自己的提示)。</summary>
internal sealed class LogRow
{
    public LogRow(string text, Brush brush)
    {
        Text = text;
        Brush = brush;
    }

    public string Text { get; }

    public Brush Brush { get; }
}

/// <summary>界面配色。全部冻结,跨线程复用安全,也省掉每次新建的开销。</summary>
internal static class Palette
{
    public static readonly Brush Accent = FromHex("#FF4D6BFE");
    public static readonly Brush Ok = FromHex("#FF12A150");
    public static readonly Brush Error = FromHex("#FFDC2626");
    public static readonly Brush Warn = FromHex("#FFB45309");
    public static readonly Brush Text = FromHex("#FF1D2330");
    public static readonly Brush Muted = FromHex("#FF6B7280");
    public static readonly Brush Dim = FromHex("#FFA0A7B8");
    public static readonly Brush LogText = FromHex("#FF39414F");
    public static readonly Brush LogError = FromHex("#FFB45309");
    public static readonly Brush LogLauncher = FromHex("#FF3B5BDB");

    private static Brush FromHex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
