using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 运行态"控件私有数据"的只读出口（目前只有 DataGrid 的行缓冲）。
    ///
    /// <b>为什么需要它</b>
    /// ---------
    /// DataGrid 的行是控件在"采样触发"那一刻抓的（<c>DataGridElement.AppendRow</c>），
    /// 不在 <see cref="ScadaElement.Properties"/> 里，Web 的标量值帧拿不到。
    /// 让 Web 自己按 10Hz 采样等于做第二份事实源，还会漏掉短于 100ms 的采行脉冲；
    /// 所以由宿主把控件里已经采好的不可变快照投影出来。
    ///
    /// <b>为什么是接口而不是直接让 Web 引用控件库</b>
    /// ---------
    /// Web 模块只引用 <c>VM.Scada</c>，看不到 <c>VM.Scada.Controls</c> 的 WPF 控件；
    /// 实现放在宿主（<c>ScadaRuntimeHost</c>），Web 通过容器解析本接口。
    /// 实现只在 UI 线程调用（运行窗口的画布是 UI 单线程语义）。
    /// </summary>
    public interface IScadaRuntimeDataProvider
    {
        /// <summary>
        /// 取某个 DataGrid 图元当前的行/列快照。图元不存在、未创建控件、不是 DataGrid 时返回 false。
        /// 返回的集合是只读视图（控件内部缓冲的当下快照），调用方不得修改。
        /// </summary>
        bool TryGetGrid(Guid elementId, out ScadaGridSnapshot snapshot);
    }

    /// <summary>DataGrid 的只读快照：列定义 + 最近的行 + 累计记录数。</summary>
    public sealed class ScadaGridSnapshot
    {
        public IReadOnlyList<ScadaGridColumn> Columns { get; init; } = Array.Empty<ScadaGridColumn>();

        public IReadOnlyList<ScadaGridRow> Rows { get; init; } = Array.Empty<ScadaGridRow>();

        /// <summary>累计采到的记录数（可能大于 <see cref="Rows"/>，因为只保留最近 N 行）。</summary>
        public int RecordedCount { get; init; }
    }
}
