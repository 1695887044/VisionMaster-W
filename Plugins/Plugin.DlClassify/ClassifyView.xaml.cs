using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Core.Halcon.Controls;
using Microsoft.Win32;

namespace Plugin.DlClassify
{
    /// <summary>
    /// 深度学习分类的配置视图（Web 化 V1）。
    ///
    /// 视图不写业务逻辑：模型加载、参数校验、试算都在 DlClassifyPlugin 里。
    /// 本文件只保留 UI 职责：
    ///   · 视图就绪信号（Loaded → OnViewLoaded）；
    ///   · 文件选择对话框（「浏览」）；
    ///   · 「试算一下」转发；
    ///   · 预览工具条的「适应窗口 / 1:1」（走 HalconBase 既有方法）；
    ///   · 一个只用于显示的枚举中文名转换器。
    ///
    /// 两个 ICommand 是视图级 UI 命令（不是 VM）：它们只委托给上面的既有入口，
    /// 不改变任何端口、持久化或业务逻辑。这样批量插件视图冒烟也能断言 Button.Command。
    /// </summary>
    public partial class ClassifyView : UserControl
    {
        public ClassifyView()
        {
            // 先建命令再解析 XAML：按钮的 Command 绑定在 InitializeComponent 期间就会查找这两个属性，
            // 晚赋值会让绑定拿到 null（没有 PropertyChanged 通知，之后也不会自己恢复）。
            BrowseModelCommand = new UiCommand(BrowseModel);
            TryPreviewCommand = new UiCommand(TryPreview);
            InitializeComponent();
            Loaded += (_, _) => Plugin?.OnViewLoaded();
        }

        /// <summary>「浏览」：打开文件对话框并写回 ModelPath，随后刷新模型摘要。</summary>
        public ICommand BrowseModelCommand { get; }

        /// <summary>「试算一下」：转发到插件既有 TryPreviewClassify()。</summary>
        public ICommand TryPreviewCommand { get; }

        private DlClassifyPlugin? Plugin => DataContext as DlClassifyPlugin;

        private void BrowseModel()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 ONNX 分类模型",
                Filter = "ONNX 模型|*.onnx|所有文件|*.*",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog() == true && Plugin != null)
            {
                Plugin.ModelPath = dialog.FileName;
                // 模型换了 → 重新读一次元数据，让摘要立刻反映新模型（选错模型时这一步就是线索）
                Plugin.OnViewLoaded();
            }
        }

        private void TryPreview() => Plugin?.TryPreviewClassify();

        /// <summary>适应窗口：走 HalconBase 的既有入口，不另造缩放逻辑。</summary>
        private void FitWindow_Click(object sender, RoutedEventArgs e)
            => FindPreviewControl()?.FitToWindow();

        /// <summary>实际像素 1:1：与右键菜单「适应图片」同一实现。</summary>
        private void FitImage_Click(object sender, RoutedEventArgs e)
            => FindPreviewControl()?.FitToImage();

        /// <summary>
        /// 预览控件由 ContentTemplate 承载（有图才创建），不在视图 namescope 里；
        /// 这里从 PreviewHost 的可视树里取当前实例。无图时返回 null，按钮静默空跑。
        /// </summary>
        private ImageReadOnly? FindPreviewControl()
            => FindVisualChild<ImageReadOnly>(PreviewHost);

        private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    return match;

                var nested = FindVisualChild<T>(child);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        /// <summary>极简 UI 命令：无参数、永远可执行，只包一个 Action。</summary>
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
    /// 缩放模式下拉的中文显示名。
    /// 只影响界面文字：SelectedItem 仍是 ClassifyResizeMode 枚举，
    /// 持久化与运行期参数仍是 Letterbox / Stretch 成员名（R40 口径①）。
    /// </summary>
    public sealed class ResizeModeLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is ClassifyResizeMode mode
                ? mode switch
                {
                    ClassifyResizeMode.Letterbox => "Letterbox · 等比补边（推荐）",
                    ClassifyResizeMode.Stretch => "Stretch · 拉伸填充",
                    _ => mode.ToString(),
                }
                : "—";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
