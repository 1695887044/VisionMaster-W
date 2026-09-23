using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>扫描组定义（对标 KEPServerEX 的 Scan Class / Ignition 的 Tag Group / WinCC 的采集周期）。</para>
    /// <para>为什么周期挂在"组"上而不是"变量"上：批量轮询规划器（<see cref="PollBatchPlanner"/>）会把地址相近的
    /// 变量合并成一次块读；若每个变量各带一个周期，就必须按周期把地址段切碎，批量合并直接退化成"每变量一次请求"。
    /// 因此变量级周期被彻底删除（见 docs/code-changes/2026-09-17-删除变量级PollIntervalMs静默失效字段.md），
    /// 改为"整组共享周期、组内照常批量合并"。</para>
    /// <para>注意：本类**只描述自定义组**。默认组是虚拟的、永远存在、不可删不可改名，其周期恒等于
    /// <see cref="CommunicationConfig.ReadCycleMs"/>，不落盘——这样老方案文件反序列化后组表为空，
    /// 全部变量走默认组，行为与改造前完全一致（零回归），也不会出现"周期有两个写入点"的双真相源。</para>
    /// </summary>
    [Serializable]
    public class ScanGroupConfig
    {
        /// <summary>组名（连接内唯一；禁止使用保留名，见 <see cref="PollScheduler.DefaultGroupName"/>）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>扫描周期（ms）。合法区间 [50, 3600000]（1 小时），越界在编译轮询计划时被过滤</summary>
        public int IntervalMs { get; set; } = 1000;

        public ScanGroupConfig() { }

        public ScanGroupConfig(string name, int intervalMs)
        {
            Name = name;
            IntervalMs = intervalMs;
        }

        /// <summary>深拷贝（组表随连接配置一起复制/编辑时用，避免副本与活对象共享同一条组定义）</summary>
        public ScanGroupConfig Clone() => new(Name, IntervalMs);

        public override string ToString() => $"{Name}({IntervalMs}ms)";
    }
}
