using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Controls;
using Core.Interfaces;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 框架给**没有自定义配置视图**的插件自动包的一层参数面板。
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
    /// 所以这一层的职责只有一个：**把端口逐个渲染成行**。
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
            // 清空后重建：本方法可能被外壳与本视图各调一次（谁先谁后不确定），
            // 做成幂等的比去猜调用顺序可靠。
            PortPanel.Children.Clear();

            var rows = (_plugin?.Inputs?.Values ?? (IEnumerable<IInputPort>)Array.Empty<IInputPort>())
                .Where(p => p != null)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (rows.Count == 0)
            {
                PortPanel.Children.Add(new TextBlock
                {
                    Text = "该步骤没有输入参数。",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 0, 2, 8),
                    Opacity = 0.7,
                });
                return;
            }

            PortPanel.Children.Add(BuildHintBar());

            foreach (var port in rows)
            {
                PortPanel.Children.Add(new LinkableValueEditor
                {
                    Port = port,

                    // 标签优先用描述（"延时时间(ms)"）—— 那才是写给人看的一句话；
                    // 端口名（Delay / Axis）是给流程与代码用的键，不该直接摆在界面上。
                    Label = string.IsNullOrWhiteSpace(port.Description) ? port.Name : port.Description,

                    StepData = stepData,

                    // 行距 6：编辑器的输入框是"下划线"样式，紧贴在一起会看不出哪条线属于哪个标签。
                    Margin = new Thickness(0, 0, 0, 6),

                    // 限宽：编辑器内部是「标签 | 输入框(占满剩余) | 🔗 | ✕」，
                    // 不限宽时输入框会一路铺到屏幕最右边 —— 一个填 500 的框占一米宽，
                    // 既难看出"这里该填什么"，也和旁边的控件对不齐。
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 520,
                });
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

            window.Width = 560;
            window.SizeToContent = SizeToContent.Height;

            // MinHeight 必须一起调小：外壳默认的 420 是给插件大视图留的保底高度，
            // 留着它就等于"内容 260、窗口 420"，下方依旧是一块空白。
            window.MinHeight = 180;
            window.MaxHeight = 760;   // 参数多到超过就交给本视图里的滚动条
        }

        /// <summary>
        /// 提示条（浅主色圆角条 + ℹ️）。
        ///
        /// 用 Border 包一层、而不是直接放一段文字：这句话讲的是"这个界面怎么用"，
        /// 需要和参数行拉开层次 —— 否则会被当成"又一条参数"，而它偏偏没有输入框，
        /// 看着像界面出了故障。
        /// </summary>
        private static Border BuildHintBar()
        {
            var text = new TextBlock
            {
                Text = "该步骤没有自定义配置界面，参数如下：没有绑定需求时直接填入常量，"
                       + "需要连上游变量就点右侧的链接图标。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Brush)Application.Current.FindResource("DialogAccentTextBrush"),
                MaxWidth = 470,
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = "\u2139",   // ℹ 信息图标
                FontSize = 13,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = (Brush)Application.Current.FindResource("DialogAccentBrush"),
                VerticalAlignment = VerticalAlignment.Top,
            });
            row.Children.Add(text);

            return new Border
            {
                Background = (Brush)Application.Current.FindResource("DialogAccentLightBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 14),
                Child = row,
            };
        }

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
