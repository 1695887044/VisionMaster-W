using System;
using System.Linq;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>读超时 vs 扫描周期的体检规则：读超时超过"生效最短扫描周期"时给一条可执行的告警。</para>
    /// <para><b>为什么阈值不是"周期的一半"</b>：TCP 类默认读超时（2000ms）与常见周期（1000ms）本就同量级，
    /// 取一半会让大量正常配置 100% 触发告警，警告立刻退化成背景噪声。判据取"超过一个<b>整</b>周期"——
    /// 含义是"一次静默超时至少吞掉一整拍"，这是用户真正能感知的伤害起点。</para>
    /// <para><b>为什么比"最短"而不是默认组</b>：调度器按各组自己的周期驱动，自定义组可能比默认组快得多；
    /// 一个 5s 超时对 100ms 组的伤害是 50 拍，对 10s 组只是半拍。取生效最短周期才是最坏情况。</para>
    /// <para><b>为什么两个出口共用一份</b>：日志出口（方案加载 / 运行时重编译）与提示出口（设置对话框保存）
    /// 各写一遍规则，迟早出现"日志说有问题、弹窗说没问题"的漂移。</para>
    /// </summary>
    public static class ReadTimeoutAdvisor
    {
        /// <summary>读超时的合法下限（与 <see cref="ConnectionConfigBase.ReadTimeoutMs"/> 的 RangeValidation 同源）。</summary>
        private const int MinReadTimeoutMs = 100;

        /// <summary>
        /// 体检一条连接。返回 null 表示无需提示；否则返回可直接展示的中文结论（含数字与建议值）。
        /// </summary>
        public static string? Describe(CommunicationConfig? config)
        {
            var link = config?.Config;
            if (link == null) return null;

            // 生效组表：默认组周期取 ReadCycleMs（<=0 回落 1000），自定义组按 Resolve 的过滤规则
            //（空名/保留名/重名/越界组会被剔除）。不能直接拿 ReadCycleMs 比——
            // 那会漏掉"默认组慢、自定义组快"这种最需要提醒的配置。
            var groups = ScanGroupTable.Resolve(config);
            if (groups.Count == 0) return null;

            int shortest = groups.Min(g => g.IntervalMs);
            if (shortest <= 0) return null;

            int timeout = link.ReadTimeoutMs;
            if (timeout <= shortest) return null;

            int cycles = (timeout + shortest - 1) / shortest;

            // 周期比读超时的合法下限还短时，"把超时压到周期/4"根本做不到——
            // 这时候给一个夹到下限的数字（如 50ms 周期 → "100~100ms"）是误导，必须直说无解。
            if (shortest <= MinReadTimeoutMs)
            {
                return $"读超时 {timeout}ms 超过生效最短扫描周期 {shortest}ms——一次静默超时会占掉约 {cycles} 个周期" +
                       $"；但周期已低于读超时下限 {MinReadTimeoutMs}ms，两者无法匹配：" +
                       $"请把周期调到 ≥{MinReadTimeoutMs * 4}ms，或接受跨拍读取。";
            }

            int suggestedLow = Math.Max(MinReadTimeoutMs, shortest / 4);
            int suggestedHigh = Math.Max(suggestedLow, shortest / 3);
            string suggestion = suggestedHigh > suggestedLow
                ? $"{suggestedLow}~{suggestedHigh}ms"
                : $"{suggestedLow}ms";

            // 串口不实现 ILinkHealthProbe：一次超时不会中断本轮，后面每个段各烧一个超时，伤害是"段数×"。
            // TCP 类首次超时即自证链路故障（HasLinkFault），代价是 1 次超时 + 重连，量级完全不同。
            string scope = link is SerialConfig
                ? "；串口不会提前中断本轮，实际代价还要乘以段数"
                : "；TCP 首次超时即判链路故障并重连，代价是 1 次超时 + 重连";

            return $"读超时 {timeout}ms 超过生效最短扫描周期 {shortest}ms——一次静默超时会占掉约 {cycles} 个周期{scope}" +
                   $"；建议降到 {suggestion}，失败交给下一拍重试。";
        }
    }
}
