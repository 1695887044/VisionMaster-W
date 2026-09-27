using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 运动卡设备契约（运行态）。一家厂商 = 一个实现。
    ///
    /// 与 <see cref="ICameraDevice"/> 是同一范式的兄弟，但**执行模型完全不同**，这点必须看清：
    ///
    ///   相机 = 推模式：设备持续往队列送帧，流程去"取"。设备是主动方。
    ///   运动 = 命令模式：流程下发命令 → 卡执行 → 状态靠主动轮询回来。流程是主动方。
    ///
    /// 因此这里有两条与相机不同、但同样硬的约束：
    ///
    /// 1) <b>命令是异步的</b>。<see cref="Enqueue"/> 返回的是"收下了吗"，不是"动到位了"。
    ///    要等单位，必须再发一条等待命令（或读 <see cref="GetAxisStatus"/> 判断到位）。
    ///    把"下发成功"当"运动完成"是运动控制里最经典的错误，会写出看起来并行、实际串行的流程。
    ///
    /// 2) <b>急停必须能插队</b>。<see cref="EmergencyStop"/> 不能排在几十条普通命令后面 ——
    ///    排队执行的急停不是急停。实现上它走专用通道（清空普通队列 + 立即执行）。
    ///
    /// 状态查询（<see cref="GetAxisStatus"/> / <see cref="ReadInput"/>）一律读**轮询缓存**，
    /// 不发起通信：界面每秒可能查几十次，每次都发一次网口请求会把带宽吃光，
    /// 而且会让"这一帧界面上的几个字段"来自不同时刻、互相矛盾。
    /// </summary>
    public interface IMotionDevice : IDisposable
    {
        /// <summary>本设备对应的配置（含 Id / 驱动键 / 地址 / 轴映射 / 参数）</summary>
        MotionDescriptor Descriptor { get; }

        /// <summary>连接/运行状态</summary>
        MotionCardState State { get; }

        /// <summary>状态补充说明（连接失败原因 / 报警原因 / 停机原因），供界面与日志直接显示</summary>
        string StateDetail { get; }

        /// <summary>能力描述（连接成功后由驱动回填；未连接时为 <see cref="MotionCapabilities.Unknown"/>）</summary>
        MotionCapabilities Capabilities { get; }

    /// <summary>
    /// 设备型号名（如 "ECI3828" / "VPLC532R"）；未知或虚拟设备返回空串。
    ///
    /// 为什么单独给一个属性、而不是让界面去解析 <see cref="StateDetail"/>：
    /// 那段说明是**给人看的自由文本**（"已连接 VPLC532R @ 127.0.0.1（固件 20190615）｜32 轴…"），
    /// 随时可能改写措辞。界面要显示"连的是哪台"就得有**结构化**的来源，
    /// 否则改一次文案就会连带改坏界面。
    /// </summary>
    string ModelName { get; }

        /// <summary>最近一次故障（分级 + 码 + 原因 + 建议）；没有故障时为 null</summary>
        MotionFault? LastFault { get; }

        /// <summary>
        /// 是否已经回过零。
        ///
        /// 为什么必须暴露：增量式编码器的卡掉电/断线后位置是"不知道在哪"，
        /// 此时若允许 MoveAbs，卡会按错误的当前坐标去算行程（可能直接撞向限位）。
        /// 所以"未回零"要能作为一道门禁（由 <see cref="IsHomed"/> 表达），
        /// 断线重连后它会自动归 false。
        /// </summary>
        bool IsHomed { get; }

        /// <summary>累计成功执行的命令数</summary>
        long ExecutedCommandCount { get; }

        /// <summary>累计被拒绝的命令数（状态门禁/能力不匹配/队列满）。与执行数分开看，能分辨"卡住了"与"用错了"</summary>
        long RejectedCommandCount { get; }

        /// <summary>累计故障次数（诊断与"设备是不是该保养了"的依据）</summary>
        long FaultCount { get; }

        /// <summary>当前排队等待执行的命令数（诊断用：居高不下说明下游卡顿）</summary>
        int PendingCommandCount { get; }

        /// <summary>
        /// 状态变化通知。注意：在**非 UI 线程**触发（命令线程/轮询线程），
        /// 订阅方（界面）必须自行调度回 UI 线程。在锁外触发，订阅方可安全回调只读查询。
        /// </summary>
        event EventHandler StateChanged;

        /// <summary>
        /// 故障通知（含分级与处理建议）。同样在非 UI 线程触发。
        /// 与 StateChanged 分开：故障要能"即使状态没变也报一次"（同一状态下的第二次报警不该被吞掉）。
        /// </summary>
        event EventHandler<MotionFault>? FaultOccurred;

        /// <summary>连接设备（打开 SDK 句柄、启动命令线程与状态轮询线程）</summary>
        bool Connect();

        /// <summary>断开设备（停线程 → 关句柄 → 清队列；运动中的轴会先被安全停止）</summary>
        void Disconnect();

        /// <summary>
        /// 清报警（需人工确认的动作，与 <see cref="MotionCommandKind.ClearAlarm"/> 同一个入口）。
        /// 返回 false 时 <paramref name="error"/> 给出原因。
        /// </summary>
        bool ClearAlarm(out string error);

        /// <summary>
        /// "设备还活着"的证据（心跳）。
        /// 网口卡没有推送通道，只能靠"轮询还能问通"来证明在线 ——
        /// 基类据此把"在线但一次都没问通"与"真掉线"分开，并在超时后安全停机。
        /// </summary>
        void NotifyAlive();

        /// <summary>
        /// 读某轴的**状态快照**（轮询缓存，不发起通信）。轴号越界或未连接时返回 null。
        /// </summary>
        AxisStatus? GetAxisStatus(int physicalIndex);

        /// <summary>读数字输入的**快照**（轮询缓存，不发起通信）</summary>
        bool ReadInput(int port);

        /// <summary>
        /// 命令入口：把一条命令交给设备排队执行。
        /// 返回的是"收下了吗"（同步），执行结果要通过命令的 <see cref="MotionCommand.State"/> 或
        /// 后续的等待步骤观察 —— 见类型注释第 1 条。
        /// </summary>
        MotionCommandResult Enqueue(MotionCommand command);

        /// <summary>
        /// 急停：清空普通命令队列并立即执行停止（插队，不排队）。
        /// 由流程中断、界面急停按钮、看门狗触发；返回是否被接受。
        /// </summary>
        MotionCommandResult EmergencyStop();

        /// <summary>
        /// 取消全部：把队列里所有命令标为已取消并调用驱动的安全停止。
        /// 与 <see cref="EmergencyStop"/> 的区别：它用于"流程正常结束/切方案"这类收尾，
        /// 语义是"这一轮结束了，把没做完的丢掉"，而不是"出事了赶紧停"。
        /// </summary>
        void CancelAll(string reason);

        /// <summary>清零计数（现场确认过之后重新开始观察）</summary>
        void ResetCounters();
    }
}
