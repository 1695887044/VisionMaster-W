using System;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 运行态写通道：把图元输入框里的一串文本，按它所绑变量的类型转换后写回工程变量。
    ///
    /// 它与 <see cref="IScadaValueSource"/> 是一对
    /// ---------
    /// 值源（<see cref="IScadaValueSource"/>）负责"变量 → 图元"这一向：解析句柄、订阅变化、取当前值，
    /// 由数据泵 <c>ScadaRuntimeBinder</c> 消费。本接口负责反向的那一向：操作员在画面上改了值，
    /// 把它送回变量（<see cref="IScadaValueHandle.TryWrite"/>）。
    ///
    /// 为什么"按文本"而不是"按已转好的对象"
    /// ---------
    /// 输入框交给程序的天然形态就是<b>一串字</b>（"23.5"、"true"、"是"），而变量的类型是可以改的
    /// （变量管理里把 int 改成 string）。把"这串字转成什么类型"定死在图元里，就会出现
    /// "组态时变量是 double、图元写死 double、后来变量改成 int、画面开始报错"这类僵死。
    /// 所以图元只管把字交出来，<b>目标类型在真正要写的那一刻向句柄现问</b>（<see cref="IScadaValueHandle.DataType"/>），
    /// 转换规则只认 <c>VariableValueConverter</c> 一份——与"写变量"动作、变量管理弹窗写值同一条口径。
    ///
    /// 为什么接口定在领域层、实现放在能看见变量注册表的那一层
    /// ---------
    /// 理由与 <see cref="IScadaValueSource"/> 逐字相同：控件库（VM.Scada.Controls）够不着
    /// <c>VariableValueConverter</c> 与变量注册表，而它恰恰是输入框的所在地；把实现放在
    /// <c>VisionMaster.Services</c>，控件只认这一个方法，依赖方向不翻。
    ///
    /// 线程
    /// ---------
    /// 只在 UI 线程上被调用（来源是键盘与鼠标），实现方不必加锁。
    /// </summary>
    public interface IScadaValueWriter
    {
        /// <summary>
        /// 把一串文本写回 <paramref name="element"/> 上 <paramref name="targetProperty"/> 所绑定的工程变量。
        ///
        /// 失败一律返回 false 并给出<b>可直接展示给操作员的中文原因</b>（"没绑变量""找不到变量"
        /// "「abc」转不成 Int32""设备离线"），不抛异常——输入框那一下不该让整页运行崩掉。
        /// </summary>
        /// <param name="element">发起写入的图元（模型；绑定挂在它身上）</param>
        /// <param name="targetProperty">写哪个属性的绑定（属性键，如 "Value"）</param>
        /// <param name="text">操作员输入的原样文本（不做 trim、不做预转换，规则由实现方统一走）</param>
        /// <param name="error">失败原因；成功为 null</param>
        bool TryWriteText(ScadaElement element, string targetProperty, string? text, out string? error);
    }
}
