using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 变量**名字**选择请求事件（2026-10-30 新增）。
    ///
    /// 与 <see cref="LinkPathEvent"/> 的关系与区别
    /// ---------
    /// 两者都通过同一套宿主通道（<c>ProcessViewModel</c> 订阅 → <c>dialogService</c> 开窗）走
    /// <c>DataBindView</c>，**看的是同一个候选树**（全局 / 系统 / 上游「变量定义」/ 上游端口）。
    /// 区别只在**回传什么、拿它干什么**：
    /// - <see cref="LinkPathEvent"/>：取值绑定。回传 <see cref="LinkReference"/>，调用方写进
    ///   <c>StepModel.LinkedSources</c>——编译器据此把值灌进端口。
    /// - 本事件：名字选择。回传的仍是 <see cref="LinkReference"/>（宿主复用同一套弹窗与返回值，
    ///   不新增第二套契约），但调用方**只取 <c>TargetPortName</c> 与 <c>TargetVariableId</c>**，
    ///   **绝不写 <c>LinkedSources</c>**。
    ///
    /// 为什么"只取名字"是安全的：单绑定模式下弹窗本身就不写活模型——
    /// <c>VariableBindingViewModel.DoFinalBind</c> 的写入分支是
    /// <c>if (!IsSingleBindMode &amp;&amp; _targetStep != null)</c>，探针 C27.11 实测
    /// <c>LinkedSources.Count == 0</c>。所以"名字行被当成连线"这件事在宿主侧不可能发生。
    ///
    /// 为什么名字行**不该**用 LinkPathEvent：它的语义是"这个**值**从哪来"，
    /// 而名字行要的是"往哪个变量**写**"。共用事件会让后续维护者误以为名字行也在连线
    /// （R33 记的正是这条静默故障：拿端口名当变量名）。用独立事件把语义钉在类型上。
    /// </summary>
    public sealed class PickVariableNameEvent
    {
        /// <summary>
        /// 期望的作用域：<c>"Global"</c>（全局 + 系统变量）或 <c>"Runtime"</c>（运行时变量）。
        /// 为空按 <c>"Global"</c> 处理。
        /// </summary>
        public string Scope { get; set; } = "Global";

        /// <summary>当前已选的名字（用于弹窗预选；空 = 不预选）。</summary>
        public string CurrentName { get; set; } = string.Empty;

        /// <summary>当前已选的变量身份（优先于名字；空 = 按名字兜底）。</summary>
        public Guid CurrentVariableId { get; set; }

        /// <summary>
        /// 是否允许"上游端口"作为候选（默认 <c>false</c>）。
        ///
        /// 写名场景必须关掉：上游端口的 <c>TargetPortName</c> 是**端口名**（如 <c>Out</c>），
        /// 写进变量名后运行期必然"找不到该变量"——这正是 R33 禁止的"拿值当名字"。
        /// </summary>
        public bool AllowStepPorts { get; set; }

        /// <summary>
        /// 是否允许手输出口（默认 <c>true</c>）。
        ///
        /// 运行时变量是**开集**（<c>Context.SetVar</c> 可造出静态图上看不到的名字），
        /// 只给列表会堵死这类合法用法（R190 唯一保留的手输例外）。
        /// 全局作用域传 <c>false</c>：全局变量必须先在变量管理里存在。
        /// </summary>
        public bool AllowFreeText { get; set; } = true;

        /// <summary>
        /// 选择完成后的回调（用户取消时不触发）。参数是弹窗回传的连线引用：
        /// **只读 <c>TargetPortName</c>（名字）与 <c>TargetVariableId</c>（身份）**，
        /// 不要把它写进 <c>LinkedSources</c>。
        /// </summary>
        public Action<LinkReference> OnPicked { get; set; }
    }
}
