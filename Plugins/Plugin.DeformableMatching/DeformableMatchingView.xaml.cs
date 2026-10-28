using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Plugin.DeformableMatching
{
    /// <summary>
    /// 可形变模板匹配配置视图 —— 纯绑定层。
    /// 视图只做四件事：Loaded 通知 VM 载入参考图；工具条调用 HalconBase 的 FitToWindow/FitToImage；
    /// 动作按钮把点击转成 ICommand 转发给 VM；不持有任何业务状态。
    /// </summary>
    public partial class DeformableMatchingView : UserControl
    {
        public DeformableMatchingView()
        {
            InitializeComponent();

            LearnCommand = new UiCommand(() => Plugin?.LearnTemplate());
            ClearTemplateCommand = new UiCommand(() => Plugin?.ClearTemplate());
            MaskCommand = new UiCommand(() => Plugin?.OpenMaskEditor());
            ClearMaskCommand = new UiCommand(() => Plugin?.ClearMask());
            LoadReferenceCommand = new UiCommand(() => Plugin?.ReloadReferencePreview());
            LoadLocalTemplateCommand = new UiCommand(() => Plugin?.LoadLocalTemplate());
            ClearLocalTemplateCommand = new UiCommand(() => Plugin?.ClearLocalTemplate());
            ExportTemplateCommand = new UiCommand(ExportTemplate);

            Loaded += OnLoaded;
        }

        private DeformableMatchingPlugin? Plugin => DataContext as DeformableMatchingPlugin;

        public ICommand LearnCommand { get; }
        public ICommand ClearTemplateCommand { get; }
        public ICommand MaskCommand { get; }
        public ICommand ClearMaskCommand { get; }
        public ICommand LoadReferenceCommand { get; }
        public ICommand LoadLocalTemplateCommand { get; }
        public ICommand ClearLocalTemplateCommand { get; }
        public ICommand ExportTemplateCommand { get; }

        private void ExportTemplate()
        {
            var plugin = Plugin;
            if (plugin == null)
                return;
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "形变模板 JSON|*.json|所有文件|*.*",
                DefaultExt = ".json",
                AddExtension = true,
                FileName = "deformable-template.json",
            };
            if (dialog.ShowDialog() == true)
                plugin.ExportTemplate(dialog.FileName, out _);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
            => Plugin?.OnViewLoaded();

        private void FitWindow_Click(object sender, RoutedEventArgs e)
            => PreviewCanvas.FitToWindow();

        private void FitImage_Click(object sender, RoutedEventArgs e)
            => PreviewCanvas.FitToImage();
    }

    /// <summary>true → Collapsed，false → Visible（用于「圆时隐藏角度/第二半轴」）。</summary>
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>极简 ICommand：CanExecute 恒真，供视图按钮绑定（与 Matching/BeadInspect 同款）。</summary>
    internal sealed class UiCommand : ICommand
    {
        private readonly Action _execute;

        public UiCommand(Action execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();
    }
}
