using System;

namespace VisionMaster.Services
{
    /// <summary>
    /// 组态侧的「选画面」入口：把"挑一个本方案里的画面"这件事收成一个回调式的方法。
    ///
    /// 为什么与 <see cref="IScadaVariablePicker"/> 分开，而不是合成一个"选个东西"的万能接口
    /// ---------
    /// 两者清单来源不同（变量来自工作区、画面来自当前方案的文档），列也不同
    /// （变量有类型/来源/当前值，画面有尺寸/图元数）。合成一个就得在接口上开一个"选什么"的参数，
    /// 调用方（面板与事件行）反而要先判断自己该传哪一档——把类型判断从 UI 挪到了接口上，没省任何东西。
    /// 两个各自只有一个方法的接口，比一个带枚举开关的大接口好懂，也好换实现。
    ///
    /// 与 <see cref="IScadaVariablePicker"/> 同源的另外两条理由（面板不碰 IDialogService、
    /// 弹窗怎么弹是应用层的事）见那个接口的注释，此处不再重复。
    /// </summary>
    public interface IScadaPagePicker
    {
        /// <summary>
        /// 弹出画面选择框。用户确认时回调 <paramref name="onPicked"/>（回传稳定 Id 与当时的名字）；
        /// 取消则一次都不回调。<b>本方法不阻塞</b>——回调发生在弹窗关闭的那一刻。
        /// </summary>
        /// <param name="currentId">当前已选的目标画面 Id（<c>Guid.Empty</c> = 还没选），用于打开时预选</param>
        /// <param name="currentName">当前已选的目标画面名（旧数据只有名字时靠它预选）</param>
        /// <param name="onPicked">确认后的落点：(画面 Id, 画面名)</param>
        void Pick(Guid currentId, string? currentName, Action<Guid, string?> onPicked);
    }
}
