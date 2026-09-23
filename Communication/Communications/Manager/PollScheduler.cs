using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 单个扫描组的运行诊断快照（值语义：跨线程传给 UI 后与 Worker 再无共享，避免"边跑边读"的竞态）。
    /// </summary>
    /// <param name="GroupName">组名</param>
    /// <param name="TargetIntervalMs">目标周期（组表配置值）</param>
    /// <param name="VariableCount">该组变量数</param>
    /// <param name="SegmentCount">该组每轮的段读次数（= 设备往返次数）</param>
    /// <param name="FallbackCount">该组每轮的兜底单读次数</param>
    /// <param name="AvgActualMs">实测周期（相邻两次开跑时刻之差的指数滑动平均）；0 = 还没测到</param>
    public readonly record struct ScanGroupStats(
        string GroupName,
        int TargetIntervalMs,
        int VariableCount,
        int SegmentCount,
        int FallbackCount,
        int AvgActualMs)
    {
        /// <summary>
        /// 达成率 = 目标周期 / 实测周期，封顶 100%（实测比目标还快时按 100% 计）。
        /// 低于 100% 的含义：本组被"同拍内排在它前面的短周期组"拖慢了（同一 socket 串行，物理上限使然）。
        /// </summary>
        public double AchieveRate => AvgActualMs <= 0 ? 0 : Math.Min(1.0, (double)TargetIntervalMs / AvgActualMs);
    }

    /// <summary>
    /// 一个扫描组的运行态：目标周期 + 该组的批量读计划 + 上次成功时刻 + 实测周期。
    /// <para>线程约定：<see cref="BeginRun"/>/<see cref="MarkSuccess"/> 仅由所属连接的 Worker 线程调用；
    /// <see cref="Snapshot"/> 可由 UI 线程调用，故所有可变字段走 Volatile 读写。</para>
    /// </summary>
    internal sealed class PollGroup
    {
        private long _lastSuccessTicks;   // 上次"跑成功"的时刻（判到期用）
        private long _lastStartTicks;     // 上次"开跑"的时刻（测实际周期用）
        private int _avgActualMs;         // 实测周期 EMA

        public PollGroup(string name, int intervalMs, PollBatchPlanner planner, int variableCount)
        {
            Name = name;
            IntervalMs = intervalMs;
            Planner = planner;
            VariableCount = variableCount;
        }

        public string Name { get; }
        public int IntervalMs { get; }
        public PollBatchPlanner Planner { get; }
        public int VariableCount { get; }

        /// <summary>
        /// 建连成功后调用：让本组立刻到期（立即允许首轮）。
        /// 同时清掉"上次开跑时刻"——否则重连前的旧时刻会让首个实测周期算出一个巨大的假值污染达成率。
        /// EMA 不清零，让达成率在重连前后保持连续。
        /// </summary>
        public void MarkDueNow()
        {
            Volatile.Write(ref _lastSuccessTicks, 0L);
            Volatile.Write(ref _lastStartTicks, 0L);
        }

        /// <summary>是否到期（距上次成功已过目标周期）</summary>
        public bool IsDue() => Environment.TickCount64 >= Volatile.Read(ref _lastSuccessTicks) + IntervalMs;

        /// <summary>开跑前调用：更新"实测周期"的 EMA（相邻两次开跑时刻之差）</summary>
        public void BeginRun()
        {
            long now = Environment.TickCount64;
            long prev = Volatile.Read(ref _lastStartTicks);
            if (prev > 0)
            {
                long actual = now - prev;
                int ema = Volatile.Read(ref _avgActualMs);
                // α=0.25：抗单次抖动，又能在十来拍内反映真实趋势
                Volatile.Write(ref _avgActualMs, ema == 0 ? (int)actual : (int)(ema * 0.75 + actual * 0.25));
            }
            Volatile.Write(ref _lastStartTicks, now);
        }

        /// <summary>本轮跑成功：刷新到期基准时刻</summary>
        public void MarkSuccess() => Volatile.Write(ref _lastSuccessTicks, Environment.TickCount64);

        public ScanGroupStats Snapshot() => new(
            Name, IntervalMs, VariableCount,
            Planner.SegmentCount, Planner.FallbackCount,
            Volatile.Read(ref _avgActualMs));
    }

    /// <summary>
    /// <para>多周期优先级调度器：把"已注册变量清单 + 连接组表"编译成 N 个扫描组，供
    /// <see cref="ConnectionWorker"/> 按拍驱动。</para>
    /// <para>为什么需要它：单周期连接无法同时满足"联锁点要 100ms"与"统计点 10s 足够"——
    /// 调快则 PLC 被打爆，调慢则联锁失控。分组后，宝贵的请求配额优先分给短周期组，
    /// 长周期组降频腾出配额（总请求数降一个数量级）。</para>
    /// <para>核心设计：</para>
    /// <para>1) <b>组内照常批量合并</b>——每组各自调一次 <see cref="PollBatchPlanner.Build"/>。
    /// "扫描组"（本类）与"存储区段分组"（PollAddress.GroupKey）是两个正交维度，故批量化不受损；</para>
    /// <para>2) <b>优先级 = 周期升序</b>——构造期就把组按周期排好序，热路径上只是顺序遍历，零比较零排序。
    /// 同一拍内所有到期组按此顺序串行跑完，不做预算截断（截断会让长周期组被饿死，
    /// 而"统计点永不更新"比"统计点晚几十毫秒"严重得多；代价由诊断面板的达成率如实暴露）；</para>
    /// <para>3) <b>失败即中止本拍</b>——任一组返回 false 说明"该组本轮所有读都失败"（链路已死），
    /// 继续跑剩余组只会浪费等待时间与刷日志，直接交给 Worker 断线重连。</para>
    /// <para>线程约定：<see cref="Run"/> 只由所属连接的 Worker 线程调用；<see cref="GetStats"/> 可由 UI 线程调用。
    /// 重建时由 Manager 整体替换实例（引用赋值原子），UI 最多读到"上一个实例的快照"一个刷新周期。</para>
    /// </summary>
    public sealed class PollScheduler
    {
        /// <summary>默认组保留名：永远存在、不可删不可改名，周期恒等于 <see cref="CommunicationConfig.ReadCycleMs"/></summary>
        public const string DefaultGroupName = "默认组";

        /// <summary>
        /// 周期下限：低于 50ms 在常规 PLC 上已无实际意义（单轮往返都不止这个量级）。
        /// <para>公开出来是为了让 UI 校验与编译期过滤共用同一条规则——两处各写一份数值，
        /// 迟早出现"编辑器让填、编译时被静默丢弃"的错位。</para>
        /// </summary>
        public const int MinIntervalMs = 50;

        /// <summary>周期上限：1 小时（同上，UI 与编译期共用）</summary>
        public const int MaxIntervalMs = 3_600_000;

        private readonly List<PollGroup> _groups; // 构造期已按 IntervalMs 升序排好（= 优先级顺序）

        private PollScheduler(List<PollGroup> groups) => _groups = groups;

        /// <summary>是否有可执行的扫描组（false = 上层应停止轮询，避免空转）</summary>
        public bool HasWork => _groups.Count > 0;

        /// <summary>实际参与轮询的组数（不含"声明了但没有变量"的空组）</summary>
        public int GroupCount => _groups.Count;

        /// <summary>所有组每轮的段读总数（= 设备往返次数）</summary>
        public int TotalSegments => _groups.Sum(g => g.Planner.SegmentCount);

        /// <summary>所有组每轮的兜底单读总数</summary>
        public int TotalFallbacks => _groups.Sum(g => g.Planner.FallbackCount);

        #region 编译：变量清单 + 组表 → 调度器

        /// <summary>
        /// 编译调度器。
        /// </summary>
        /// <param name="variables">某连接下的全部已注册变量</param>
        /// <param name="config">连接配置（取组表与默认组周期）；为 null 时全部走默认组（安全兜底）</param>
        /// <param name="log">编译期告警输出（组名回落等汇总信息，不进热路径）</param>
        /// <returns>无变量 / 无可轮询项时返回 null，调用方据此停止轮询</returns>
        public static PollScheduler? Create(
            IEnumerable<CommunicationVariable>? variables,
            CommunicationConfig? config,
            Action<string>? log = null)
        {
            var all = variables as IList<CommunicationVariable> ?? variables?.ToList() ?? new List<CommunicationVariable>();
            if (all.Count == 0) return null;

            // ① 解析组表：默认组恒在首位 + 自定义组（过滤非法/重名/保留名，封顶 8 个）
            var defs = ScanGroupTable.Resolve(config);

            // ② 按组名归桶；未知组名/空 → 回落默认组（变量绝不因为组配置问题而丢失轮询）
            var buckets = ScanGroupTable.Bucket(all, defs, log);

            // ③ 每组各编译一次批量计划（组内照常合并段）；空组 / 无可轮询项 → 不建运行态，避免空转
            var groups = new List<PollGroup>(defs.Count);
            foreach (var def in defs)
            {
                if (!buckets.TryGetValue(def.Name, out var vars) || vars.Count == 0) continue;

                var planner = PollBatchPlanner.Build(vars, log);
                if (planner.PollItemCount == 0) continue;

                groups.Add(new PollGroup(def.Name, def.IntervalMs, planner, vars.Count));
            }

            if (groups.Count == 0) return null;

            // ④ 周期升序 = 优先级顺序（短周期先跑），在构造期定序，热路径零排序
            groups.Sort((a, b) => a.IntervalMs.CompareTo(b.IntervalMs));
            return new PollScheduler(groups);
        }

        /// <summary>取某连接可用的扫描组名（含默认组，恒在首位），供 UI 下拉框使用</summary>
        public static IReadOnlyList<string> ResolveGroupNames(CommunicationConfig? config)
            => ScanGroupTable.Resolve(config).Select(d => d.Name).ToList();

        #endregion

        #region 运行：一拍调度

        /// <summary>是否至少有一个组到期（Worker 用它决定要不要进本轮调度）</summary>
        public bool ShouldRun()
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                if (_groups[i].IsDue()) return true;
            }
            return false;
        }

        /// <summary>
        /// 执行本拍到期的所有扫描组，严格按周期升序（= 优先级）串行执行。
        /// </summary>
        /// <returns>true = 本拍执行的组全部成功（含"本拍无到期组"）；false = 通信级故障，交给 Worker 断线重连</returns>
        public bool Run(ICommunicationConnection connection)
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                if (!group.IsDue()) continue;

                group.BeginRun();
                bool ok = group.Planner.Poll(connection); // 契约不变：false = 本轮所有读都失败 = 通信级故障

                if (!ok)
                {
                    // 链路已死：本拍剩余组不再空跑（省掉无谓等待与日志噪音）。
                    // 不 MarkSuccess → 本组保持"到期"，重连成功后立即补读
                    return false;
                }

                group.MarkSuccess();
            }

            // 注意：Worker 只在 ShouldRun() == true 时才调用本方法，故返回 true 不会"假报链路活跃"
            return true;
        }

        /// <summary>建连成功后调用：让所有组立刻到期（立即允许首轮轮询）</summary>
        public void ResetAllDue()
        {
            for (int i = 0; i < _groups.Count; i++)
                _groups[i].MarkDueNow();
        }

        #endregion

        #region 诊断

        /// <summary>取各组运行诊断快照（只读，供 UI 定时刷新）</summary>
        public IReadOnlyList<ScanGroupStats> GetStats()
        {
            var list = new List<ScanGroupStats>(_groups.Count);
            for (int i = 0; i < _groups.Count; i++)
                list.Add(_groups[i].Snapshot());
            return list;
        }

        /// <summary>一行式组摘要（供重建日志使用：一次重建只出一条汇总，不逐组刷屏）</summary>
        public string Describe() =>
            string.Join(" | ", _groups.Select(g => $"{g.Name} {g.IntervalMs}ms({g.VariableCount}变量/{g.Planner.SegmentCount}段)"));

        #endregion
    }

    /// <summary>
    /// 扫描组表解析与变量归组（编译期辅助，无运行态）。
    /// 独立出来是为了让"组表合法性"这条规则只有一个实现点：UI 校验、轮询编译、变量下拉框都走它。
    /// </summary>
    internal static class ScanGroupTable
    {
        /// <summary>周期下限：低于 50ms 在常规 PLC 上已无实际意义（单轮往返都不止这个量级）</summary>
        private const int MinIntervalMs = PollScheduler.MinIntervalMs;

        /// <summary>周期上限：1 小时</summary>
        private const int MaxIntervalMs = PollScheduler.MaxIntervalMs;

        /// <summary>
        /// 解析组表：默认组恒在首位（周期取 ReadCycleMs），其后是过滤后的自定义组。
        /// 过滤规则：非空名 + 非保留名 + 未重名 + 周期合法；总数封顶 <see cref="CommunicationConfig.MaxScanGroups"/>。
        /// </summary>
        public static List<(string Name, int IntervalMs)> Resolve(CommunicationConfig? config)
        {
            var list = new List<(string Name, int IntervalMs)>
            {
                (PollScheduler.DefaultGroupName, config?.ReadCycleMs is > 0 ? config.ReadCycleMs : 1000)
            };

            var custom = config?.ScanGroups;
            if (custom == null) return list;

            foreach (var group in custom)
            {
                if (list.Count > CommunicationConfig.MaxScanGroups) break; // 首元素是默认组，故 > 上限即已满
                if (group == null || string.IsNullOrWhiteSpace(group.Name)) continue;

                var name = group.Name.Trim();
                if (name == PollScheduler.DefaultGroupName) continue;          // 保留名
                if (group.IntervalMs is < MinIntervalMs or > MaxIntervalMs) continue;
                if (list.Any(x => x.Name == name)) continue;                   // 重名：取首个

                list.Add((name, group.IntervalMs));
            }

            return list;
        }

        /// <summary>
        /// 把变量按 <see cref="CommunicationVariable.ScanGroup"/> 归桶。
        /// 无法归属的（空名 / 指向不存在的组）一律进默认组，并汇总一条日志——绝不逐变量刷屏。
        /// </summary>
        public static Dictionary<string, List<CommunicationVariable>> Bucket(
            IList<CommunicationVariable> variables,
            List<(string Name, int IntervalMs)> defs,
            Action<string>? log)
        {
            var buckets = new Dictionary<string, List<CommunicationVariable>>(defs.Count);
            foreach (var def in defs)
                buckets[def.Name] = new List<CommunicationVariable>();

            var valid = new HashSet<string>(buckets.Keys);
            int fallbackCount = 0;

            foreach (var variable in variables)
            {
                if (variable == null) continue;

                var key = variable.ScanGroup?.Trim();
                if (string.IsNullOrEmpty(key))
                {
                    buckets[PollScheduler.DefaultGroupName].Add(variable);
                    continue;
                }

                if (!valid.Contains(key))
                {
                    fallbackCount++;
                    buckets[PollScheduler.DefaultGroupName].Add(variable);
                    continue;
                }

                buckets[key].Add(variable);
            }

            if (fallbackCount > 0)
                log?.Invoke($"{fallbackCount} 个变量的扫描组名在连接组表中不存在（可能组已被删除），已回落到默认组");

            return buckets;
        }
    }
}
