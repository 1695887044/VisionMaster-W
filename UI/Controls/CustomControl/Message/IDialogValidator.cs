namespace UI.CustomControl
{
    /// <summary>
    /// 自定义内容弹窗（<see cref="EasyDialog.ShowCustomAsync"/>）的"确定前校验"契约。
    ///
    /// 为什么要有这条
    /// ---------
    /// 2026-10-27 计划表走查发现：视图里写了 <c>ValidateAndShow()</c>，但宿主的「确定」按钮
    /// 直接把结果设成 Primary 并关窗——校验函数没有任何调用方，等于死代码。
    /// 结果是"名称空着、动作一条没有"也能保存成功，错误提示永远不出现。
    ///
    /// 宿主在把结果设为 Primary 之前调用本接口；返回 false 表示"不关窗、留在原地"。
    /// 只对 customContent 生效，消息弹窗（MessageDialogView）路径不受影响。
    /// </summary>
    public interface IDialogValidator
    {
        /// <summary>校验并就地显示错误；true = 可以关闭。</summary>
        bool ValidateAndShow();
    }
}
