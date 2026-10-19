using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Services.Health
{
    /// <summary>
    /// 健康等级。数值即优先级：聚合取 max，所以 Idle &lt; Ok &lt; Warn &lt; Error。
    ///
    /// <para><b>为什么 Idle 排在 Ok 前面</b>：聚合要的是"最坏的那个"。
    /// "未启用/未连接"不是故障，但也不该把整页染绿——取 max 时 Idle 会被同域的 Ok 压过，
    /// 只有全域都 Idle 时整体才是 Idle（界面显示"暂无可监控设备"）。</para>
    /// </summary>
    public enum HealthLevel
    {
        Idle = 0,
        Ok = 1,
        Warn = 2,
        Error = 3
    }

    /// <summary>
    /// 概览表的一行（跨域统一投影）。
    /// <para>只放"分诊需要的最小字段"；域内明细（隔离点列表、扫描组统计、轴明细）留在各自页签，
    /// 压进这里会丢信息，也会让概览表变成第二个设置界面。</para>
    /// </summary>
    public sealed record HealthIssue(
        string Domain,
        string Target,
        HealthLevel Level,
        string Message,
        string Metric = "")
    {
        /// <summary>稳定键：不含时间与易变数字，否则 1s 刷新会把整表重建。</summary>
        public string Key => $"{Domain}|{Target}|{Message}";
    }

    /// <summary>域的汇总（概览页四张卡）。</summary>
    public sealed record HealthDomainSummary(
        string Domain,
        string DisplayName,
        HealthLevel Level,
        int Total,
        int Ok,
        int Warn,
        int Error,
        int Idle,
        string Note)
    {
        /// <summary>一行摘要：有对象时给"正常/总数"，没对象时给"未配置"。</summary>
        public string CountText => Total == 0 ? "未配置" : $"{Ok}/{Total} 正常";

        public string LevelText => Level switch
        {
            HealthLevel.Error => "故障",
            HealthLevel.Warn => "警告",
            HealthLevel.Ok => "正常",
            _ => "未启用"
        };

        /// <summary>状态灯档位（XAML 触发器用，与 IO 健康面板同一套字符串）。</summary>
        public string LevelKey => Level switch
        {
            HealthLevel.Error => "error",
            HealthLevel.Warn => "warn",
            HealthLevel.Ok => "ok",
            _ => "idle"
        };
    }

    /// <summary>等级聚合与文案的公共口径（三个 Monitor 共用，避免各写一套）。</summary>
    public static class HealthLevels
    {
        public static HealthLevel Aggregate(IEnumerable<HealthLevel> levels)
        {
            var list = levels as IReadOnlyList<HealthLevel> ?? levels.ToList();
            if (list.Count == 0) return HealthLevel.Idle;
            return list.Max();
        }

        /// <summary>数字 → 中文（概览"持续/关键指标"列与 tooltip 用）。</summary>
        public static string Text(HealthLevel level) => level switch
        {
            HealthLevel.Error => "故障",
            HealthLevel.Warn => "警告",
            HealthLevel.Ok => "正常",
            _ => "未启用"
        };

        /// <summary>数字 → XAML 状态灯档位（ok / warn / error / idle）。</summary>
        public static string Key(HealthLevel level) => level switch
        {
            HealthLevel.Error => "error",
            HealthLevel.Warn => "warn",
            HealthLevel.Ok => "ok",
            _ => "idle"
        };
    }
}
