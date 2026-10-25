using System.Windows;
using System.Windows.Controls;
using VisionMaster.Services;

namespace VisionMaster.Controls
{
    /// <summary>
    /// 拖拽浮层组合控件：拖拽卡片 + 落点提示条（设计基线 docs/UI设计基线/drag-drop-hint v2）。
    ///
    /// 只为把"两处拖拽源各写一遍 StackPanel"收成一处（R120/R141）：
    /// 模板里仍只映射 Icon / Title / Category，提示条自己从
    /// <see cref="Services.DragHintPresenter.Current"/> 取数。
    ///
    /// 覆盖范围：工具箱 → 流程栏 / 画布（gong 拖拽）。画布内部拖节点用的是视图自己的 Popup，
    /// 不经过本控件（设计稿 §08 已列为不覆盖）。
    /// </summary>
    public partial class DragGhostStack : UserControl
    {
        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
            nameof(Icon), typeof(string), typeof(DragGhostStack), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(DragGhostStack), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty CategoryProperty = DependencyProperty.Register(
            nameof(Category), typeof(string), typeof(DragGhostStack), new PropertyMetadata(string.Empty));

        public DragGhostStack()
        {
            InitializeComponent();

            // 拖拽开始的钩子：本控件由 gong 在**每次拖拽开始时**实例化（拖影 adorner 的内容），
            // 所以 Loaded 等价于"新一轮拖拽开始"。在这里复位，能清掉上一轮可能残留的提示
            // （例如上一轮在非法落点上松手：DragOver 把 Effects 设成 None 后 gong 不再派发
            //  Drop / DragLeave，我们那侧就没有任何"收尾"回调可挂）。
            Loaded += (_, _) => DragHintPresenter.Reset();
        }

        /// <summary>算子图标字形（Font Awesome 码点）</summary>
        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        /// <summary>卡片标题（算子名 / 分支显示名）</summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>卡片分类行（为空时卡片自动收起第二行）</summary>
        public string Category
        {
            get => (string)GetValue(CategoryProperty);
            set => SetValue(CategoryProperty, value);
        }
    }
}
