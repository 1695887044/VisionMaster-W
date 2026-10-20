using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Core.Controls;
using Core.Interfaces;
using HalconDotNet;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 自动参数面板的单行。
    ///
    /// 职责只有"把一行端口画出来"：
    ///   · 左侧中文描述（<see cref="IInputPort.Description"/>，为空回退端口名）；
    ///   · 端口键 + 类型 / 必填徽章；
    ///   · 右侧 240px 编辑列（<see cref="LinkableValueEditor"/> 内置链接/清除，输入区约 190；Label 置空，标签外置）；
    ///   · 枚举/封闭候选在行尾显示锁图标。
    ///
    /// 行状态（已链接 / 不可文本输入）通过本控件上的只读依赖属性驱动外层 Border，
    /// 不改共享控件内部；这些属性只是编辑器状态的镜像，供 XAML 触发器读取。
    /// </summary>
    public partial class AutoPortRow : UserControl
    {
        private static readonly DependencyPropertyKey IsLinkedPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsLinked), typeof(bool), typeof(AutoPortRow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsLinkedProperty = IsLinkedPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey IsTextEditablePropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsTextEditable), typeof(bool), typeof(AutoPortRow),
                new PropertyMetadata(true));

        public static readonly DependencyProperty IsTextEditableProperty = IsTextEditablePropertyKey.DependencyProperty;

        private readonly LinkableValueEditor _editor;
        private readonly EventHandler _linkAddressChangedHandler;
        private readonly DependencyPropertyDescriptor? _linkAddressDescriptor;
        private bool _linkHandlerAttached;

        /// <summary>
        /// 链接地址变化（链接 / 解除链接）时触发，供上层刷新「已链接」统计。
        /// </summary>
        public event EventHandler? LinkStateChanged;

        public AutoPortRow(IInputPort port, IStepConfigData stepData)
        {
            InitializeComponent();

            // 共享控件在代码里创建：VisionMaster 里存在 VisionMaster.Core 命名空间，
            // 若在 XAML 里直接写 clr-namespace:Core.Controls，生成代码会按相对命名空间
            // 解析成 VisionMaster.Core.Controls（CS0234）。这里用 ContentControl 承载，
            // 视觉与契约完全不变（仍是唯一的 LinkableValueEditor 编辑入口）。
            _editor = new LinkableValueEditor
            {
                Height = 36,
                VerticalAlignment = VerticalAlignment.Center,
                Label = string.Empty,
            };
            EditorHost.Content = _editor;

            _editor.Port = port;
            _editor.StepData = stepData;

            SetValue(IsTextEditablePropertyKey, _editor.IsTextEditable);
            SetValue(IsLinkedPropertyKey, _editor.IsLinked);

            var label = string.IsNullOrWhiteSpace(port.Description) ? port.Name : port.Description;
            LabelText.Text = label ?? string.Empty;
            PortKeyText.Text = port.Name ?? string.Empty;
            TypeText.Text = GetTypeLabel(port.DataType);

            var required = port.IsRequired;
            RequiredBadge.Visibility = required ? Visibility.Visible : Visibility.Collapsed;
            OptionalBadge.Visibility = required ? Visibility.Collapsed : Visibility.Visible;

            // 枚举 / IsFunctionalEnum 的共同点是"合法值是一个封闭集合"，只能选、不能链接。
            // LinkableValueEditor 自己会隐藏 🔗 / ✕，这里只补一个行尾锁图标解释原因。
            LockIcon.Visibility = IsEnumPort(port) ? Visibility.Visible : Visibility.Collapsed;

            _linkAddressChangedHandler = OnEditorLinkAddressChanged;
            _linkAddressDescriptor = DependencyPropertyDescriptor.FromProperty(
                LinkableValueEditor.LinkAddressProperty,
                typeof(LinkableValueEditor));

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>本行承载的端口（供上层统计使用）。</summary>
        public IInputPort? Port => _editor.Port;

        /// <summary>
        /// 复用行时刷新步骤数据（Initialize 被外壳与视图各调一次，端口不变就不重建行，
        /// 避免 LinkableValueEditor 对 IInputPort.ValueChanged 的订阅累积）。
        /// </summary>
        public void RefreshStepData(IStepConfigData stepData)
        {
            _editor.StepData = stepData;
            SetValue(IsLinkedPropertyKey, _editor.IsLinked);
            SetValue(IsTextEditablePropertyKey, _editor.IsTextEditable);
        }

        /// <summary>当前是否已链接（编辑器状态的只读镜像，供行样式触发器使用）。</summary>
        public bool IsLinked => (bool)GetValue(IsLinkedProperty);

        /// <summary>当前端口是否支持文本输入（编辑器状态的只读镜像，供行样式触发器使用）。</summary>
        public bool IsTextEditable => (bool)GetValue(IsTextEditableProperty);

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_linkHandlerAttached || _linkAddressDescriptor == null) return;
            _linkAddressDescriptor.AddValueChanged(_editor, _linkAddressChangedHandler);
            _linkHandlerAttached = true;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_linkHandlerAttached || _linkAddressDescriptor == null) return;
            _linkAddressDescriptor.RemoveValueChanged(_editor, _linkAddressChangedHandler);
            _linkHandlerAttached = false;
        }

        private void OnEditorLinkAddressChanged(object? sender, EventArgs e)
        {
            SetValue(IsLinkedPropertyKey, _editor.IsLinked);
            LinkStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private static bool IsEnumPort(IInputPort port) =>
            port.DataType?.IsEnum == true || port.IsFunctionalEnum;

        /// <summary>
        /// 类型徽章文案：只做"人话分类"，不显示程序集类型名。
        /// 映射口径见设计基线 README（HImage→图像 / HRegion→区域 / enum→枚举 / ...）。
        /// </summary>
        private static string GetTypeLabel(Type? type)
        {
            if (type == null) return "参数";
            if (type == typeof(HImage)) return "图像";
            if (type == typeof(HRegion)) return "区域";
            if (type.IsEnum) return "枚举";
            if (type == typeof(bool)) return "布尔";
            if (type == typeof(int) || type == typeof(long)) return "整数";
            if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return "数值";
            if (type == typeof(string)) return "文本";
            return "参数";
        }
    }
}
