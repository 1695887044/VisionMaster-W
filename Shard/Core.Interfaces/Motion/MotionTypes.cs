using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 运动卡连接/运行状态。
    ///
    /// 与相机状态机（Closed / Connecting / Online / Streaming）同形，但多出运动特有的两个状态：
    /// <see cref="Alarm"/>（报警待处理）与 <see cref="SafeStopped"/>（已安全停机）。
    ///
    /// 为什么必须把它们独立成状态，而不是塞进 Online 里挂个布尔：
    /// "在线"与"能动"是两件事 —— 一张卡可以通信正常（Online）却因为伺服报警而不允许任何运动。
    /// 若只用一个 IsAlarm 布尔，每个调用点都要自己判断"现在到底能不能下发命令"，
    /// 漏一处就会对着报警中的轴下发 Move（真机上这是撞机的直接原因）。
    /// 状态机把"能不能动"变成可以统一把关的门禁。
    /// </summary>
    public enum MotionCardState
    {
        /// <summary>未连接</summary>
        Closed = 0,

        /// <summary>正在连接</summary>
        Connecting = 1,

        /// <summary>在线，可接受运动命令</summary>
        Online = 2,

        /// <summary>报警中：通信正常但不接受运动命令，须先清报警（需人工确认）</summary>
        Alarm = 3,

        /// <summary>已安全停机：急停/看门狗/流程中断触发，须显式复位才回到 Online</summary>
        SafeStopped = 4,
    }

    /// <summary>
    /// 命令种类。
    ///
    /// 划分依据是"板卡 API 的语义族"，不是"厂商的某个函数"：
    /// 各家 SDK 的函数名千差万别，但轴控制无非这几件事。
    /// 插补（多轴联动）暂不在此列 —— 它是轴组级命令，第一期先留出枚举位置但不实现，
    /// 避免"接口里躺着一个永远 NotSupported 的项"让使用者误以为能用。
    /// </summary>
    public enum MotionCommandKind
    {
        /// <summary>轴使能</summary>
        Enable = 0,

        /// <summary>轴失能</summary>
        Disable = 1,

        /// <summary>绝对点位运动（只下发，不等待到位）</summary>
        MoveAbsolute = 2,

        /// <summary>相对点位运动（只下发，不等待到位）</summary>
        MoveRelative = 3,

        /// <summary>回零（阻塞到回零结束或超时，属于"不可盲目重试"的命令）</summary>
        Home = 4,

        /// <summary>单轴停止（减速停 / 立即停由 <see cref="MotionCommand.StopMode"/> 决定）</summary>
        Stop = 5,

        /// <summary>写数字输出（IO）</summary>
        SetOutput = 6,

        /// <summary>
        /// 清伺服/驱动器报警。
        ///
        /// 必须有这一条：报警状态（<see cref="MotionCardState.Alarm"/>）是"通信正常但拒绝运动"，
        /// 若没有清报警的入口，一次伺服报警就只能靠重启软件/断电，这在产线上等于停机等人。
        /// 注意它是"需人工确认"的动作 —— 界面上不自动重试，由操作员明确点一次。
        /// </summary>
        ClearAlarm = 7,

        /// <summary>
        /// 点动（Jog）：按给定方向以当前速度**持续**运动，直到收到停止命令。
        ///
        /// 与相对运动的区别是"有没有终点"：相对运动走完固定距离就停，
        /// 点动要一直走到被叫停 —— 这正是它是**手动对位**（调试面板按住才动、松手即停）所必需的。
        /// 也正因为它没有终点，调用方必须自带两件事：
        ///   ① 松手即停（界面上的按住/松开）；
        ///   ② 心跳兜底（界面事件丢了也不能一直动，见调试面板的点动看门狗）。
        /// 卡不支持时由能力位 <see cref="MotionCapabilities.SupportsJog"/> 拒绝。
        /// </summary>
        Jog = 8,
    }

    /// <summary>命令在队列里的生命周期状态（<see cref="MotionCommand.State"/>）</summary>
    public enum MotionCommandState
    {
        /// <summary>已入队，等待执行</summary>
        Queued = 0,

        /// <summary>正在执行</summary>
        Executing = 1,

        /// <summary>执行完成（成功）</summary>
        Done = 2,

        /// <summary>执行失败（驱动给出的业务失败）</summary>
        Failed = 3,

        /// <summary>执行超时</summary>
        TimedOut = 4,

        /// <summary>被取消（急停清队 / 流程中断 / 断开连接）</summary>
        Canceled = 5,
    }

    /// <summary>
    /// <see cref="IMotionDevice.Enqueue"/> 的**立即**返回值：这条命令有没有被收进队列。
    ///
    /// 注意它与 <see cref="MotionCommandState"/> 的区别：这里回答的是"收下了吗"（同步、立刻知道），
    /// 后者回答"后来执行得怎么样"（异步、要等）。运动是异步的，这两个问题必须分开表达，
    /// 否则调用方会把"已入队"当成"已经动到位"。
    /// </summary>
    public enum MotionCommandResult
    {
        /// <summary>已接收并排队</summary>
        Accepted = 0,

        /// <summary>拒绝：卡未连接或在报警/安全停机状态</summary>
        Rejected_NotConnected = 1,

        /// <summary>拒绝：该卡/该轴不支持这种命令（能力协商不通过）</summary>
        Rejected_NotSupported = 2,

        /// <summary>拒绝：参数非法（如轴号越界、超出软限位）</summary>
        Rejected_InvalidArgument = 3,

        /// <summary>拒绝：命令队列已满（说明调用方发得太快，或卡侧卡住了）</summary>
        Rejected_QueueFull = 4,
    }

    /// <summary>驱动执行一条命令后返回的结果（<see cref="MotionDeviceBase.ExecuteCommandCore"/> 的返回值）</summary>
    public enum MotionCommandOutcome
    {
        /// <summary>执行成功完成</summary>
        Done = 0,

        /// <summary>执行失败（原因写进 outcomeMessage，会成为命令的 Error）</summary>
        Failed = 1,

        /// <summary>超时（命令已发出但卡侧迟迟没有完成，可能是卡死，需要人工确认）</summary>
        TimedOut = 2,

        /// <summary>该卡不支持（能力协商不通过，属于配置问题而不是运行时故障）</summary>
        NotSupported = 3,
    }

    /// <summary>
    /// 回零方式（能力协商会用 <see cref="MotionCapabilities.SupportedHomeModes"/> 暴露"这张卡支持哪些"）。
    ///
    /// 为什么不直接用厂商的模式码：正运动是 27/23/19/1/2/37/17/18，雷赛与别的品牌是另一套数字。
    /// 用统一枚举、由驱动各自映射，方案才能跨品牌迁移；映射表写在驱动里，不进配置。
    /// </summary>
    public enum HomeMode
    {
        /// <summary>负限位 + Index（最常见）</summary>
        NegativeLimitIndex = 0,

        /// <summary>正限位 + Index</summary>
        PositiveLimitIndex = 1,

        /// <summary>仅原点开关</summary>
        Origin = 2,

        /// <summary>负限位 + 原点</summary>
        NegativeLimitOrigin = 3,

        /// <summary>正限位 + 原点</summary>
        PositiveLimitOrigin = 4,

        /// <summary>零位置预设（当前位置直接设为零点，不找开关）</summary>
        PresetZero = 5,

        /// <summary>仅负限位</summary>
        NegativeLimit = 6,

        /// <summary>仅正限位</summary>
        PositiveLimit = 7,
    }

    /// <summary>
    /// 故障分级。
    ///
    /// 这是"现场能不能自己处理好"的分水岭：三级之间的处置方式完全不同，
    /// 混成一个"运动失败"会让操作员对"复位一下就行"和"要叫维修"做出同样的反应。
    /// </summary>
    public enum MotionFaultSeverity
    {
        /// <summary>提示级：不影响继续运行（如软限位拒绝了一次移动、命令参数被夹取）</summary>
        Notice = 0,

        /// <summary>可恢复：自动重连 / 重试即可（如通信超时、命令被 NACK）</summary>
        Recoverable = 1,

        /// <summary>需人工复位：清报警后才能继续（如伺服报警、跟随误差超限）</summary>
        RequiresReset = 2,

        /// <summary>需停机维修：不再自动恢复（如硬件故障、反复重连失败）</summary>
        RequiresService = 3,
    }
}
