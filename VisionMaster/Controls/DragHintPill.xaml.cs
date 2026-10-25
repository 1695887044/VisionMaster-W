using System.Windows;
using System.Windows.Controls;
using VisionMaster.Services;

namespace VisionMaster.Controls
{
    /// <summary>
    /// 拖拽落点提示条（设计基线 docs/UI设计基线/drag-drop-hint v2）。
    ///
    /// 只做显示：文案与档位由放置端经 <see cref="DragHintPresenter"/> 写好，
    /// 本控件不判落点、不拼文案——判据只有一份（放置端的 DragOver），
    /// 否则提示与真实落点会漂移。
    /// </summary>
    public partial class DragHintPill : UserControl
    {
        /// <summary>提示整句</summary>
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(DragHintPill), new PropertyMetadata(string.Empty));

        /// <summary>状态档位（Hidden 时整条收起）</summary>
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(DragHintKind), typeof(DragHintPill),
            new PropertyMetadata(DragHintKind.Hidden));

        public DragHintPill()
        {
            InitializeComponent();
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public DragHintKind Kind
        {
            get => (DragHintKind)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }
    }
}
