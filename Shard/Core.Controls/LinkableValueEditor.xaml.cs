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
    /// - 未链接：输入框手动编辑固定值；✕ 清空输入
    /// - 已链接：输入框变为只读链接地址；🔗 改链、✕ 解除链接
    /// - 链接动作通过 GlobalEventBus 发布 LinkPathEvent，由主程序呼出变量绑定弹窗完成
    /// - 绑定结果直接写入 StepData.SetLink(PortName, link)，随流程持久化
    /// </summary>
    public partial class LinkableValueEditor : UserControl
    {
        #region 依赖属性

        /// <summary>
        /// 固定值（未链接时使用，TwoWay 绑定到宿主 ViewModel 属性）
        /// </summary>
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value), typeof(string), typeof(LinkableValueEditor),
                new FrameworkPropertyMetadata(
                    string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
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
        /// 目标输入端口名（必须与插件 InputPort.Name 一致），必填
        /// </summary>
        public static readonly DependencyProperty PortNameProperty =
            DependencyProperty.Register(
                nameof(PortName), typeof(string), typeof(LinkableValueEditor),
                new FrameworkPropertyMetadata(null, OnRestoreStateChanged));

        public string PortName
        {
            get => (string)GetValue(PortNameProperty);
            set => SetValue(PortNameProperty, value);
        }

        /// <summary>
        /// 端口期望的数据类型（用于绑定弹窗类型校验），默认 object
        /// XAML 中用 PortType="{x:Type system:String}" 形式指定
        /// </summary>
        public static readonly DependencyProperty PortTypeProperty =
            DependencyProperty.Register(
                nameof(PortType), typeof(Type), typeof(LinkableValueEditor),
                new PropertyMetadata(typeof(object)));

        public Type PortType
        {
            get => (Type)GetValue(PortTypeProperty);
            set => SetValue(PortTypeProperty, value);
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

            // 未链接时双击输入框触发浏览命令（可选）
            PART_ValueBox.MouseDoubleClick += (s, e) =>
            {
                if (!IsLinked)
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
            editor.SetValue(IsLinkedPropertyKey, !string.IsNullOrEmpty((string)e.NewValue));
        }

        private static void OnRestoreStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // StepData / PortName 就绪后，从步骤配置恢复链接状态
            ((LinkableValueEditor)d).RestoreLinkState();
        }

        private void RestoreLinkState()
        {
            if (StepData == null || string.IsNullOrEmpty(PortName))
                return;

            SetCurrentValue(LinkAddressProperty, StepData.GetLinkedAddress(PortName));
        }

        #endregion

        #region 事件处理

        private void LinkBtn_Click(object sender, RoutedEventArgs e)
        {
            var stepData = StepData;
            var portName = PortName;
            if (stepData == null || string.IsNullOrEmpty(portName))
                return;

            // 发布链接请求，主程序呼出 DataBindView 单绑定模式，选中后回调
            GlobalEventBus.Publish(new LinkPathEvent
            {
                InputPortName = portName,
                TargetType = PortType ?? typeof(object),
                OnBound = link =>
                {
                    stepData.SetLink(portName, link);
                    SetCurrentValue(LinkAddressProperty, link.DisplayAddress);
                }
            });
        }

        private void UnlinkBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IsLinked)
            {
                // 已链接：解除链接，恢复固定值编辑
                StepData?.RemoveLink(PortName);
                SetCurrentValue(LinkAddressProperty, null);
            }
            else
            {
                // 未链接：清空固定值
                SetCurrentValue(ValueProperty, string.Empty);
            }
        }

        #endregion
    }
}
