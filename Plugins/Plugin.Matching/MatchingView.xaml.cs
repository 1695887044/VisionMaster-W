using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Core.Halcon.Controls;

namespace Plugin.Matching
{
    /// <summary>
    /// 模板匹配的配置视图。
    /// 视图不写业务逻辑：画框由 Core.Halcon 的 ImageEdit 负责，画布与参数的同步都在 MatchingPlugin 里。
    /// 本文件只留三类 UI 胶水：视图就绪信号（带图上屏）、按钮命令转发、状态文案与画布适配。
    /// </summary>
    public partial class MatchingView : UserControl
    {
        public MatchingView()
        {
            InitializeComponent();

            AddEntryCommand = new UiCommand(() => Plugin?.AddTemplateEntry());
            DeleteEntryCommand = new UiCommand(() => Plugin?.DeleteSelectedEntry());
            SetDefaultCommand = new UiCommand(() => Plugin?.SetAsDefault());
            LoadImageCommand = new UiCommand(() => Plugin?.LoadPreviewImage());
            CreateTemplateCommand = new UiCommand(() => Plugin?.CreateTemplate());
            ClearTemplateCommand = new UiCommand(() => Plugin?.ClearTemplate());
            ClearSmearCommand = new UiCommand(() => Plugin?.ClearSmear());

            SizeChanged += (_, e) => IsNarrow = e.NewSize.Width < 700;
            Loaded += (_, _) => (DataContext as MatchingPlugin)?.OnViewLoaded();
        }

        private MatchingPlugin? Plugin => DataContext as MatchingPlugin;

        /// <summary>窄档（宿主 640 窗口的内容区约 600）：只控制视图内的提示/胶囊/预览高度，不改业务。</summary>
        public static readonly DependencyProperty IsNarrowProperty = DependencyProperty.Register(
            nameof(IsNarrow),
            typeof(bool),
            typeof(MatchingView),
            new PropertyMetadata(false));

        public bool IsNarrow
        {
            get => (bool)GetValue(IsNarrowProperty);
            private set => SetValue(IsNarrowProperty, value);
        }

        // 按钮命令：DataContext 是插件实例，所以命令挂在视图上、用 RelativeSource 绑定。
        // 只转发到插件现有的公开方法，不新增任何业务行为。
        public ICommand AddEntryCommand { get; }
        public ICommand DeleteEntryCommand { get; }
        public ICommand SetDefaultCommand { get; }
        public ICommand LoadImageCommand { get; }
        public ICommand CreateTemplateCommand { get; }
        public ICommand ClearTemplateCommand { get; }
        public ICommand ClearSmearCommand { get; }

        private void FitWindow_Click(object sender, RoutedEventArgs e) =>
            FindVisualChild<ImageEdit>(MainCanvasHost)?.FitToWindow();

        private void FitImage_Click(object sender, RoutedEventArgs e) =>
            FindVisualChild<ImageEdit>(MainCanvasHost)?.FitToImage();

        private static T? FindVisualChild<T>(DependencyObject root)
            where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
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
    }

    /// <summary>
    /// 信息栏空串兜底：插件初始 StatusMessage 为空时显示"就绪"，避免状态栏左侧一片空白。
    /// 只做文案转换，不改变 StatusLevel 三档语义。
    /// </summary>
    public sealed class StatusMessageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string text && !string.IsNullOrWhiteSpace(text)
                ? text
                : "就绪：载入参考图并框选模板区域";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>视图内按钮用的最小 ICommand 实现：只执行、不做 CanExecute 门槛。</summary>
    internal sealed class UiCommand : ICommand
    {
        private readonly Action _execute;

        public UiCommand(Action execute) => _execute = execute;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
