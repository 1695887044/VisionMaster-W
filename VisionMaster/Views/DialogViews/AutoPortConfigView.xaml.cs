using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Core.Controls;
using Core.Interfaces;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 框架给**没有自定义配置视图**的插件自动包的一层参数面板（Web 设计基线 v1）。
    ///
    /// 【要解决的问题】
    /// 插件不实现 <see cref="IPluginCustomViewProvider"/> 时，此前是回退到「变量绑定」窗口
    ///（DataBindView），而那个窗口是"先选左边端口、再在右边选上游"的两步式交互 ——
    /// 只想填个常量也得绕一圈，端口一多更难用。
    /// 这里改成"**参数清单**"：一次把所有输入端口列出来，每行 = 标签 + 值输入框 + 🔗（链接）
    /// + ✕（清除），见 <see cref="LinkableValueEditor"/>。没有绑定需求就直接在框里填常量。
    ///
    /// 【为什么几乎不用写逻辑】
    /// 配置生命周期本就属于插件基类，不在宿主：
    /// <see cref="VisionPluginBase.Initialize(IStepConfigData)"/> 把 InputValues 灌进端口，
    /// <see cref="VisionPluginBase.OnConfirm(IStepConfigData)"/> 把端口值写回 InputValues。
    /// <see cref="LinkableValueEditor"/> 又能直接吃 <see cref="IInputPort"/> 并自己读写
    ///（含发布 <see cref="LinkPathEvent"/> 请主程序呼出绑定弹窗、自己处理清除）。
    /// 所以这一层的职责只有两个：**把端口逐个渲染成行**、**把步骤上下文与统计显示出来**。
    ///
    /// 【只列输入端口，不列 [StepConfig] 常量】
    /// 这是刻意的，不是遗漏：本面板只在"插件没有自定义视图"时启用，
    /// 而这类插件的参数**必须**是 <see cref="InputPort{T}"/> 才能被这里编辑到 ——
    /// 标成 [StepConfig] 的参数在这里没有形态可渲染，等于没有入口（历史上就是这么丢的：
    /// 「轴运动」的绝对/相对切换在界面上一直改不了）。有自定义视图的插件不受影响。
    /// </summary>
    public partial class AutoPortConfigView : UserControl, IPluginConfigView
    {
        private readonly VisionPluginBase? _plugin;
        private IStepConfigData? _stepData;

        public AutoPortConfigView(IVisionPlugin plugin)
        {
            InitializeComponent();

            // 窗口尺寸由**本视图**负责，而不是让外壳去猜内容类型。
            //
            // 上一版把判断写在外壳的 Loaded 里（UiContentHost.Content is AutoPortConfigView），
            // 结果没生效：Loaded 触发时那段绑定可能还没把内容挂上，判断落空 → 尺寸没改 →
            // 表现就是"只有一条参数，窗口照样一大块空白"。
            // 放在本视图自己的 Loaded 里则必然成立：控件已经进了可视树，父窗口一定拿得到。
            Loaded += OnLoaded;

            // 端口实例必须来自**插件实例**，而不是 PluginService 扫描出的 PortDefinition 快照：
            // 编辑器要读写端口上的实时值、并把这个实例的端口连到上游，
            // 只有它才和本次编辑是同一份数据（快照里那份在方案加载前就生成好了，值是空的）。
            _plugin = plugin as VisionPluginBase;
        }

        public void Initialize(IStepConfigData stepData)
        {
            _stepData = stepData;

            var rows = GetPorts();
            var existing = PortPanel.Children.OfType<AutoPortRow>().ToList();

            // 外壳与视图都可能调一次 Initialize（谁先谁后不确定）。
            // 端口集合与顺序都没变时**复用现有行**：LinkableValueEditor 在设置 Port 时会
            // 订阅 IInputPort.ValueChanged，但共享控件没有卸载退订入口；重建行会让旧编辑器
            // 继续挂在同一个端口事件上，调用两次就多一份订阅。复用是这里最稳的幂等做法。
            if (existing.Count == rows.Count
                && existing.Select(r => r.Port).SequenceEqual(rows))
            {
                foreach (var row in existing)
                    row.RefreshStepData(stepData);
                UpdateStepCard(stepData, rows);
                UpdateSectionState(rows.Count > 0);
                if (rows.Count == 0)
                    UpdateEmptyState();
                return;
            }

            // 端口集合变了才清空重建；旧行先退订本视图的统计回调。
            foreach (var oldRow in existing)
                oldRow.LinkStateChanged -= OnRowLinkStateChanged;
            PortPanel.Children.Clear();

            UpdateStepCard(stepData, rows);
            UpdateSectionState(rows.Count > 0);

            if (rows.Count == 0)
            {
                UpdateEmptyState();
                return;
            }

            foreach (var port in rows)
            {
                // 行的标签优先用 port.Description，为空回退 port.Name（见 AutoPortRow）。
                var row = new AutoPortRow(port, stepData);
                row.LinkStateChanged += OnRowLinkStateChanged;
                PortPanel.Children.Add(row);
            }
        }

        /// <summary>
        /// 进入可视树后，把宿主窗口调成"按内容定高"。
        ///
        /// 外壳默认尺寸是为**插件自带视图**准备的固定大画布（创建 ROI 那类需要足够绘制区），
        /// 套在只有几行的参数面板上就是"一条输入、一屏空白"。
        /// 插件自带视图不受影响 —— 那段逻辑在它那边根本不会执行。
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;   // 只需首次进入可视树时调一次

            if (Window.GetWindow(this) is not { } window) return;

            // MinWidth 必须一起调小：PluginConfigShellView 的 WindowStyle 给插件自带视图
            // 留了 640 的保底宽度；不改它，Width=560 会被外壳钳成 640（设计基线 v1 的 560 就落不了地）。
            window.MinWidth = 560;
            window.Width = 560;
            window.SizeToContent = SizeToContent.Height;

            // MinHeight 必须一起调小：外壳默认的 560 是给插件大视图留的保底高度，
            // 留着它就等于"内容 260、窗口 560"，下方依旧是一块空白。
            window.MinHeight = 180;
            window.MaxHeight = 760;   // 参数多到超过就交给本视图里的滚动条
        }

        /// <summary>
        /// 实时端口集合（按 Name 排序）。
        ///
        /// 必须取插件实例上的端口，不能取 PluginService 的 PortDefinition 快照 ——
        /// 编辑器要读写端口实时值、并把链接挂到这个实例上，快照里的值是空的。
        /// </summary>
        private List<IInputPort> GetPorts() =>
            (_plugin?.Inputs?.Values ?? (IEnumerable<IInputPort>)Array.Empty<IInputPort>())
                .Where(p => p != null)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>步骤卡：图标 + 名称 + 静态副标题 + 三个统计。</summary>
        private void UpdateStepCard(IStepConfigData? stepData, IReadOnlyList<IInputPort> ports)
        {
            StepIconText.Text = string.IsNullOrWhiteSpace(stepData?.Icon) ? "\uf110" : stepData.Icon;
            StepNameText.Text = string.IsNullOrWhiteSpace(stepData?.StepName) ? "插件配置" : stepData.StepName;
            UpdateMetrics(stepData, ports);
        }

        /// <summary>输入数 / 必填数 / 已链接数（链接判据与 LinkableValueEditor 同源）。</summary>
        private void UpdateMetrics(IStepConfigData? stepData, IReadOnlyList<IInputPort> ports)
        {
            InputMetricText.Text = ports.Count.ToString(CultureInfo.InvariantCulture);
            RequiredMetricText.Text = ports.Count(p => p.IsRequired).ToString(CultureInfo.InvariantCulture);
            LinkedMetricText.Text = ports.Count(p => IsPortLinked(stepData, p.Name)).ToString(CultureInfo.InvariantCulture);
            SectionCountText.Text = ports.Count.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>列表 / 空态切换：空态时区块头与清单一起收起。</summary>
        private void UpdateSectionState(bool hasPorts)
        {
            SectionHeader.Visibility = hasPorts ? Visibility.Visible : Visibility.Collapsed;
            PortListBorder.Visibility = hasPorts ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = hasPorts ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// 空态文案：插件实例没注入时说明"读不到端口"，而不是让用户以为插件真的没有参数。
        /// </summary>
        private void UpdateEmptyState()
        {
            if (_plugin == null)
            {
                EmptyTitleText.Text = "无法读取插件参数";
                EmptyDescriptionText.Text =
                    "插件实例未注入，无法读取输入端口；确认 / 取消 / 执行入口保持可用。";
                return;
            }

            EmptyTitleText.Text = "该步骤没有输入参数";
            EmptyDescriptionText.Text =
                "插件没有声明任何输入端口，因此没有可编辑的参数。仍可执行、确认或取消；"
                + "若该插件应有参数，请检查插件是否把配置写成了 [StepConfig]（本版兜底面板只渲染 InputPort）。";
        }

        /// <summary>
        /// 链接 / 解除链接后刷新「已链接」统计。
        /// 行背景由 AutoPortRow 自己的 DataTrigger 跟随，不在这里处理。
        /// </summary>
        private void OnRowLinkStateChanged(object? sender, EventArgs e)
        {
            if (_stepData == null) return;
            UpdateMetrics(_stepData, GetPorts());
        }

        /// <summary>
        /// 与 LinkableValueEditor.RestoreLinkState 同一口径：先看 IsLinked，再兜底看地址非空。
        /// </summary>
        private static bool IsPortLinked(IStepConfigData? stepData, string portName) =>
            stepData != null
            && (stepData.IsLinked(portName) || !string.IsNullOrEmpty(stepData.GetLinkedAddress(portName)));

        /// <summary>
        /// 确认：写回步骤配置。
        ///
        /// 交给插件基类的默认实现，而不是在这里自己遍历端口写 ——
        /// 那是框架统一的写路径（<see cref="IStepConfigData.SetInputValue"/>），
        /// 它还会触发变更通知以保证流程版本递增（否则改完不重编译，等于没改）。
        /// </summary>
        public void OnConfirm(IStepConfigData stepData) => _plugin?.OnConfirm(stepData);

        /// <summary>取消：丢弃本次修改（基类默认实现不落盘）</summary>
        public void OnCancel() => _plugin?.OnCancel();
    }
}
