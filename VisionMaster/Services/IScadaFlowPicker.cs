using System;

namespace VisionMaster.Services
{
    /// <summary>
    /// 组态侧的「选流程」入口：把"挑一条本方案里的流程"这件事收成一个回调式的方法。
    ///
    /// 为什么与 <see cref="IScadaVariablePicker"/> / <see cref="IScadaPagePicker"/> 分开，
    /// 而不是合成一个"选个东西"的万能接口
    /// ---------
    /// 三者清单来源不同（变量来自工程变量表、画面来自当前方案的组态文档、流程来自当前方案的
    /// 流程集合），列也不同（变量有类型/来源/当前值，画面有尺寸/图元数，流程有角色/启停态）。
    /// 合成一个就得在接口上开一个"选什么"的参数，调用方反而要先判断自己该传哪一档——
    /// 把类型判断从 UI 挪到了接口上，没省任何东西。
    ///
    /// 目标身份为什么是 <c>string</c> 而不是 <see cref="Guid"/>
    /// ---------
    /// 流程模型的身份键 FlowID 本来就是字符串（Guid 的 "N" 格式文本，见 FlowModel），
    /// 与 <see cref="IScadaFlowRunner"/> 的寻址参数同型——这里原样过手，不做来回转换。
    ///
    /// 与另两个 picker 同源的另外两条理由（面板不碰 IDialogService、
    /// 弹窗怎么弹是应用层的事）见 <see cref="IScadaVariablePicker"/> 的注释，此处不再重复。
    /// </summary>
    public interface IScadaFlowPicker
    {
        /// <summary>
        /// 弹出流程选择框。用户确认时回调 <paramref name="onPicked"/>（回传稳定 Id 与当时的名字）；
        /// 取消则一次都不回调。<b>本方法不阻塞</b>——回调发生在弹窗关闭的那一刻。
        /// </summary>
        /// <param name="currentFlowId">当前已选的目标流程 Id（空白 = 还没选），用于打开时预选</param>
        /// <param name="currentName">当前已选的目标流程名（旧数据只有名字时靠它预选）</param>
        /// <param name="onPicked">确认后的落点：(流程 Id, 流程名)</param>
        void Pick(string? currentFlowId, string? currentName, Action<string, string?> onPicked);
    }
}
