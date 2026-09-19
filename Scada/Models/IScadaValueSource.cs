using System;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 运行态值通道：一条画面绑定解析成功后拿到的"能读、能订阅、能写"的变量句柄。
    ///
    /// 为什么领域层不直接用 IVariable / IVariableRegistry
    /// ---------
    /// 那两个类型住在 VM.Core，而依赖方向是 VM.Core → VM.Scada（见 VM.Scada.csproj 的注释），
    /// 领域层一旦反向引用就成环。所以这里只声明运行态数据泵真正需要的一小撮能力：
    /// 身份（Id）、展示名、数据类型、当前值、值变化通知、显式写。
    /// 由 VM.Core 侧写一个适配器把 <c>IVariableRegistry</c> 接上来。
    ///
    /// 为什么抽"句柄"而不是让 IScadaValueSource 摊开一堆方法
    /// ---------
    /// 数据泵的表结构是"变量 → 一批目标（控件 + 属性键 + 格式）"，按变量订阅一次、反向分发。
    /// 句柄就是这张表的键所指向的那个对象，订阅与写都挂在它身上；将来换数据源
    /// （OPC 组订阅、历史库回放）只需换一个实现，数据泵一行不用改。
    /// </summary>
    public interface IScadaValueHandle
    {
        /// <summary>变量稳定身份。Guid.Empty 表示"旧数据，只能按名字找"</summary>
        Guid VariableId { get; }

        /// <summary>变量展示名（日志与诊断文案用）。机器寻址一律用 <see cref="VariableId"/></summary>
        string Name { get; }

        /// <summary>变量的数据类型。值转换以它为目标类型</summary>
        Type DataType { get; }

        /// <summary>当前值快照。拿不到时为 null，不抛异常</summary>
        object? Value { get; }

        /// <summary>
        /// 值变化通知（底层变量的 ValueChanged 原样转发）。
        /// <b>注意线程</b>：网络变量由后台轮询线程改值，本事件很可能在非 UI 线程触发，
        /// 切线程是订阅方（数据泵）的责任，不是这里。
        /// </summary>
        event EventHandler? ValueChanged;

        /// <summary>
        /// 显式写值（工业写操作正门）。成功返回 true；
        /// 失败返回 false，且 <paramref name="error"/> 是可以直接展示给操作员的中文原因。
        /// </summary>
        bool TryWrite(object? value, out string? error);
    }

    /// <summary>
    /// 运行态数据源：把画面绑定里的"变量引用"解析成值通道。
    ///
    /// 接口定在领域层、实现放在能看见变量注册表的那一层（VM.Core），理由与
    /// <see cref="IScadaActionDispatcher"/> 完全相同：数据泵（ScadaRuntimeBinder）属于运行态规则，
    /// 不该为了拿一个变量而引用整个 VM.Core；而 ScadaChecks 也能塞一个假数据源进来，
    /// 钉住"命中/未命中、Id 优先、停用不刷新、摘表无残留"这几条规则。
    /// </summary>
    public interface IScadaValueSource
    {
        /// <summary>
        /// 解析一条绑定的变量引用：<b>Id 优先、名字兜底</b>，与流程连线、变量注册表同一口径
        /// （见 <see cref="ScadaBinding"/>）。命中返回 true 并给出通道；两者都落空返回 false。
        /// </summary>
        /// <param name="variableId">权威键（Guid.Empty = 旧数据，只能按名字找）</param>
        /// <param name="fallbackName">兜底名字（旧数据或用户手输）</param>
        /// <param name="handle">解析结果；未命中为 null</param>
        bool TryResolve(Guid variableId, string? fallbackName, out IScadaValueHandle? handle);
    }
}
