using System;
using System.ComponentModel;
using System.Windows.Media;

namespace VM.Charts
{
    /// <summary>线型(适配层翻译为 ScottPlot.LineStyle)</summary>
    public enum ChartLineType
    {
        Solid,
        Dash,
        Dot,
    }

    /// <summary>
    /// 图表系列视图模型契约。
    /// VM 侧只面对"数据 + 状态",不出现任何 ScottPlot 类型;
    /// 数据采集线程直接写 Buffer(线程安全),控件按节流周期拉快照渲染。
    /// </summary>
    public interface ISeriesVm : INotifyPropertyChanged
    {
        /// <summary>增量同步的身份键(同一 Key 的系列在图上只存在一个 Plottable)</summary>
        string Key { get; }

        /// <summary>图例/悬停提示显示名</summary>
        string Label { get; }

        /// <summary>
        /// 曲线颜色。A=0 表示"未指定"——控件按 Fluent 色板自动分配
        /// (顺序:Accent → Success → Warning → Danger → 紫 → 青)。
        /// </summary>
        Color Color { get; }

        bool IsVisible { get; set; }

        /// <summary>采样率(Hz):X 轴 = 样本序号 / SampleRate,单位秒</summary>
        int SampleRate { get; }

        /// <summary>Y 轴索引:0 = 左轴(默认),1 = 右轴(幅值差异大的多曲线场景)</summary>
        int YAxisIndex { get; }

        /// <summary>线宽</summary>
        double LineWidth { get; set; }

        /// <summary>线型</summary>
        ChartLineType LineType { get; set; }

        /// <summary>
        /// 采集起点时刻。设置后 X 刻度显示真实时钟(HH:mm:ss),
        /// 十字光标读数也带绝对时间;null = 相对秒。
        /// </summary>
        DateTime? StartTime { get; set; }

        /// <summary>
        /// 数据缓冲(有效区间从 0 开始,容量固定,写满后自动滑窗丢最旧)。
        /// 控件在 UI 线程读取时通过 <see cref="Lock"/> 保证与采集线程互斥。
        /// </summary>
        double[] Buffer { get; }

        /// <summary>当前有效样本数(原子读)</summary>
        int Count { get; }

        /// <summary>
        /// 累计写入样本数(单调递增,含已滑出窗口的)。
        /// ScottPlot 5 的 DataStreamer 靠它计算"自上次渲染以来新增了哪些样本"。
        /// </summary>
        long TotalWritten { get; }

        /// <summary>与 <see cref="Buffer"/> 读写互斥的锁对象</summary>
        object Lock { get; }

        /// <summary>数据版本号:每批写入递增;控件轮询它决定是否需要重绘(拉模型,零跨线程事件)</summary>
        int Version { get; }

        /// <summary>已写入的时长(秒)= TotalWritten / SampleRate(单调增长,滑窗后仍正确)</summary>
        double HeadSeconds { get; }

        /// <summary>
        /// 渲染 X 偏移(秒):滑窗写满后,缓冲区第一样本的全局时间 = TotalWritten - 容量,
        /// Signal 必须按此偏移才能让曲线贴住真实时间轴(否则滑窗后整条曲线向左错位)。
        /// </summary>
        double XOffsetSeconds { get; }
    }
}
