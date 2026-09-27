namespace Plugin.Motion.Steps
{
    /// <summary>
    /// 运动方式（步骤界面上的"绝对 / 相对"）。
    ///
    /// 为什么不直接复用 <c>Core.Interfaces.MotionCommandKind</c>：那是**命令**的种类
    /// （给驱动看的），这只是界面上的一个二选一选择项（给用户看的）。
    /// 让配置项直接暴露命令枚举，会在将来命令种类变多（如插补）时把无关选项塞进这个下拉里。
    /// </summary>
    public enum MotionMoveMode
    {
        /// <summary>绝对运动：走到指定的坐标点</summary>
        Absolute = 0,

        /// <summary>相对运动：从当前位置再走一段（增量，可为负）</summary>
        Relative = 1,
    }

    /// <summary>
    /// 轴的停止方式（步骤界面上的选择项）。
    /// 数值与正运动 SDK 的约定一致（2=减速停 / 3=立即停），映射写在驱动侧，这里只是选择项。
    /// </summary>
    public enum MotionStopMode
    {
        /// <summary>减速停：按当前加减速曲线停下 —— 默认，对机械冲击小</summary>
        Decelerate = 2,

        /// <summary>立即停：切断运动立即停止 —— 仅急停/限位等场合使用，对机械冲击大</summary>
        Immediate = 3,
    }

    /// <summary>轴 IO 的操作方向</summary>
    public enum MotionIoDirection
    {
        /// <summary>写输出（气缸、指示灯等）</summary>
        WriteOutput = 0,

        /// <summary>读输入（传感器、限位等）</summary>
        ReadInput = 1,
    }
}
