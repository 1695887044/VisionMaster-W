using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VisionMaster.Services
{
    /// <summary>
    /// 落点提示条的状态档位（设计基线 docs/UI设计基线/drag-drop-hint v2 §03）。
    /// 档位只决定配色与图标，文案由放置端拼好整句塞进 <see cref="DragHintState.Text"/>。
    /// </summary>
    public enum DragHintKind
    {
        /// <summary>没有落点：整条收起（不是变灰），只留拖拽卡片</summary>
        Hidden,

        /// <summary>有效落点（天蓝）：允许放下，且不改变视图状态</summary>
        Valid,

        /// <summary>需注意（琥珀）：允许放下，但有副作用（如会展开折叠的容器）</summary>
        Caution,

        /// <summary>非法落点（红）：不允许放下，文案必须说清"怎么才能放"</summary>
        Illegal,

        /// <summary>运行锁（灰）：不是用户的错，不判红</summary>
        Locked,
    }

    /// <summary>
    /// 落点提示条的共享状态（单写者：放置端的 DragOver / DragLeave / Drop）。
    ///
    /// 【为什么需要一个共享对象，而不是把提示塞进拖拽卡片的 DataContext】
    /// 卡片由**拖拽源**驱动（只知道"拖的是什么"），落点由**放置端**决定——两者在 gong 里是
    /// 两条不同的链路（DragAdornerTemplate 绑源数据，IDropTarget 拿 DropInfo）。
    /// gong 4.0.0 自带的 DropHint 只有 None/Active/Error 三态、模板 DataContext 只有
    /// HintText + HintState，表达不了"需注意 / 运行锁"两档，所以提示条改由本对象驱动，
    /// 与卡片放进同一个 adorner 模板里（视觉位置与设计稿一致）。
    ///
    /// 【生命周期纪律】写入方必须成对：DragOver 里 Show，DragLeave / Drop / 拖拽取消里 Hide。
    /// 漏了 Hide 的后果是"拖完一条提示条永远挂在光标旁"，所以 <see cref="Reset"/> 也供
    /// 拖拽源在 DragDropOperationFinished / DragCancelled 时兜底调用。
    ///
    /// 【不是模型状态】纯瞬时 UI 状态：不落盘、不进撤销栈、不参与 Version、不跨线程。
    /// 拖拽全程在 UI 线程，因此不加锁。
    /// </summary>
    public sealed class DragHintState : INotifyPropertyChanged
    {
        private string _text = string.Empty;
        private DragHintKind _kind = DragHintKind.Hidden;
        private string? _owner;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>提示整句（放置端按设计稿 §04 的公式拼好，模板只负责显示）</summary>
        public string Text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value ?? string.Empty;
                Raise();
            }
        }

        /// <summary>状态档位（决定配色与图标）</summary>
        public DragHintKind Kind
        {
            get => _kind;
            set
            {
                if (_kind == value) return;
                _kind = value;
                Raise();
                Raise(nameof(IsVisible));
            }
        }

        /// <summary>
        /// 当前提示的写入方标识（"流程栏" / "画布"）。
        /// gong 的 DragLeave 是 Dispatcher 延迟派发的，从流程栏移到画布时，
        /// 流程栏那条 DragLeave 可能排在画布 DragOver 之后——不带 owner 的 Hide 会把
        /// 画布刚写的提示抹掉（用户停住不动就看不到提示）。DragLeave 只撤自己的。
        /// </summary>
        public string? Owner
        {
            get => _owner;
            set
            {
                if (_owner == value) return;
                _owner = value;
                Raise();
            }
        }

        /// <summary>模板据此收起整条（Hidden 时不占位、不显示空底色）</summary>
        public bool IsVisible => _kind != DragHintKind.Hidden;

        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// 落点提示条的全局入口。单例状态挂在 <see cref="Current"/> 上供 XAML 绑定
    /// （<c>{Binding Source={x:Static services:DragHintPresenter.Current}, Path=Text}</c>）。
    /// </summary>
    public static class DragHintPresenter
    {
        /// <summary>流程栏放置端的标识（DragLeave 只撤自己写的提示）</summary>
        public const string ProcessTreeOwner = "process-tree";

        /// <summary>画布放置端的标识</summary>
        public const string CanvasOwner = "canvas";

        /// <summary>唯一的提示状态实例（拖拽全程单写者，见 <see cref="DragHintState"/> 注释）</summary>
        public static DragHintState Current { get; } = new();

        /// <summary>显示一条提示。文案为空视为"没有落点"，直接收起。</summary>
        public static void Show(string? owner, string? text, DragHintKind kind)
        {
            if (string.IsNullOrWhiteSpace(text) || kind == DragHintKind.Hidden)
            {
                Hide(owner);
                return;
            }

            Current.Owner = owner;
            Current.Text = text!;
            Current.Kind = kind;
        }

        /// <summary>
        /// 只收起"自己写的"那条提示（DragLeave 用）。当前提示已被别的放置端接管时什么都不做——
        /// 否则跨放置端拖动会被延迟派发的 DragLeave 抹掉刚写好的提示。
        /// </summary>
        public static void Hide(string? owner)
        {
            if (!string.Equals(Current.Owner, owner, StringComparison.Ordinal))
                return;

            Current.Text = string.Empty;
            Current.Kind = DragHintKind.Hidden;
        }

        /// <summary>
        /// 无条件收起（Drop / 拖拽开始复位用）。文案一并清空：否则下一次拖拽在第一次
        /// DragOver 之前会短暂显示上一次的落点文案。
        /// </summary>
        public static void Hide()
        {
            Current.Text = string.Empty;
            Current.Owner = null;
            Current.Kind = DragHintKind.Hidden;
        }

        /// <summary>
        /// 兜底复位：拖拽整体结束时调用（与 <see cref="Hide()"/> 等价，语义上区分
        /// "本次拖拽结束"与"暂时离开某个放置端"）。
        /// </summary>
        public static void Reset() => Hide();
    }
}
