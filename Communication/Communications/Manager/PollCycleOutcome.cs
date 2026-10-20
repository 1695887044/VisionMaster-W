namespace VisionMaster.Communications
{
    /// <summary>
    /// 一轮轮询的结局。
    ///
    /// 【为什么需要三态而不是 bool】
    /// 插队读/写要求在“段与段之间”让路：让路时本组并没有失败，只是没跑完。
    /// 若沿用 bool（true=成功 / false=链路故障），Worker 只能二选一：
    ///   · 把让路当成功 → 本组被 MarkSuccess，剩下的段要等下一个完整周期才补读；
    ///   · 把让路当故障 → 好连接被误判成断线，触发重连。
    /// 两者都错。第三态 <see cref="Yielded"/> 让 Worker 明确知道“这次没跑完、下一拍继续”，
    /// 同时不刷新本组的到期时刻。
    /// </summary>
    public enum PollCycleOutcome
    {
        /// <summary>本拍所有到期的组都跑完了（含“本拍无到期组”）</summary>
        Completed,

        /// <summary>
        /// 有插队命令在排队，轮询在段间主动让路。本组不 MarkSuccess（保持到期），
        /// 下一拍重新跑整组；已读过的段会重读一次，这是让路的代价。
        /// </summary>
        Yielded,

        /// <summary>通信级故障（某组本轮所有读都失败 / 链路探针判定已断开），交给 Worker 断线重连</summary>
        LinkFault,
    }
}
