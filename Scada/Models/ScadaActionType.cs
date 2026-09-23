namespace VisionMaster.Scada
{
    /// <summary>
    /// 事件钩子命中后要执行的动作类型（"点一下按钮能干什么"的取值集合）。
    ///
    /// 与 <see cref="ScadaEventType"/> 同样的落盘纪律：本枚举的<b>数值</b>会被写进 .vms，
    /// 一旦发布就不许改、不许插入中间值，只许在末尾追加。删掉某个动作也不许回收它的数值——
    /// 否则老工程里那条动作会被读成另一个动作，静默地干错事（组态现场最怕这种）。
    ///
    /// 刻意<b>没有</b> <c>None</c>：一条动作记录必然表达一件要干的事，"没有动作"由
    /// <see cref="ScadaEventHook.Actions"/> 集合为空来表达，不该占一个枚举值。
    /// 副作用是"新建一条动作"的默认值是 <see cref="Log"/>——这正是想要的：属性面板上点
    /// "添加动作"，先长出一条"记录日志"，用户改内容就能用，不必先做一次无意义的类型选择。
    /// </summary>
    public enum ScadaActionType
    {
        /// <summary>往运行日志写一行（默认动作；也是唯一在本阶段就真能执行的动作）</summary>
        Log = 1,

        /// <summary>往一个工程变量写值（S6 已接通：经 IScadaValueSource 解析后走 IWritableVariable.TryWrite）</summary>
        WriteVariable = 2,

        /// <summary>切换到另一张画面（S8 已接通：经 IScadaNavigator 找画面，Id 优先、名字兜底）</summary>
        Navigate = 3,
    }

    /// <summary>
    /// 动作类型的展示名。
    ///
    /// 为什么展示名放在领域层而不是属性面板里写死：动作类型可扩展（将来加"确认对话框""播放声音"），
    /// 而"这一行的下拉框该显示什么"和"日志里该怎么描述这条动作"是同一份知识。
    /// 分两处写，就会出现"面板里叫『写变量』、日志里叫『WriteVariable』"。
    /// </summary>
    public static class ScadaActionTypeExtensions
    {
        /// <summary>给人看的动作名（属性面板下拉、日志描述共用）</summary>
        public static string DisplayName(this ScadaActionType type) => type switch
        {
            ScadaActionType.Log => "记录日志",
            ScadaActionType.WriteVariable => "写变量",
            ScadaActionType.Navigate => "切换画面",
            // 高版本软件存下的动作类型读进低版本会落到这里（Newtonsoft 按数值反序列化，不校验取值范围）。
            // 报出数值而不是抛异常：坏在一条动作上，不该让整张画面打不开。
            _ => $"未知动作({(int)type})",
        };

        /// <summary>
        /// 这条动作"在运行侧还没接通"的一句话原因；<c>null</c> = 已经接通。
        ///
        /// 为什么要放在领域层、而且要<b>和日志共用同一句</b>：属性面板上写着"要等 S6"、
        /// 运行日志里写着另一句话，用户就没法把"我配的那条"和"日志里那条"对上号，
        /// 排查方向直接跑偏。这与 <see cref="DisplayName"/> 只写一份是同一个理由。
        ///
        /// 三个动作现在都已接通，所以只剩"本版本不认识"这一档。
        /// 将来再加动作类型，没接通的就在这个 switch 里给它一句原因，接上了再删掉那一项。
        /// </summary>
        public static string? PendingReason(this ScadaActionType type) => type switch
        {
            // 记录日志、写变量、切换画面三条都已接通：
            // - 写变量走 IScadaValueSource → IWritableVariable.TryWrite，失败（没选变量/找不到变量/
            //   转换不过/设备拒写）属于"配错了"，由执行侧当场算出来，不在这里预先写死。
            // - 切换画面走 IScadaNavigator → ScadaRuntime.Navigate，目标页被删掉同样是"配错了"，
            //   也由执行侧当场算出来。
            ScadaActionType.Log => null,
            ScadaActionType.WriteVariable => null,
            ScadaActionType.Navigate => null,
            _ => "本版本不认识该动作",
        };
    }
}
