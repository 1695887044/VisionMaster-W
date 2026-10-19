using System;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 轴面板上的"操作按钮"能发什么命令。
    ///
    /// 只列**单轴、无参数或只有方向**的动作：定位/相对定位需要目标与速度输入，
    /// 属于"参数化命令"，本批仍走动作表（那里有完整的数值编辑与审计）。
    /// 面板自带的是调试台最常用的六个：使能、失能、点动开始/结束、回零、停止、清报警。
    /// </summary>
    public enum ScadaAxisCommandKind
    {
        /// <summary>伺服使能</summary>
        Enable = 0,

        /// <summary>伺服失能</summary>
        Disable = 1,

        /// <summary>点动开始（<see cref="IScadaAxisCommandSink.TryExecute"/> 的 value 是带符号速度，正负即方向）</summary>
        JogStart = 2,

        /// <summary>点动结束</summary>
        JogStop = 3,

        /// <summary>回零</summary>
        Home = 4,

        /// <summary>停止（减速停）</summary>
        Stop = 5,

        /// <summary>清伺服/驱动器报警</summary>
        ClearAlarm = 6,
    }

    /// <summary>
    /// 轴面板的运行态命令出口。
    ///
    /// 为什么要有它、而不是让面板直接拿 <c>MotionActionExecutor</c>：
    /// `VM.Scada.Controls` 刻意不认识运动模块（与 <see cref="IScadaAxisSource"/> 同一条边界），
    /// 面板只认这个只读接口；宿主侧的实现是**唯一**接触 `IMotionDevice` / 门禁 / 点动看门狗的地方。
    ///
    /// 与动作表的关系
    /// ---------
    /// 面板按钮和组态动作最终走**同一个执行器**（`MotionActionExecutor`）：
    /// 门禁、点动看门狗、下发口径只有一份。区别只在"谁触发"——
    /// 面板是现场调试时顺手按，动作表是画面逻辑里配的。
    ///
    /// 实现方契约
    /// ---------
    /// ① **不许抛异常**（调用点在鼠标事件链上，抛出去就是"点按钮程序崩了"）；
    /// ② 返回 false 时 `error` 是可以直接上屏的中文原因；
    /// ③ <b>下发成功 ≠ 到位</b>（与 `IMotionDevice.Enqueue` 同一口径）。
    /// </summary>
    public interface IScadaAxisCommandSink
    {
        /// <summary>
        /// 对某根轴执行一条命令。
        /// </summary>
        /// <param name="axisId">轴稳定身份（面板的当前选中轴）</param>
        /// <param name="command">命令种类</param>
        /// <param name="value">
        /// 只有 <see cref="ScadaAxisCommandKind.JogStart"/> 用它：带符号速度（mm/s，正=正向）。
        /// 其余命令忽略。
        /// </param>
        /// <param name="error">失败原因（可直接上屏）；成功为 null</param>
        bool TryExecute(Guid axisId, ScadaAxisCommandKind command, double? value, out string? error);

        /// <summary>
        /// 定位到某个绝对位置（点位表的「定位」）。
        /// 走与动作表「轴绝对定位」同一条路径：门禁（软限位/使能/回零/忙）只有一份。
        /// </summary>
        /// <param name="velocityMmPerS">速度（mm/s）；&lt;=0 = 用卡级默认</param>
        bool TryMove(Guid axisId, double targetMm, double velocityMmPerS, out string? error);

        /// <summary>
        /// 示教：把当前位置写进某个点位（点位表的「示教」）。
        ///
        /// 这是**运行期改配置**（写 <c>MotionDescriptor.Points</c>，随方案保存落盘），
        /// 权限由调用方（图元）在按下之前判——本接口只负责"能不能写、写成没有"。
        /// </summary>
        bool TryTeach(Guid axisId, int pointIndex, double positionMm, double speedMmPerS, out string? error);

        /// <summary>改某个点位的速度（运行期改配置；权限口径同示教）。</summary>
        bool TrySetPointSpeed(Guid axisId, int pointIndex, double speedMmPerS, out string? error);
    }
}
