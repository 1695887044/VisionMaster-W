using System;

namespace VisionMaster.Services
{
    /// <summary>
    /// 组态侧的「选变量」入口：把"挑一个工程变量"这件事收成一个回调式的方法。
    ///
    /// 为什么要抽这一层，而不是让属性面板直接拿 <c>IDialogService</c>
    /// ---------
    /// ① 面板与事件行是能在无 WPF 宿主下直接构造、直接断言的对象（ScadaChecks 里就是 new 出来的），
    ///    硬接 Prism 的弹窗服务，等于让断言也得先造一套弹窗基础设施；而"选了哪个变量"这件事
    ///    本身只是一对 (Id, 名字)，用不着把 UI 基础设施搬进被测对象。
    /// ② 弹窗怎么弹（模态还是非模态、同步还是异步）是应用层的事。Prism 的 ShowDialog 是回调式的，
    ///    把它摊在面板上，面板就得学会"回调里再改模型"这套时序；收进本接口后，
    ///    面板只说一句"选完把结果给我"，谁去弹、怎么弹都与它无关。
    ///
    /// 与 <c>IUserNotifier</c> 是同一个范式：面板只认"一件事怎么做"，不认"这件事长什么样"。
    /// </summary>
    public interface IScadaVariablePicker
    {
        /// <summary>
        /// 弹出变量选择框。用户确认时回调 <paramref name="onPicked"/>（回传稳定 Id 与当时的名字）；
        /// 取消则一次都不回调。<b>本方法不阻塞</b>——回调发生在弹窗关闭的那一刻。
        /// </summary>
        /// <param name="currentId">当前已绑的变量 Id（<c>Guid.Empty</c> = 还没绑），用于打开时预选</param>
        /// <param name="currentName">当前已绑的变量名（旧数据只有名字时靠它预选）</param>
        /// <param name="onPicked">确认后的落点：(变量 Id, 变量名)</param>
        void Pick(Guid currentId, string? currentName, Action<Guid, string?> onPicked);
    }
}
