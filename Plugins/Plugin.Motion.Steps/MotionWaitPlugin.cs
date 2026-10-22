using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Core.Interfaces;

namespace Plugin.Motion.Steps
{
    /// <summary>
    /// 等待到位。
    ///
    /// 为什么它必须是一个**独立步骤**（而不是让「轴运动」自己等）：
    /// 运动是异步的。产线上大量场景要"两轴同时运动"，如果运动步骤内部阻塞等到位，
    /// 两条轴就只能串行——现场会发现"节拍莫名慢了一倍"，而且极难看出原因。
    /// 把"下发"与"等待"拆开，用户才能显式表达自己的意图。
    ///
    /// 等待的依据是**设备状态**（轴的 InPosition / Moving），不是"某条命令的完成信号"：
    /// 命令对象不跨步骤传递（那会让流程依赖对象身份，无法保存/重放），
    /// 而"这个轴现在到没到位"是设备上随时可查的事实。
    ///
    /// P1-A 起支持两种判据（由 <see cref="Target"/> 决定）：
    ///   · 默认（Target = NaN）：沿用旧语义，等 <c>InPosition &amp;&amp; !Moving</c>；
    ///   · Target 有值：等 <c>!Moving &amp;&amp; |PositionMm - Target| &lt;= Tolerance</c>，
    ///     不依赖卡上报 InPosition（现场有卡/仿真器的到位位未驱动）。
    /// 旧流程存档里没有 Target 键，端口保持默认 NaN，行为与升级前一致。
    /// </summary>
    [Display(
        Name = "等待到位",
        GroupName = "运动控制",
        Description = "等待指定轴运动到目标位置（轮询设备状态，带超时与报警检查）",
        ShortName = "\uf017")]
    public class MotionWaitPlugin : VisionPluginBase
    {


        /// <summary>（端口）逻辑轴名：可被上游链接；不链接用手填值；都为空则回落到上面的常量</summary>
        public InputPort<string> Axis { get; } = new InputPort<string>("Axis", "X", "逻辑轴名（可链接；不链接则用轴名常量）")
        {
            IsRequired = false,
            IsFunctionalEnum = true,
            OptionKind = StepConfigOptionKind.MotionAxisName,
            PresetOptions = StepConfigOptionSource.GetOptions(StepConfigOptionKind.MotionAxisName).ToList(),
        };

        /// <summary>等待超时（ms）。超时即判失败 —— "走不到"与"走得慢"必须能区分</summary>
        [StepConfig]
        public int TimeoutMs { get; set; } = 30_000;

        /// <summary>轮询间隔（ms）。太小会占满通信带宽（网口卡尤其明显）</summary>
        [StepConfig]
        public int PollIntervalMs { get; set; } = 20;

        /// <summary>
        /// 目标位置（mm）。默认 <see cref="double.NaN"/> = **沿用旧语义**：
        /// 只看设备快照的 <c>InPosition &amp;&amp; !Moving</c>。
        ///
        /// 填了有限数值则改按"位置判据"：<c>!Moving &amp;&amp; |PositionMm - Target| &lt;= Tolerance</c>。
        /// 为什么两种模式并存：旧流程存档里没有这个键，默认 NaN 让它们的行为一个字都不变；
        /// 新流程/现场反馈"卡不报 InPosition（例如 0x800 未驱动）"时，可以显式给目标与容差。
        ///
        /// 必须是输入端口（不是 [StepConfig]）：本插件没有自定义视图，参数面板只渲染输入端口；
        /// 而且目标位置经常来自上游（视觉/配方），要能链接。
        /// </summary>
        public InputPort<double> Target { get; } = new InputPort<double>(
            "Target", double.NaN, "目标位置(mm；NaN=沿用设备到位信号)")
        {
            IsRequired = false,
        };

        /// <summary>
        /// 到位容差（mm），**只在 <see cref="Target"/> 有值时生效**。默认 0.05 mm。
        /// 填负数按绝对值处理；填 NaN/∞ 回退到默认值（容差没有"无限大"的合法语义）。
        /// </summary>
        public InputPort<double> Tolerance { get; } = new InputPort<double>(
            "Tolerance", 0.05, "到位容差(mm，仅 Target 有值时生效)")
        {
            IsRequired = false,
        };

        /// <summary>等待结束时的实际位置（mm），供报表/下游步骤使用</summary>
        public OutputPort<double> Position { get; } = new OutputPort<double>("Position", "到位位置(mm)");

        /// <summary>等待耗时（ms）</summary>
        public OutputPort<int> ElapsedMs { get; } = new OutputPort<int>("Elapsed", "等待耗时(ms)");

