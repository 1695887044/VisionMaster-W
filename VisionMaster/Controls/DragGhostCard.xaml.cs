using System.Windows;
using System.Windows.Controls;

namespace VisionMaster.Controls
{
    /// <summary>
    /// 拖拽浮层卡片（设计基线 docs/UI设计基线/toolbox-drag-ghost/ v2）。
    ///
    /// 三处共用同一份视觉：工具箱拖拽源（ToolItemModel）、流程栏内部拖动（StepModel / StepCollection）、
    /// 画布节点拖动（CanvasNodeViewModel 的 Model）。所以取数只用依赖属性，不假设 DataContext 类型——
    /// 各处用 3 行 DataTemplate 把模型字段映射到 Icon / Title / Category 即可（R120：不复制视觉）。
    /// </summary>
    public partial class DragGhostCard : UserControl
    {
        /// <summary>图标码点（Font Awesome 6 Pro Solid，与工具箱条目同一套）</summary>
        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
            nameof(Icon), typeof(string), typeof(DragGhostCard), new PropertyMetadata(string.Empty));

        /// <summary>主标题（算子名 / 步骤名 / 分支名）</summary>
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(DragGhostCard), new PropertyMetadata(string.Empty));

        /// <summary>副标题（分类名 / "分支" / "共 N 个模块"）；为空时第二行收起</summary>
        public static readonly DependencyProperty CategoryProperty = DependencyProperty.Register(
            nameof(Category), typeof(string), typeof(DragGhostCard), new PropertyMetadata(string.Empty));

        public DragGhostCard()
        {
            InitializeComponent();
        }

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Category
        {
            get => (string)GetValue(CategoryProperty);
            set => SetValue(CategoryProperty, value);
        }
    }
}
