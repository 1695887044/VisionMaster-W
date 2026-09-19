using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 端口免强转读写扩展。
    ///
    /// 为什么需要它：插件在 RunAlgorithm 里操作动态端口或经接口（IInputPort/IOutputPort）持有的端口时，
    /// 只能写 (port as OutputPort&lt;HImage&gt;).Value = xxx 这样的强转样板——强转失败抛 NRE/InvalidCast，
    /// 且弱类型 Value 入口每次都走装箱与转换判断。
    /// 本扩展把"按 T 分派到强类型路径"集中到一处：命中泛型实现走零转换快路径，未命中退回智能转换。
    /// </summary>
    public static class PortExtensions
    {
        /// <summary>
        /// 输出端口写值（免强转）。
        /// 命中 OutputPort&lt;T&gt; 时走 TypedValue 强类型路径：引用比较判变更、值类型免装箱；
        /// 未命中（类型声明不符）时退回 Value 弱类型入口，由智能转换兜底。
        /// </summary>
        /// <typeparam name="T">值的类型，通常可由实参自动推断，无需显式写出</typeparam>
        /// <param name="port">输出端口</param>
        /// <param name="value">要写入的值</param>
        public static void Set<T>(this IOutputPort port, T value)
        {
            if (port == null)
                throw new ArgumentNullException(nameof(port));

            if (port is OutputPort<T> typed)
                typed.TypedValue = value;
            else
                port.Value = value;
        }

        /// <summary>
        /// 输入端口读值（免强转，链接优先）。
        /// 命中 InputPort&lt;T&gt; 时直接返回 ActualValue：上游引用类型全程零转换零拷贝；
        /// 未命中时取 GetActualValue() 并按 ValueConverter 的规则转换。
        /// </summary>
        /// <typeparam name="T">期望的值类型，通常可由赋值目标自动推断，无需显式写出</typeparam>
        /// <param name="port">输入端口</param>
        /// <returns>端口当前实际有效值（链接源优先，其次手动值）</returns>
        public static T Get<T>(this IInputPort port)
        {
            if (port == null)
                throw new ArgumentNullException(nameof(port));

            if (port is InputPort<T> typed)
                return typed.ActualValue;

            object raw = port.GetActualValue();
            if (raw is T typedValue)
                return typedValue;

            return (T)ValueConverter.Convert(raw, typeof(T));
        }
    }
}
