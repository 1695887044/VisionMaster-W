using Core.Events;
using Core.Interfaces;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Core.Controls
{
    /// <summary>
    /// 可链接参数编辑器（固定值 / 变量链接 二合一控件，VisionMaster 风格）
    /// 布局：标签 + 下划线输入框 + 链接(🔗) / 清除(✕) 图标按钮
    /// - 直接绑定 InputPort：端口名/类型/值/链接状态均从端口读取
    /// - 端口类型驱动控件形态：
    ///   可文本输入类型（string/int/double/enum...）→ 输入框 + 链接按钮 + 清除按钮
    ///   不可文本输入类型（HImage/byte[]...）     → 占位提示 + 链接按钮（未链接时无清除按钮）
    /// - 链接动作通过 GlobalEventBus 发布 LinkPathEvent，由主程序呼出变量绑定弹窗完成
    /// </summary>
    public partial class LinkableValueEditor : UserControl
    {
        #region 依赖属性

        /// <summary>
        /// 绑定的输入端口（端口即数据成员：名称/类型/值/链接状态均从端口读取）
        /// </summary>
        public static readonly DependencyProperty PortProperty =
            DependencyProperty.Register(
                nameof(Port), typeof(IInputPort), typeof(LinkableValueEditor),
                new FrameworkPropertyMetadata(null, OnPortChanged));

        public IInputPort Port
        {
            get => (IInputPort)GetValue(PortProperty);
            set => SetValue(PortProperty, value);
        }

        /// <summary>
        /// 左侧标签文本（如 "延时时间(ms)"），为空时不显示
        /// </summary>
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(
                nameof(Label), typeof(string), typeof(LinkableValueEditor),
                new PropertyMetadata(null, OnLabelChanged));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        /// <summary>
        /// 当前链接显示地址（如 "Global.CT" 或 "上游步骤.端口名"），为空表示未链接
        /// </summary>
        public static readonly DependencyProperty LinkAddressProperty =
            DependencyProperty.Register(
                nameof(LinkAddress), typeof(string), typeof(LinkableValueEditor),
                new FrameworkPropertyMetadata(null, OnLinkAddressChanged));

        public string LinkAddress
        {
            get => (string)GetValue(LinkAddressProperty);
            set => SetValue(LinkAddressProperty, value);
        }

        /// <summary>
        /// 是否已链接（由 LinkAddress 派生，只读）
        /// </summary>
        private static readonly DependencyPropertyKey IsLinkedPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsLinked), typeof(bool), typeof(LinkableValueEditor),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsLinkedProperty = IsLinkedPropertyKey.DependencyProperty;

        public bool IsLinked
        {
            get => (bool)GetValue(IsLinkedProperty);
            private set => SetValue(IsLinkedPropertyKey, value);
        }

        /// <summary>
        /// 端口当前是否支持文本输入（由 Port.DataType 驱动，只读）
        /// string/基元类型/enum/decimal → true；HImage/byte[]/复杂类型 → false
        /// </summary>
        private static readonly DependencyPropertyKey IsTextEditablePropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsTextEditable), typeof(bool), typeof(LinkableValueEditor),
                new PropertyMetadata(true));

        public static readonly DependencyProperty IsTextEditableProperty = IsTextEditablePropertyKey.DependencyProperty;

        public bool IsTextEditable
        {
            get => (bool)GetValue(IsTextEditableProperty);
            private set => SetValue(IsTextEditablePropertyKey, value);
        }

        /// <summary>
        /// 端口当前值的字符串形式（供 XAML 双向绑定；内部桥接到 Port.Value）
        /// </summary>
        public static readonly DependencyProperty PortValueProperty =
            DependencyProperty.Register(
                nameof(PortValue), typeof(string), typeof(LinkableValueEditor),
                new FrameworkPropertyMetadata(string.Empty, OnPortValueChanged));

        public string PortValue
        {
            get => (string)GetValue(PortValueProperty);
            set => SetValue(PortValueProperty, value);
        }

        /// <summary>
        /// 步骤配置数据（IStepConfigData，绑定宿主 ViewModel 的 StepData 属性），必填
        /// </summary>
        public static readonly DependencyProperty StepDataProperty =
            DependencyProperty.Register(
                nameof(StepData), typeof(IStepConfigData), typeof(LinkableValueEditor),
                new FrameworkPropertyMetadata(null, OnRestoreStateChanged));

        public IStepConfigData StepData
        {
            get => (IStepConfigData)GetValue(StepDataProperty);
            set => SetValue(StepDataProperty, value);
        }

        /// <summary>
        /// 可选的浏览命令（如打开文件/文件夹对话框），设置后双击下划线区域触发
        /// </summary>
        public static readonly DependencyProperty BrowseCommandProperty =
            DependencyProperty.Register(
                nameof(BrowseCommand), typeof(ICommand), typeof(LinkableValueEditor),
                new PropertyMetadata(null));

        public ICommand BrowseCommand
        {
            get => (ICommand)GetValue(BrowseCommandProperty);
            set => SetValue(BrowseCommandProperty, value);
        }

        #endregion

        public LinkableValueEditor()
        {
            InitializeComponent();
            UpdateButtonVisibility();

            // 未链接时双击输入框触发浏览命令（可选）
            PART_ValueBox.MouseDoubleClick += (s, e) =>
            {
                if (!IsLinked && IsTextEditable)
                    BrowseCommand?.Execute(null);
            };
        }

        #region 内部逻辑

        private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (LinkableValueEditor)d;
            var text = (string)e.NewValue;
            editor.PART_Label.Text = text;
            editor.PART_Label.Visibility = string.IsNullOrEmpty(text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private static void OnLinkAddressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (LinkableValueEditor)d;
            var linked = !string.IsNullOrEmpty((string)e.NewValue);
            editor.SetValue(IsLinkedPropertyKey, linked);
            editor.UpdateButtonVisibility();
        }

        private static void OnPortChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = (LinkableValueEditor)d;
            var port = e.NewValue as IInputPort;
            if (port == null)
            {
                editor.IsTextEditable = true;
                return;
            }

            // 端口类型驱动可输入性
            editor.IsTextEditable = IsTextEditableType(port.DataType);

            // 订阅端口值变化，同步刷新输入框显示
            port.ValueChanged -= editor.OnPortValueChanged;
            port.ValueChanged += editor.OnPortValueChanged;

            editor.SyncPortValue();
            editor.RestoreLinkState();
            editor.UpdateButtonVisibility();
        }

        private static void OnRestoreStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LinkableValueEditor)d).RestoreLinkState();
        }

        private static void OnPortValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 输入框编辑 → 写回端口
            var editor = (LinkableValueEditor)d;
            if (editor.Port == null || !editor.IsTextEditable) return;
            var text = (string)e.NewValue;
            try
            {
                editor.Port.Value = string.IsNullOrEmpty(text)
                    ? GetDefaultValue(editor.Port.DataType)
                    : Convert.ChangeType(text, editor.Port.DataType);
            }
            catch { /* 类型转换失败忽略，保留原值 */ }
        }

        private void OnPortValueChanged(object sender, EventArgs e)
        {
            // 端口外部变更（如链接灌值）→ 同步输入框
            if (IsLinked) return;
            SyncPortValue();
        }

        /// <summary>
        /// 判断端口类型是否支持文本输入
        /// 白名单：string、基元类型（int/double/bool/...）、enum、decimal
        /// </summary>
        private static bool IsTextEditableType(Type type)
        {
            if (type == null) return false;
            return type == typeof(string)
                || type.IsPrimitive
                || type.IsEnum
                || type == typeof(decimal);
        }

        /// <summary>
        /// 从端口读取当前值，刷新 PortValue 显示
        /// </summary>
        private void SyncPortValue()
        {
            if (Port == null) return;
            var val = Port.Value;
            SetCurrentValue(PortValueProperty, val?.ToString() ?? string.Empty);
        }

        private void RestoreLinkState()
        {
            if (StepData == null || Port == null) return;
            SetCurrentValue(LinkAddressProperty, StepData.GetLinkedAddress(Port.Name));
        }

        /// <summary>
        /// 更新占位提示和按钮可见性（由 IsLinked / IsTextEditable 驱动）
        /// </summary>
        private void UpdateButtonVisibility()
        {
            // 占位提示：不可文本输入 且 未链接 时显示
            PART_Placeholder.Visibility =
                (!IsTextEditable && !IsLinked) ? Visibility.Visible : Visibility.Collapsed;

            // ✕ 按钮：不可文本输入 且 未链接 时隐藏（没有值可清空，只能链接）
            PART_UnlinkBtn.Visibility =
                (!IsTextEditable && !IsLinked) ? Visibility.Collapsed : Visibility.Visible;
        }

        private static object GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        #endregion

        #region 事件处理

        private void LinkBtn_Click(object sender, RoutedEventArgs e)
        {
            var stepData = StepData;
            var port = Port;
            if (stepData == null || port == null) return;

            // 发布链接请求，主程序呼出 DataBindView 单绑定模式，选中后回调
            GlobalEventBus.Publish(new LinkPathEvent
            {
                InputPortName = port.Name,
                TargetType = port.DataType ?? typeof(object),
                OnBound = link =>
                {
                    stepData.SetLink(port.Name, link);
                    SetCurrentValue(LinkAddressProperty, link.DisplayAddress);
                }
            });
        }

        private void UnlinkBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IsLinked)
            {
                // 已链接：解除链接，恢复固定值编辑
                StepData?.RemoveLink(Port.Name);
                SetCurrentValue(LinkAddressProperty, null);
                SyncPortValue(); // 恢复显示端口当前固定值
            }
            else
            {
                // 未链接：清空固定值
                SetCurrentValue(PortValueProperty, string.Empty);
            }
        }

        #endregion
    }
}
