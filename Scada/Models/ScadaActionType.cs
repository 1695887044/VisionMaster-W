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
        /// <summary>往运行日志写一行（默认动作：新建一条动作就立刻能用，不必先做一次类型选择）</summary>
        Log = 1,

        /// <summary>往一个工程变量写值（S6 已接通：经 IScadaValueSource 解析后走 IWritableVariable.TryWrite）</summary>
        WriteVariable = 2,

        /// <summary>切换到另一张画面（S8 已接通：经 IScadaNavigator 找画面，Id 优先、名字兜底）</summary>
        Navigate = 3,

        /// <summary>让一条流程进入连续运行（已接通：经 IScadaFlowRunner，连续语义幂等——已在运行不算失败）</summary>
        StartFlow = 4,

        /// <summary>发出一条流程的停止（已接通：经 IScadaFlowRunner，幂等——本就未在运行也算成功）</summary>
        StopFlow = 5,

        /// <summary>把一条流程跑一遍（已接通：经 IScadaFlowRunner，单次语义不幂等——正在运行时拒绝）</summary>
        RunFlowOnce = 6,

        // ── 运动命令族（数值 7~15，只许在末尾追加） ──
        //
        // 为什么这九条要进动作表、而不是让用户"配一条流程去调运动步骤"：
        // 组态现场最常要的正是"点一下让这根轴走到这里 / 回零 / 停"，
        // 为此去流程栏画一条只有一步的流程，等于让每个按钮都背上一条流程的维护成本
        // （改名、停用、编译不过都会让按钮静默失效）。
        //
        // 粒度按流程侧的步骤划：九条与「轴使能 / 轴运动 / 轴回零 / 轴停止」四类步骤一一对应，
        // 参数口径也一致（见 MotionActionExecutor），所以现场两处配出来的行为不会分叉。
        // 刻意**不**再往下细分（例如 IO 写点）：动作表的定位是"操作员点一下能安全地做什么"，
        // 需要人盯着一步步做的操作用流程步骤更合适。

        /// <summary>轴使能（已接通：经 MotionActionExecutor → IMotionDevice.Enqueue(Enable)）</summary>
        MotionEnable = 7,

        /// <summary>轴失能（已接通：经 MotionActionExecutor → IMotionDevice.Enqueue(Disable)）</summary>
        MotionDisable = 8,

        /// <summary>轴绝对定位（已接通：走到 <c>TargetMm</c>；只下发，不等到位）</summary>
        MotionMoveAbsolute = 9,

        /// <summary>轴相对定位（已接通：从当前位置再走 <c>TargetMm</c>，可为负）</summary>
        MotionMoveRelative = 10,

        /// <summary>轴回零（已接通：回零方式用卡级默认，动作表不提供逐条选择）</summary>
        MotionHome = 11,

        /// <summary>轴停止（已接通：减速停，与流程侧「轴停止」的默认档一致）</summary>
        MotionStop = 12,

        /// <summary>清轴报警（已接通：报警态下这是唯一该被允许的动作，见 MotionActionExecutor）</summary>
        MotionClearAlarm = 13,

        /// <summary>点动开始（已接通：方向由 <c>TargetMm</c> 的正负号决定，缺省正向）</summary>
        MotionJogStart = 14,

        /// <summary>点动结束（已接通：停是安全动作，不走门禁）</summary>
        MotionJogStop = 15,

        /// <summary>暂停流程（P0）：只对连续运行的流程生效；停在节点边界。</summary>
        PauseFlow = 16,

        /// <summary>恢复流程（P0）：把已暂停的连续运行流程从当前步继续。</summary>
        ResumeFlow = 17,

        /// <summary>
        /// 返回上一页（P0）：运行态页栈回退；不需要目标画面，栈空时跳过。
        /// 与「切换画面」共用同一个导航出口，但语义是“回到进入当前页之前那一页”。
        /// </summary>
        NavigateBack = 18,

        /// <summary>
        /// 打开弹窗（特殊画面）：目标必须标了 <see cref="ScadaPage.IsPopup"/>；不改当前页、不压页栈。
        /// 已有弹窗打开时跳过（v1 不支持嵌套）。
        /// </summary>
        OpenPopup = 19,

        /// <summary>关闭当前弹窗（没有弹窗时跳过）。</summary>
        ClosePopup = 20,

        /// <summary>
        /// 增量：读当前值 + <see cref="ScadaAction.Step"/>，按 Min/Max 夹紧后写回。
        /// 微调按钮（速度、补偿、偏移）用它，不必为每个档位配一条「写变量」。
        /// </summary>
        Increment = 21,

        /// <summary>减量：读当前值 − <see cref="ScadaAction.Step"/>，按 Min/Max 夹紧后写回。</summary>
        Decrement = 22,

        /// <summary>
        /// 应用方案级配方（v3）：经 <see cref="IRecipeCommandSink"/> 走宿主 RecipeService，
        /// 权限/运行闸/整单预检/回滚/终检/审计全复用；只应用，不保存方案。
        /// </summary>
        ApplyRecipe = 23,

        // ── 位操作族（v4，数值 24~26，只许在末尾追加） ──
        //
        // 为什么单独长三条动作、而不是让用户写两条「写变量」再自己算掩码：
        // 现场最常见的是"置位/复位一个字里的某一位"（启动位、使能位、报警确认位），
        // 掩码值要随当前值变，写死一个常量会在别的位非零时把整字覆盖掉——那是现场最难查的一类事故。
        // 读-改-写必须由执行侧一次完成，配置侧只声明"改哪一位、改成什么"。
        //
        // 位号口径（<see cref="ScadaAction.BitIndex"/>）：<c>null</c> = Bool 变量整体写；
        // <c>0..31</c> = 整型变量里的位号（与设计基线"位号 0~31"一致）。
        // 整型还受自身位宽限制（byte 只有 0~7），越界由执行侧当场拒绝并说明原因。

        /// <summary>
        /// 置位（v4 位操作）：Bool 变量整体写 <c>true</c>；整型变量把 <see cref="ScadaAction.BitIndex"/>
        /// 指定的那一位置 1，其余位保持不变（读-改-写一次完成）。
        /// </summary>
        SetBit = 24,

        /// <summary>
        /// 复位（v4 位操作）：Bool 变量整体写 <c>false</c>；整型变量把 <see cref="ScadaAction.BitIndex"/>
        /// 指定的那一位置 0，其余位保持不变。
        /// </summary>
        ResetBit = 25,

        /// <summary>
        /// 取反（v4 位操作）：Bool 变量整体取反；整型变量把 <see cref="ScadaAction.BitIndex"/>
        /// 指定的那一位取反（0↔1），其余位保持不变。
        /// </summary>
        ToggleBit = 26,

        /// <summary>
        /// 播放声音（v4）：按 <see cref="ScadaAction.SoundKind"/> 播提示/警告/严重音。
        /// 事件负责“什么时候触发”，声音是触发后的副作用；本动作不改变任何变量。
        /// </summary>
        PlaySound = 27,

        /// <summary>
        /// 语音播报（v5）：把 <see cref="ScadaAction.Text"/> 念出来，文本里可写
        /// <c>{变量名}</c> 按运行态当前值替换（见 <see cref="ScadaTextTemplate"/>）。
        /// 与「播放声音」同属"只产生副作用、不改变量"，但走的是 TTS 队列（限流/抢占），
        /// 且不受报警播报的最低级别门槛限制——这是操作员主动触发的一句话。
        /// </summary>
        SpeakText = 28,

        /// <summary>
        /// 登录（v6）：弹出运行态登录窗，让操作员当场登录。
        ///
        /// <b>不把账号密码存进 .vms</b>：组态文件会被复制、会被发到现场、会被 AI 读；
        /// 把口令写进画面等于把它写进明文配置文件。本动作只负责"把登录窗调出来"，
        /// 凭据由人在窗里敲、由 <c>ScadaUserStore</c> 校验。
        /// </summary>
        Logon = 29,

        /// <summary>
        /// 注销（v6）：结束当前登录会话，回到"未登录"（只剩 <c>Always</c> 权限）。
        /// 与 <see cref="Logon"/> 配对，让运行画面能自己管会话，不必退回主界面。
        /// </summary>
        Logoff = 30,

        /// <summary>
        /// 切换语言（v6）：把运行态当前语言切到 <see cref="ScadaAction.LanguageId"/> 指定的那张语言表。
        /// 目标不存在/表为空时由执行侧当场拒绝并说明，不静默留在原语言。
        /// </summary>
        SetLanguage = 31,

        /// <summary>
        /// 延时（v6）：把<b>同一钩子里排在后面的动作</b>推迟 <see cref="ScadaAction.DelayMs"/> 毫秒再执行。
        ///
        /// 为什么需要它：脉冲输出（写 1 → 等 500ms → 写 0）、点灯提示后自动复位、
        /// 按一下分两步走——没有延时就只能靠流程步骤，而画面按钮不该为此启动一条流程。
        ///
        /// 语义边界（实现按此执行，不额外发挥）：
        /// ① 只影响<b>本条之后</b>的动作；前面的动作已经执行完，不会回滚；
        /// ② 延时期间不阻塞 UI 线程（排到 Dispatcher 队列），也不影响其它事件；
        /// ③ 运行停止/切页时未到点的后续动作<b>不再执行</b>，日志记一条"延时已取消"。
        /// </summary>
        Delay = 32,

        /// <summary>
        /// 确认报警（v7）：确认当前<b>全部</b>待确认报警（激活 + 已恢复未确认）。
        ///
        /// 为什么不带"确认某一条"的参数：动作表挂在图元/画面上，它不知道操作员想确认哪一条；
        /// 逐条确认是「报警汇总表」行内按钮的事（那里有 AlarmId）。
        /// 返回 0 条不算失败——"本来就没有待确认的"是一种正常结果，日志里照实说。
        /// </summary>
        AcknowledgeAlarm = 33,

        /// <summary>
        /// 静音（v7）：按 <see cref="ScadaAction.MuteMinutes"/> 分钟静音报警声音。
        /// 紧急级（Critical）不受静音约束（见宿主 ScadaSoundAlarmService 的口径），这一点由执行侧说明。
        /// </summary>
        MuteAlarm = 34,
    }

    /// <summary>
    /// 动作分类的唯一一份名单：运动族 / 停止族 / 兜底权限 Key。
    /// <see cref="ScadaAction.IsMotionAction"/>、<see cref="ScadaRuntime.RequiredPermissionFor"/> 与
    /// 轴面板的命令感知闸门都从这里取，避免三份名单分头演化。
    /// </summary>
    public static class ScadaActionTaxonomy
    {
        /// <summary>未配权限的非停运动 → 轴点动。</summary>
        public const string MotionFallbackPermission = ScadaPermissionKeys.AxisJog;

        /// <summary>停止族（含点动结束）→ 停止。</summary>
        public const string StopPermission = ScadaPermissionKeys.Stop;

        public static bool IsMotion(ScadaActionType type) => type is
            ScadaActionType.MotionEnable or ScadaActionType.MotionDisable
            or ScadaActionType.MotionMoveAbsolute or ScadaActionType.MotionMoveRelative
            or ScadaActionType.MotionHome or ScadaActionType.MotionStop
            or ScadaActionType.MotionClearAlarm
            or ScadaActionType.MotionJogStart or ScadaActionType.MotionJogStop;

        public static bool IsStopFamily(ScadaActionType type)
            => type is ScadaActionType.MotionStop or ScadaActionType.MotionJogStop;

        public static bool IsNonStopMotion(ScadaActionType type)
            => IsMotion(type) && !IsStopFamily(type);

        public static bool IsStopCommand(ScadaAxisCommandKind kind)
            => kind is ScadaAxisCommandKind.Stop or ScadaAxisCommandKind.JogStop;
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
            ScadaActionType.StartFlow => "启动流程",
            ScadaActionType.StopFlow => "停止流程",
            ScadaActionType.RunFlowOnce => "执行一次",
            // 运动九条的名字与流程侧步骤的 [Display(Name)] 同一口径（「轴使能」「轴回零」…）：
            // 同一件事在两处叫不同名字，现场就没法把"按钮上写的"和"日志里记的"对上号。
            ScadaActionType.MotionEnable => "轴使能",
            ScadaActionType.MotionDisable => "轴失能",
            ScadaActionType.MotionMoveAbsolute => "轴绝对定位",
            ScadaActionType.MotionMoveRelative => "轴相对定位",
            ScadaActionType.MotionHome => "轴回零",
            ScadaActionType.MotionStop => "轴停止",
            ScadaActionType.MotionClearAlarm => "清轴报警",
            ScadaActionType.MotionJogStart => "点动开始",
            ScadaActionType.MotionJogStop => "点动结束",
            ScadaActionType.PauseFlow => "暂停流程",
            ScadaActionType.ResumeFlow => "恢复流程",
            ScadaActionType.NavigateBack => "返回上一页",
            ScadaActionType.OpenPopup => "打开弹窗",
            ScadaActionType.ClosePopup => "关闭弹窗",
            ScadaActionType.Increment => "增量",
            ScadaActionType.Decrement => "减量",
            // 位操作三条用 PLC 现场的通用叫法（置位/复位/取反）：与「增量/减量」同属"改一个变量"，
            // 名字也保持一致的两字短词，下拉里一眼能扫到。
            ScadaActionType.SetBit => "置位",
            ScadaActionType.ResetBit => "复位",
            ScadaActionType.ToggleBit => "取反",
            ScadaActionType.PlaySound => "播放声音",
            ScadaActionType.SpeakText => "语音播报",
            ScadaActionType.ApplyRecipe => "应用配方",
            // v6 四条：登录/注销/切换语言/延时。名字都用现场两字短词，下拉里与既有动作同一节奏。
            ScadaActionType.Logon => "登录",
            ScadaActionType.Logoff => "注销",
            ScadaActionType.SetLanguage => "切换语言",
            ScadaActionType.Delay => "延时",
            // v7：报警闭环的两条。名字与「报警确认按钮」「静音」在界面上同词。
            ScadaActionType.AcknowledgeAlarm => "确认报警",
            ScadaActionType.MuteAlarm => "静音",
            // 高版本软件存下的动作类型读进低版本会落到这里（Newtonsoft 按数值反序列化，不校验取值范围）。
            // 报出数值而不是抛异常：坏在一条动作上，不该让整张画面打不开。
            _ => $"未知动作({(int)type})",
        };

        /// <summary>
        /// 这条动作是不是“危险动作”（P0 确认框的红色语气判据）。
        ///
        /// 口径：会启停流程、会让轴动、会让轴失能的动作算危险；日志/写变量/切画面/
        /// 清报警/点动结束不算。暂停/恢复只改流程运行状态，不算设备动作，故不算危险。
        /// 这份清单只写一遍：事件编辑器的「危险动作」标记与确认弹窗的 PrimaryTone
        /// 都从这里取，不在视图或分发器里各写一套。
        ///
        /// 位操作三条（置位/复位/取反）<b>不算危险</b>：它们改的是一个变量的值，
        /// 与「写变量」「增量/减量」同一档——会不会动设备取决于那个变量接给谁，
        /// 那是变量侧的配置，不是动作类型能判定的。需要二次确认时由钩子上的
        /// 「执行前确认」显式打开（确认框对非危险动作同样有效，只是语气不红）。
        /// </summary>
        public static bool IsDangerous(this ScadaActionType type) => type is
            ScadaActionType.StartFlow or ScadaActionType.StopFlow or ScadaActionType.RunFlowOnce
            or ScadaActionType.MotionStop or ScadaActionType.MotionDisable
            or ScadaActionType.MotionMoveAbsolute or ScadaActionType.MotionMoveRelative
            or ScadaActionType.MotionHome or ScadaActionType.MotionJogStart
            or ScadaActionType.ApplyRecipe;

        /// <summary>
        /// 这条动作"在运行侧还没接通"的一句话原因；<c>null</c> = 已经接通。
        ///
        /// 为什么要放在领域层、而且要<b>和日志共用同一句</b>：属性面板上写着"要等 S6"、
        /// 运行日志里写着另一句话，用户就没法把"我配的那条"和"日志里那条"对上号，
        /// 排查方向直接跑偏。这与 <see cref="DisplayName"/> 只写一份是同一个理由。
        ///
        /// 所有动作现在都已接通（含 2026-10-24 追加的位操作三条），所以只剩"本版本不认识"这一档。
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
            ScadaActionType.Increment => null,
            ScadaActionType.Decrement => null,
            // 位操作三条（v4）与「写变量」同一条通道：没选变量 / 找不到变量 / 不是 Bool 或整型 /
            // 位号越界 / 设备拒写，全是"配错了或此刻写不进"，由执行侧当场算出来并给可直接照做的中文，
            // 所以这里不预先写死。
            ScadaActionType.SetBit => null,
            ScadaActionType.ResetBit => null,
            ScadaActionType.ToggleBit => null,
            // 播放声音：没接声卡/播放器属于宿主装配状态，由执行侧当场报“未接入”，不在这里预判。
            ScadaActionType.PlaySound => null,
            // 语音播报：没接播报器/本机没中文语音包属于宿主装配状态，由执行侧当场报"未接入"；
            // 文本里的变量找不到属于配错了，也由执行侧当场算出来（缺哪个念哪句都在日志里）。
            ScadaActionType.SpeakText => null,
            ScadaActionType.Navigate => null,
            // 流程启停三条走 IScadaFlowRunner：目标流程找不到/停用/加密/编译不过/正被占用，
            // 全是"配错了或这次没排上"，由执行侧当场算出来，不在这里预先写死。
            ScadaActionType.StartFlow => null,
            ScadaActionType.StopFlow => null,
            ScadaActionType.RunFlowOnce => null,
            // 运动九条走 MotionActionExecutor：没选轴/轴已停用/卡不在线/未使能/未回零/超软限位/
            // 命令被拒……每一档都是"配错了或此刻不能动"，由执行侧当场算出来（门禁
            // MotionCommandGate 给的是可直接上屏的中文），所以这里一律不预先写死。
            // 注意"没接入运动模块"（宿主没注入 executor）也<b>不</b>算未接通：
            // 那是宿主装配状态，不是动作类型的能力问题——由分发器照实报"未接入"，见 ScadaActionDispatcher。
            ScadaActionType.MotionEnable => null,
            ScadaActionType.MotionDisable => null,
            ScadaActionType.MotionMoveAbsolute => null,
            ScadaActionType.MotionMoveRelative => null,
            ScadaActionType.MotionHome => null,
            ScadaActionType.MotionStop => null,
            ScadaActionType.MotionClearAlarm => null,
            ScadaActionType.MotionJogStart => null,
            ScadaActionType.MotionJogStop => null,
            ScadaActionType.PauseFlow => null,
            ScadaActionType.ResumeFlow => null,
            ScadaActionType.NavigateBack => null,
            ScadaActionType.OpenPopup => null,
            ScadaActionType.ClosePopup => null,
            ScadaActionType.ApplyRecipe => null,
            // v6 四条：接通与否取决于宿主装配（登录窗/用户会话/语言服务是否注入），
            // 那是装配状态不是动作能力，由执行侧当场报"未接入"并给出可照做的原因。
            // 延时不需要任何外部服务，只要在 UI 线程上就一定能排进 Dispatcher。
            ScadaActionType.Logon => null,
            ScadaActionType.Logoff => null,
            ScadaActionType.SetLanguage => null,
            ScadaActionType.Delay => null,
            // v7 两条：是否接通取决于宿主装配（报警命令出口 / 静音出口），由执行侧当场报"未接入"。
            ScadaActionType.AcknowledgeAlarm => null,
            ScadaActionType.MuteAlarm => null,
            _ => "本版本不认识该动作",
        };
    }
}
