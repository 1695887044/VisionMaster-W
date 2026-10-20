using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 变量链接请求事件
    /// 插件自定义配置视图发布此事件，请求主程序呼出变量绑定弹窗（DataBindView 单绑定模式）
    /// 主程序（ProcessViewModel）订阅后弹出绑定窗口，绑定成功时通过 OnBound 回调返回结果
    /// </summary>
    public class LinkPathEvent
    {
        /// <summary>
        /// 要绑定的输入端口名（必须与插件 InputPort.Name 一致）
        /// 为空时主程序退化为打开完整的绑定视图（兼容旧行为）
        /// </summary>
        public string InputPortName { get; set; }

        /// <summary>
        /// 端口期望的数据类型（用于绑定弹窗的类型校验），可为 null
        /// </summary>
        public Type TargetType { get; set; }

        /// <summary>
        /// 是否允许把全局/系统变量绑到这个输入端口（默认 true，兼容既有行为）。
        ///
        /// C#脚本的输入端口只接上游数据流（全局/系统变量走左侧「共享变量」声明表），
        /// 因此它发起的绑定请求传 false：弹窗里的「全局变量」节点整块隐藏。
        /// 旧方案里已经绑在输入上的全局变量仍然可读（连线还在 LinkedSources 里），
        /// 只是不能再新绑一个。
        /// </summary>
        public bool AllowGlobals { get; set; } = true;

        /// <summary>
        /// 绑定成功后的回调（用户取消时不触发）
        /// 回调参数为用户选中的连线引用（含 DisplayAddress 显示地址）
        /// </summary>
        public Action<LinkReference> OnBound { get; set; }
    }
}
