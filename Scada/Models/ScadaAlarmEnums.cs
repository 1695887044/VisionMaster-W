using System;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 报警严重度。只回答"这条报警有多要紧"，决定列表里的排序与配色，
    /// <b>不</b>决定行为——"要不要弹窗""要不要停线"属于运行策略，是宿主的事。
    ///
    /// 为什么是三档而不是两档：现场最常吵的两句话是"这条太吵了"和"这条我怎么没看见"，
    /// 二者用同一档颜色表达时，用户只能靠删报警来消噪。给一个中间的"警告"档，
    /// "提示"才有地方放那些确实该记、但不必打断操作员的事实。
    /// </summary>
    public enum ScadaAlarmSeverity
    {
        /// <summary>提示：记录事实，不需要操作员动作</summary>
        Info = 0,

        /// <summary>警告：需要关注，但可以先把手上的活干完</summary>
        Warning = 1,

        /// <summary>严重：必须立刻处理</summary>
        Critical = 2,
    }

    /// <summary>
    /// 报警条件种类：现场语言里的"高高 / 高 / 低 / 低低"四限，加上布尔跳变与通信断线。
    ///
    /// <b>为什么把四限做成四个种类，而不是"方向 + 阈值 + 严重度"三件套让用户自己拼</b>：
    /// 四限是 ISA-18.2 与所有主流组态软件的既有词汇，操作员看到"高高限"就知道
    /// 它是同一个变量上比"高限"更严重的那一条；让用户自己拼方向与严重度，
    /// 等于把一条行业共识翻译成六种配法，配错了还不容易看出来。
    /// 严重度仍可单独改（<see cref="ScadaAlarmDefinition.Severity"/>），种类只给默认值。
    ///
    /// <b>为什么没有"范围内报警"</b>：那需要上下两个阈值成对出现，与"一条定义一个阈值"
    /// 的表结构冲突。真要用，配两条（低限 + 高限）语义完全等价。
    /// </summary>
    public enum ScadaAlarmKind
    {
        /// <summary>高限：值 <b>&gt;</b> 阈值时报警</summary>
        High = 0,

        /// <summary>高高限：同高限的比较方向，只是更严重（默认 <see cref="ScadaAlarmSeverity.Critical"/>）</summary>
        HighHigh = 1,

        /// <summary>低限：值 <b>&lt;</b> 阈值时报警</summary>
        Low = 2,

        /// <summary>低低限：同低限的比较方向，只是更严重（默认 <see cref="ScadaAlarmSeverity.Critical"/>）</summary>
        LowLow = 3,

        /// <summary>布尔为真时报警（阈值不参与判定）</summary>
        BoolOn = 4,

        /// <summary>布尔为假时报警（阈值不参与判定）</summary>
        BoolOff = 5,

        /// <summary>通信断线：超过 <see cref="ScadaAlarmDefinition.StaleSeconds"/> 没收到新值即报警</summary>
        Stale = 6,
    }

    /// <summary>
    /// 报警态机的四个状态。这是 ISA-18.2 里"操作员视角"的最小闭环：
    /// <code>
    ///           条件成立（且过了延时）
    ///   Normal ────────────────────────► Active ──── 操作员确认 ────► Acknowledged
    ///      ▲                               │                              │
    ///      │                            条件恢复                        条件恢复
    ///      │                               ▼                              │
    ///      └──── 操作员确认 ──── Recovered ◄────────────────────────────┘
    /// </code>
    ///
    /// <b>为什么要区分"已确认时恢复"与"未确认时恢复"</b>：前者（Acknowledged → Normal）
    /// 说明操作员已经知道这条报警并处理完了，可以直接消失；后者（Active → Recovered）
    /// 说明报警自己好了、但操作员可能压根没看见——这时若直接消失，
    /// 现场就会出现"设备半夜跳过一次超温，早上交接班没人知道"。<see cref="Recovered"/>
    /// 这个状态的全部价值就是逼出这一次确认。
    /// </summary>
    public enum ScadaAlarmState
    {
        /// <summary>正常：条件不成立，也没有待确认的历史</summary>
        Normal = 0,

        /// <summary>激活：条件成立，尚未确认</summary>
        Active = 1,

        /// <summary>已确认：条件仍成立，操作员已确认知道</summary>
        Acknowledged = 2,

        /// <summary>已恢复未确认：条件已经不成立，但操作员还没确认过这条报警</summary>
        Recovered = 3,
    }

    /// <summary>
    /// 报警词汇表的显示名与判定辅助。集中在一处，避免"高限"这三个字在菜单、面板、
    /// 落盘 CSV 里各写一遍——那种副本一旦不同步，用户就会看到两种叫法指同一条报警。
    /// </summary>
    public static class ScadaAlarmExtensions
    {
        /// <summary>严重度的中文名（面板与导出共用）</summary>
        public static string DisplayName(this ScadaAlarmSeverity severity) => severity switch
        {
            ScadaAlarmSeverity.Critical => "严重",
            ScadaAlarmSeverity.Warning => "警告",
            _ => "提示",
        };

        /// <summary>条件种类的现场叫法</summary>
        public static string DisplayName(this ScadaAlarmKind kind) => kind switch
        {
            ScadaAlarmKind.HighHigh => "高高限",
            ScadaAlarmKind.High => "高限",
            ScadaAlarmKind.Low => "低限",
            ScadaAlarmKind.LowLow => "低低限",
            ScadaAlarmKind.BoolOn => "为真报警",
            ScadaAlarmKind.BoolOff => "为假报警",
            _ => "通信断线",
        };

        /// <summary>态机状态的中文名</summary>
        public static string DisplayName(this ScadaAlarmState state) => state switch
        {
            ScadaAlarmState.Active => "激活",
            ScadaAlarmState.Acknowledged => "已确认",
            ScadaAlarmState.Recovered => "已恢复未确认",
            _ => "正常",
        };

        /// <summary>是不是"四限"里的一个（只有这几种要用到阈值与回差）</summary>
        public static bool IsLimit(this ScadaAlarmKind kind)
            => kind == ScadaAlarmKind.High || kind == ScadaAlarmKind.HighHigh
            || kind == ScadaAlarmKind.Low || kind == ScadaAlarmKind.LowLow;

        /// <summary>是不是布尔跳变类（不读阈值，只把值当真假看）</summary>
        public static bool IsBoolean(this ScadaAlarmKind kind)
            => kind == ScadaAlarmKind.BoolOn || kind == ScadaAlarmKind.BoolOff;

        /// <summary>
        /// 种类自带的默认严重度：只有"高高限 / 低低限 / 断线"够得上严重——
        /// 前两者是安全极限，后者意味着"你连当前值都不知道了"。
        /// </summary>
        public static ScadaAlarmSeverity DefaultSeverity(this ScadaAlarmKind kind) => kind switch
        {
            ScadaAlarmKind.HighHigh => ScadaAlarmSeverity.Critical,
            ScadaAlarmKind.LowLow => ScadaAlarmSeverity.Critical,
            ScadaAlarmKind.Stale => ScadaAlarmSeverity.Critical,
            ScadaAlarmKind.High => ScadaAlarmSeverity.Warning,
            ScadaAlarmKind.Low => ScadaAlarmSeverity.Warning,
            _ => ScadaAlarmSeverity.Warning,
        };

        /// <summary>这个状态是否"还挂在报警列表上"（正常态才该从实时列表消失）</summary>
        public static bool IsAbnormal(this ScadaAlarmState state) => state != ScadaAlarmState.Normal;

        /// <summary>这个状态是否在等操作员按确认（<see cref="ScadaAlarmState.Acknowledged"/> 已按过，不再等）</summary>
        public static bool NeedsAcknowledge(this ScadaAlarmState state)
            => state == ScadaAlarmState.Active || state == ScadaAlarmState.Recovered;
    }
}