        public override void RunAlgorithm(IExecutionContext context)
        {
            var axisName = Axis.ActualValue ?? string.Empty;

            Position.Value = 0;
            ElapsedMs.Value = 0;

            if (!MotionAxisResolution.TryResolveAxis(axisName, out var device, out var mapping, out var resolveError))
            {
                Fail(resolveError);
                return;
            }

            var timeout = Math.Max(1, TimeoutMs);
            var interval = Math.Clamp(PollIntervalMs, 1, 500);
            var watch = Stopwatch.StartNew();

            // Target 的两种模式：
            //   NaN（默认）= 旧语义，只认设备快照的 InPosition && !Moving；
            //   有限数值   = 显式位置判据，不要求卡上报 InPosition（0x800 未驱动的卡也能等）。
            // 旧流程存档里没有 Target 键 → 端口保持默认 NaN → 行为与升级前完全一致。
            var target = Target.ActualValue;
            if (double.IsInfinity(target))
            {
                Fail($"目标位置 {target} 不是有效数字：请填有限数值，或填 NaN 沿用设备到位信号");
                return;
            }

            var useExplicitTarget = !double.IsNaN(target);
            var tolerance = Tolerance.ActualValue;
            if (double.IsNaN(tolerance) || double.IsInfinity(tolerance)) tolerance = 0.05;
            tolerance = Math.Abs(tolerance);

            while (true)
            {
                // 取消优先：流程停止时立刻退出，不要等满超时
                if (context.CancellationToken.IsCancellationRequested)
                {
                    Fail($"等待轴「{axisName}」到位时被中断（流程已停止）");
                    return;
                }

                // 卡状态快检：报警/安全停机（以及断开、连接中）时轴已经不可信，
                // 不要继续等到超时 —— 现场需要的是立刻失败并给出卡的状态说明。
                var cardState = device.State;
                if (cardState != MotionCardState.Online)
                {
                    Fail($"等待轴「{axisName}」期间运动卡不可用（{cardState}）：{device.StateDetail}");
                    return;
                }

                var status = device.GetAxisStatus(mapping.PhysicalIndex);
                if (status == null)
                {
                    Fail($"读不到轴「{axisName}」的状态：运动卡「{device.Descriptor.Caption}」可能已断开（{device.StateDetail}）");
                    return;
                }

                // 报警/限位优先于"到位"：轴在报警状态下可能恰好停在目标附近，
                // 若只看 InPosition 会把"撞停"当成"走到位"
                if (status.HasCriticalSignal)
                {
                    var fault = device.LastFault;
                    Fail($"等待轴「{axisName}」期间出现异常信号：{status.Describe()}"
                         + (string.IsNullOrWhiteSpace(fault?.Suggestion) ? string.Empty : $"（建议：{fault!.Suggestion}）"));
                    return;
                }

                if (useExplicitTarget)
                {
                    // 显式模式：不要求 InPosition（有些卡/仿真器不驱动该位），
                    // 只要求"停下来且位置落在容差内"。
                    // 刻意**不**先等 Moving 变 true：短行程可能在两次轮询之间就走完了，
                    // 那样会永远等不到"运动中"而白等到超时。
                    var deviation = Math.Abs(status.PositionMm - target);
                    if (!status.Moving && deviation <= tolerance)
                    {
                        Position.Value = status.PositionMm;
                        ElapsedMs.Value = (int)watch.ElapsedMilliseconds;
                        Success.Value = true;
                        context.Logger.Info(
                            $"{InstanceName} 轴「{axisName}」已到位：{status.PositionMm:F3} mm"
                            + $"（目标 {target:F3} mm ±{tolerance:F3} mm，偏差 {deviation:F3} mm，耗时 {watch.ElapsedMilliseconds} ms）");
                        return;
                    }
                }
                else if (status.InPosition && !status.Moving)
                {
                    // 旧语义：两个位同时成立才算到位（到位信号亮但还在动 = 还没停稳）
                    Position.Value = status.PositionMm;
                    ElapsedMs.Value = (int)watch.ElapsedMilliseconds;
                    Success.Value = true;
                    context.Logger.Info($"{InstanceName} 轴「{axisName}」已到位：{status.PositionMm:F3} mm（耗时 {watch.ElapsedMilliseconds} ms）");
                    return;
                }

                if (watch.ElapsedMilliseconds > timeout)
                {
                    Position.Value = status.PositionMm;
                    ElapsedMs.Value = (int)watch.ElapsedMilliseconds;
                    var targetText = useExplicitTarget
                        ? $"目标 {target:F3} mm ±{tolerance:F3} mm，偏差 {Math.Abs(status.PositionMm - target):F3} mm，"
                        : string.Empty;
                    Fail($"等待轴「{axisName}」到位超时（>{timeout} ms，{targetText}当前位置 {status.PositionMm:F3} mm）："
                         + "请检查目标是否超出行程、速度是否过低，或该轴是否被限位/报警阻挡");
                    return;
                }

                try { context.CancellationToken.WaitHandle.WaitOne(interval); }
                catch (ObjectDisposedException) { /* 令牌已释放：下一轮循环会因取消而退出 */ }
            }
        }

        public override void Initialize() { }

        public override void Dispose() { }
    }
}
