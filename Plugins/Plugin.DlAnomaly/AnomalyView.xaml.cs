using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Core.Halcon.Controls;
using Microsoft.Win32;

namespace Plugin.DlAnomaly;

/// <summary>
/// 深度学习异常检测的配置视图代码后置。
///
/// 为什么命令要在 <c>InitializeComponent()</c> 之**前**建：
/// XAML 里的 <c>Command="{Binding BrowseModelCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"</c>
/// 在 <c>InitializeComponent()</c> 解析期间就会求值一次；如果命令属性那时还是 null，
/// 绑定会把 null 缓存进 BindingExpression，之后即使赋值也不会重新拉取——按钮静默无响应。
/// 这是 DlClassify 视图踩过的同一个坑，这里按同样顺序写。
///
/// 为什么 OnViewLoaded 挂在 Loaded 而不是构造器：
/// 构造时 DataContext 还没设置（宿主是 <c>new AnomalyView { DataContext = this }</c>），
/// 此时 <c>Plugin</c> 为 null，取上游图与刷新模型态都会空跑。
/// </summary>
public partial class AnomalyView : UserControl
{
    public AnomalyView()
    {
        // 先建命令，再 InitializeComponent —— 顺序不能换（见类注释）。
        BrowseModelCommand = new UiCommand(BrowseModel);
        TryPreviewCommand = new UiCommand(TryPreview);

        InitializeComponent();

        // DataContext 由宿主在构造后赋值，所以首屏刷新放 Loaded。
        Loaded += (_, _) => Plugin?.OnViewLoaded();
    }

    private DlAnomalyPlugin? Plugin => DataContext as DlAnomalyPlugin;

    /// <summary>选择模型文件。写完 ModelPath 立刻走一次 OnViewLoaded 刷新模型卡。</summary>
    public ICommand BrowseModelCommand { get; }

    /// <summary>用当前上游图试算一次，不执行流程。</summary>
    public ICommand TryPreviewCommand { get; }

    private void BrowseModel()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 ONNX 异常检测模型",
            Filter = "ONNX 模型|*.onnx|所有文件|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (Plugin is { } plugin)
        {
            plugin.ModelPath = dialog.FileName;
            plugin.OnViewLoaded();
        }
    }

    private void TryPreview() => Plugin?.TryPreviewDetect();

    private void FitWindow_Click(object sender, RoutedEventArgs e)
        => FindVisualChild<ImageReadOnly>(PreviewHost)?.FitToWindow();

    private void FitImage_Click(object sender, RoutedEventArgs e)
        => FindVisualChild<ImageReadOnly>(PreviewHost)?.FitToImage();

    /// <summary>
    /// 在可视树里找第一个指定类型的孩子。
    /// ImageReadOnly 在 ContentTemplate 里，按名字取不到，只能遍历。
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>最简命令：无参数、恒可执行。</summary>
    private sealed class UiCommand : ICommand
    {
        private readonly Action _execute;

        public UiCommand(Action execute) => _execute = execute;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }
}

/// <summary>
/// 缩放模式的上屏文案。
///
/// R40 口径①：**持久化仍然是成员名**（Letterbox / Stretch），这里只改显示。
/// 所以 ConvertBack 返回 <see cref="Binding.DoNothing"/>，绝不把中文写回模型。
/// </summary>
public sealed class AnomalyResizeModeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            AnomalyResizeMode.Letterbox => "Letterbox · 等比补边（推荐）",
            AnomalyResizeMode.Stretch => "Stretch · 拉伸填充",
            _ => value?.ToString() ?? string.Empty,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>热力图插值方式的上屏文案（同上，只改显示不改值）。</summary>
public sealed class AnomalyInterpolationLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            AnomalyInterpolation.Nearest => "Nearest · 最近邻（默认）",
            AnomalyInterpolation.Bilinear => "Bilinear · 双线性",
            _ => value?.ToString() ?? string.Empty,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
