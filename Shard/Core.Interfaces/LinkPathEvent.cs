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
        /// 绑定成功后的回调（用户取消时不触发）
        /// 回调参数为用户选中的连线引用（含 DisplayAddress 显示地址）
        /// </summary>
        public Action<LinkReference> OnBound { get; set; }
    }
}
