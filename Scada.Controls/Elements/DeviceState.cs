namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 工艺设备（泵 / 电机）的运行状态。
    ///
    /// 为什么泵与电机共用这一个枚举，而不是各写一个 PumpState / MotorState：
    /// 两者在画面上表达的是同一件事——"这台设备现在停着、在转、还是坏了"。
    /// 各写一份的代价不是多打几个字，而是两处语义会各自漂移（哪天给泵加一个"待机"，
    /// 电机那边就悄悄对不上），而模板、描述符、断言又都是照同一套颜色词汇写的。
    ///
    /// 与 <see cref="ValveState"/> / <see cref="LampState"/> 的区别在于使用者个数：
    /// 那两个枚举只服务一个图元，就近写在各自的控件文件里；这一个有两个使用者，
    /// 于是单独一处定义——判据是"有几个使用者"，不是"哪个文件看起来整齐"。
    /// </summary>
    public enum DeviceState
    {
        /// <summary>停机（默认）：设备没在转，画面上是暗色</summary>
        Stopped,

        /// <summary>运行：设备在转</summary>
        Running,

        /// <summary>故障：跳闸、过载、联锁断开——需要操作员立刻看见</summary>
        Fault,
    }
}
