namespace VisionMaster.Scada
{
    /// <summary>
    /// 一次事件钩子的执行结果计数。Web 命令层用它把"命令收到并执行了"与
    /// "动作实际成功了几条"分开：分发器不抛异常不等于动作成功（变量没选、类型转不过去
    /// 都会走 Skipped/Failed），命令级回执和审计必须能看见这个区别。
    /// </summary>
    /// <param name="Total">动作总数（含坏数据/null 项）</param>
    /// <param name="Succeeded">成功条数</param>
    /// <param name="Skipped">未执行条数（未选目标、条件不满足、桌面确认取消等）</param>
    /// <param name="Failed">失败条数（转换/写入/执行异常）</param>
    public readonly record struct ScadaActionDispatchResult(
        int Total,
        int Succeeded,
        int Skipped,
        int Failed)
    {
        /// <summary>空结果（旧实现/测试桩没有返回计数时使用）</summary>
        public static readonly ScadaActionDispatchResult Empty = new(0, 0, 0, 0);

        /// <summary>至少一条动作失败</summary>
        public bool AnyFailed => Failed > 0;

        /// <summary>至少一条动作没有执行</summary>
        public bool AnySkipped => Skipped > 0;

        /// <summary>全部动作都成功（空动作表视为成功）</summary>
        public bool AllSucceeded => Failed == 0 && Skipped == 0;
    }
}
