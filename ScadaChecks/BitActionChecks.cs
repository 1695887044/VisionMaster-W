using System;
using System.Collections.Generic;
using System.Linq;
using VisionMaster.Scada;
using VisionMaster.Services;
using VisionMaster.ViewModels;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [BIT] 位操作动作（置位 / 复位 / 取反）：枚举稳定性、Bool 与整型两条执行路径、
        /// 越界与非整型拒绝、变量缺失、写失败、Describe/Detail 文案、动作编辑器候选与排序。
        ///
        /// 全部用生产那个 <see cref="ScadaActionDispatcher"/>（它只依赖 <see cref="ILogService"/> 与
        /// <see cref="IScadaValueSource"/>），值源/句柄用本工程既有的 <see cref="FakeValueSource"/> /
        /// <see cref="FakeValueHandle"/>——验的是现场跑的那一套，不是为测试另写的替身。
        ///
        /// 与「写变量」那批断言的分工：那边钉的是"值转不过去 / 写不进"四档措辞，
        /// 这里钉的是"读-改-写只动目标位、其余位一个都不许变"以及"位号越界必须拒绝"。
        /// 位操作最危险的失败模式是静默改错位：值变了、日志还是绿的，现场要到设备误动作才发现。
        /// </summary>
        private static class BitActionChecks
        {
            public static void Run() => RunChecks();

            private static void RunChecks()
            {
                Section("位操作动作（SetBit / ResetBit / ToggleBit）");

                CheckEnumContract();
                CheckDescribeContract();
                CheckBoolPath();
                CheckIntegerPath();
                CheckRejections();
                CheckEditorChoices();
            }

            /// <summary>
            /// [BIT1] 枚举数值与展示契约。数值是落盘键：改一个数就会让老 .vms 里的动作变成另一件事。
            /// </summary>
            private static void CheckEnumContract()
            {
                Check("[BIT1] 枚举末尾追加且旧值不动：SetBit=24 / ResetBit=25 / ToggleBit=26，Log=1、ApplyRecipe=23",
                    (int)ScadaActionType.SetBit == 24
                    && (int)ScadaActionType.ResetBit == 25
                    && (int)ScadaActionType.ToggleBit == 26
                    && (int)ScadaActionType.Log == 1
                    && (int)ScadaActionType.ApplyRecipe == 23
                    && Enum.GetValues<ScadaActionType>().Select(v => (int)v)
                        .SequenceEqual(Enumerable.Range(1, 27)),
                    $"SetBit={(int)ScadaActionType.SetBit} / ResetBit={(int)ScadaActionType.ResetBit} / " +
                    $"ToggleBit={(int)ScadaActionType.ToggleBit} / values=" +
                    string.Join(",", Enum.GetValues<ScadaActionType>().Select(v => (int)v)));

                var displayNames = new[]
                {
                    ScadaActionType.SetBit.DisplayName(),
                    ScadaActionType.ResetBit.DisplayName(),
                    ScadaActionType.ToggleBit.DisplayName(),
                };

                Check("[BIT2] 展示名与 PendingReason：三条都有中文名、都已接通（PendingReason=null），且不算危险动作",
                    displayNames.SequenceEqual(new[] { "置位", "复位", "取反" })
                    && new[] { ScadaActionType.SetBit, ScadaActionType.ResetBit, ScadaActionType.ToggleBit }
                        .All(t => t.PendingReason() == null && !t.IsDangerous()),
                    string.Join("/", displayNames));
            }

            /// <summary>
            /// [BIT3] 行内摘要与日志措辞。两处必须同源（Detail 给面板、Describe 给日志/审计），
            /// 否则用户对不上"我配的那条"和"日志里那条"。
            /// </summary>
            private static void CheckDescribeContract()
            {
                var set = new ScadaAction { Type = ScadaActionType.SetBit, VariableName = "ControlWord", BitIndex = 3 };
                var reset = new ScadaAction { Type = ScadaActionType.ResetBit, VariableName = "ControlWord", BitIndex = 3 };
                var toggle = new ScadaAction { Type = ScadaActionType.ToggleBit, VariableName = "ControlWord", BitIndex = 3 };

                Check("[BIT3] 整型带位号：Detail/Describe 都是「变量.位号 ← 结果」，三种结果分别是 1 / 0 / 取反",
                    set.Detail == "ControlWord.3 ← 1"
                    && set.Describe() == "置位 ControlWord.3 ← 1"
                    && reset.Detail == "ControlWord.3 ← 0"
                    && reset.Describe() == "复位 ControlWord.3 ← 0"
                    && toggle.Detail == "ControlWord.3 ← 取反"
                    && toggle.Describe() == "取反 ControlWord.3 ← 取反",
                    $"{set.Describe()} / {reset.Describe()} / {toggle.Describe()}");

                var boolAction = new ScadaAction { Type = ScadaActionType.SetBit, VariableName = "运行中" };

                Check("[BIT4] 位号留空（Bool 整体写）：Detail 不缀那个点，Describe 明说「未填位号，按 Bool 整体写」",
                    boolAction.Detail == "运行中 ← 1"
                    && boolAction.Describe() == "置位 运行中 ← 1（未填位号，按 Bool 整体写）",
                    $"{boolAction.Detail} / {boolAction.Describe()}");

                var noVariable = new ScadaAction { Type = ScadaActionType.ResetBit };

                Check("[BIT5] 未选变量：Describe 给「复位（未选变量）」，不留半截串；Detail 沿用「(未指定)」占位",
                    noVariable.Describe() == "复位（未选变量）"
                    && noVariable.Detail == "(未指定) ← 0",
                    $"{noVariable.Describe()} / {noVariable.Detail}");

                var probe = new ScadaAction { Type = ScadaActionType.SetBit, VariableName = "ControlWord" };
                var raised = new List<string>();
                probe.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "<null>");
                probe.BitIndex = 5;

                var typeProbe = new ScadaAction();
                var typeRaised = new List<string>();
                typeProbe.PropertyChanged += (_, e) => typeRaised.Add(e.PropertyName ?? "<null>");
                typeProbe.Type = ScadaActionType.ToggleBit;

                Check("[BIT6] 改位号广播 Detail（面板那行当场换字）；改类型广播 IsBitAction（编辑区当场显隐），且不误判成运动动作",
                    raised.Contains(nameof(ScadaAction.Detail))
                    && probe.Detail == "ControlWord.5 ← 1"
                    && typeRaised.Contains(nameof(ScadaAction.IsBitAction))
                    && typeProbe.IsBitAction
                    && !typeProbe.IsMotionAction,
                    $"detail={probe.Detail} / typeRaised={string.Join("/", typeRaised)}");
            }

            /// <summary>
            /// [BIT7~BIT11] Bool 变量：整体写，位号忽略。
            /// </summary>
            private static void CheckBoolPath()
            {
                var values = new FakeValueSource();
                var flag = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "运行中", DataType = typeof(bool) };
                values.ById[flag.VariableId] = flag;
                values.ByName[flag.Name] = flag;

                var setLog = Dispatch(values, Bit(ScadaActionType.SetBit, flag));

                Check("[BIT7] Bool 置位：写 true（不是 1），日志一行 Info 带「置位 运行中 ← 1」",
                    Equals(flag.Value, true)
                    && setLog.Lines.Count == 1
                    && setLog.Lines[0].StartsWith("I ", StringComparison.Ordinal)
                    && setLog.Lines[0].EndsWith("→ 置位 运行中 ← 1（未填位号，按 Bool 整体写）", StringComparison.Ordinal),
                    Single(setLog));

                var resetLog = Dispatch(values, Bit(ScadaActionType.ResetBit, flag));

                Check("[BIT8] Bool 复位：写 false，日志带「复位 运行中 ← 0」",
                    Equals(flag.Value, false)
                    && resetLog.Lines.Count == 1
                    && resetLog.Lines[0].Contains("复位 运行中 ← 0", StringComparison.Ordinal),
                    Single(resetLog));

                var toggleOn = Dispatch(values, Bit(ScadaActionType.ToggleBit, flag));
                var afterFirstToggle = flag.Value;
                var toggleOff = Dispatch(values, Bit(ScadaActionType.ToggleBit, flag));

                Check("[BIT9] Bool 取反：false→true→false，两次都成功且各留一行",
                    Equals(afterFirstToggle, true)
                    && Equals(flag.Value, false)
                    && toggleOn.Lines.Count == 1 && toggleOn.Lines[0].StartsWith("I ", StringComparison.Ordinal)
                    && toggleOff.Lines.Count == 1 && toggleOff.Lines[0].StartsWith("I ", StringComparison.Ordinal),
                    $"第一次后={afterFirstToggle} / 第二次后={flag.Value}");

                // Bool 变量填了位号也不该改"整体写"的语义：Bool 只有一位，位号在这里没有意义
                var withIndex = Dispatch(values, Bit(ScadaActionType.SetBit, flag, 0));

                Check("[BIT10] Bool 变量填了位号（0）：按整体写处理，不报错、不因位号而改道整型分支",
                    Equals(flag.Value, true)
                    && withIndex.Lines.Count == 1
                    && withIndex.Lines[0].StartsWith("I ", StringComparison.Ordinal),
                    Single(withIndex));

                var beforeBadBool = flag.Value;
                var badBool = Dispatch(values, Bit(ScadaActionType.SetBit, flag, 5));

                Check("[BIT10b] Bool 变量填非零位号（5）：明确拒绝，不静默按整体写",
                    Equals(flag.Value, beforeBadBool)
                    && badBool.Lines.Count == 1
                    && badBool.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && badBool.Lines[0].Contains("Bool，位号只能留空或 0", StringComparison.Ordinal),
                    Single(badBool));

                // 取反读不出当前值时拒绝，绝不按 false 起猜（那会把"取反"静默变成"置位"）
                var blind = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "未读到", DataType = typeof(bool) };
                var blindValues = new FakeValueSource();
                blindValues.ById[blind.VariableId] = blind;
                var blindLog = Dispatch(blindValues, Bit(ScadaActionType.ToggleBit, blind));

                Check("[BIT11] Bool 取反但当前值读不出来（null）：拒绝并说明原因，不按 false 猜成置位、不写变量",
                    blindLog.Lines.Count == 1
                    && blindLog.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && blindLog.Lines[0].Contains("不是有效布尔，无法取反", StringComparison.Ordinal)
                    && blind.Value == null,
                    Single(blindLog));
            }

            /// <summary>
            /// [BIT12~BIT16] 整型变量：读-改-写，只动目标位。
            /// </summary>
            private static void CheckIntegerPath()
            {
                var values = new FakeValueSource();
                var word = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "运行字", DataType = typeof(int) };
                values.ById[word.VariableId] = word;
                values.ByName[word.Name] = word;
                word.Value = 0;

                var bit0Log = Dispatch(values, Bit(ScadaActionType.SetBit, word, 0));

                Check("[BIT12] int bit0 置位：0 → 1，日志带「置位 运行字.0 ← 1」",
                    Equals(word.Value, 1)
                    && bit0Log.Lines.Count == 1
                    && bit0Log.Lines[0].Contains("置位 运行字.0 ← 1", StringComparison.Ordinal),
                    $"{word.Value} / {Single(bit0Log)}");

                var bit31Log = Dispatch(values, Bit(ScadaActionType.SetBit, word, 31));

                Check("[BIT13] int bit31 置位：1 → 0x80000001（负数，符号位被置上；bit0 原样保留）",
                    Equals(word.Value, unchecked((int)0x80000001))
                    && bit31Log.Lines.Count == 1
                    && bit31Log.Lines[0].Contains("置位 运行字.31 ← 1", StringComparison.Ordinal),
                    $"{word.Value} / {Single(bit31Log)}");

                Dispatch(values, Bit(ScadaActionType.ResetBit, word, 31));
                var afterReset = word.Value;
                Dispatch(values, Bit(ScadaActionType.ToggleBit, word, 0));

                Check("[BIT14] 复位 bit31 只清那一位（回到 1），取反 bit0 只翻那一位（1→0），其余位全程不动",
                    Equals(afterReset, 1) && Equals(word.Value, 0),
                    $"复位后={afterReset} / 取反后={word.Value}");

                // 有符号负数按二进制补码摊开：-1 是 32 个 1，清 bit31 之后应当只剩低 31 位
                var signed = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "有符号字", DataType = typeof(long) };
                var signedValues = new FakeValueSource();
                signedValues.ById[signed.VariableId] = signed;
                signed.Value = -1L;
                Dispatch(signedValues, Bit(ScadaActionType.ResetBit, signed, 31));

                Check("[BIT15] long 当前值 -1（全 1）：复位 bit31 得 -2147483649（64 位其余位原样保留，符号位不被「取绝对值」弄丢）",
                    Equals(signed.Value, -2147483649L),
                    signed.Value?.ToString() ?? "null");

                // 窄类型受自身位宽限制：byte 没有第 8 位
                var small = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "字节", DataType = typeof(byte) };
                var smallValues = new FakeValueSource();
                smallValues.ById[small.VariableId] = small;
                small.Value = (byte)0;

                var byte7Log = Dispatch(smallValues, Bit(ScadaActionType.SetBit, small, 7));
                var byte8Log = Dispatch(smallValues, Bit(ScadaActionType.SetBit, small, 8));

                Check("[BIT16] byte 位宽：bit7 可以（→128），bit8 越界拒绝且值不变（原因写明 0~7）",
                    Equals(byte7Log.Lines.Count, 1)
                    && byte7Log.Lines[0].StartsWith("I ", StringComparison.Ordinal)
                    && Equals(byte8Log.Lines.Count, 1)
                    && byte8Log.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && byte8Log.Lines[0].Contains("的可用位号是 0~7", StringComparison.Ordinal)
                    && Equals(small.Value, (byte)128),
                    $"{byte7Log.Lines.FirstOrDefault()} ⏎ {byte8Log.Lines.FirstOrDefault()} / value={small.Value}");
            }

            /// <summary>
            /// [BIT17~BIT24] 拒绝路径：越界、缺位号、类型不符、变量缺失、写失败。
            /// 每一条都要"值没被改"——只断言日志不红，改错了位也看不出来。
            /// </summary>
            private static void CheckRejections()
            {
                var values = new FakeValueSource();
                var word = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "运行字", DataType = typeof(int) };
                values.ById[word.VariableId] = word;
                values.ByName[word.Name] = word;
                word.Value = 0x1234;

                var over = Dispatch(values, Bit(ScadaActionType.SetBit, word, 32));
                var negative = Dispatch(values, Bit(ScadaActionType.SetBit, word, -1));

                Check("[BIT17] 位号 32 / -1 越界：各记一条 Error、写明可用范围 0~31，变量值一位都没动",
                    over.Lines.Count == 1 && over.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && over.Lines[0].Contains("位号 32 越界", StringComparison.Ordinal)
                    && negative.Lines.Count == 1 && negative.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && negative.Lines[0].Contains("位号 -1 越界", StringComparison.Ordinal)
                    && Equals(word.Value, 0x1234),
                    $"{over.Lines.FirstOrDefault()} ⏎ {negative.Lines.FirstOrDefault()} / value={word.Value}");

                var noIndex = Dispatch(values, Bit(ScadaActionType.SetBit, word));

                Check("[BIT18] 整型没填位号：Error 说清「必须填位号 0~31」，不按 bit0 猜",
                    noIndex.Lines.Count == 1
                    && noIndex.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && noIndex.Lines[0].Contains("必须填位号 0~31", StringComparison.Ordinal)
                    && Equals(word.Value, 0x1234),
                    Single(noIndex));

                // long 的位宽是 64，但设计口径只允许 0~31：第 32 位必须拒绝，不能"能用就放行"
                var wide = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "长整", DataType = typeof(long) };
                var wideValues = new FakeValueSource();
                wideValues.ById[wide.VariableId] = wide;
                wide.Value = 0L;
                var wideLog = Dispatch(wideValues, Bit(ScadaActionType.SetBit, wide, 32));

                Check("[BIT19] long 位号 32：虽在 64 位内，但超出设计口径 0~31 → 拒绝（可用位号写 0~31）",
                    wideLog.Lines.Count == 1
                    && wideLog.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && wideLog.Lines[0].Contains("的可用位号是 0~31", StringComparison.Ordinal)
                    && Equals(wide.Value, 0L),
                    Single(wideLog));

                // 非整型 / 非 Bool：拒绝，不做字符串拼接
                var ratio = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "比例", DataType = typeof(double) };
                var text = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "名称", DataType = typeof(string) };
                var mixed = new FakeValueSource();
                mixed.ById[ratio.VariableId] = ratio;
                mixed.ById[text.VariableId] = text;
                ratio.Value = 1.5d;
                text.Value = "旧文本";

                var doubleLog = Dispatch(mixed, Bit(ScadaActionType.SetBit, ratio, 0));
                var stringLog = Dispatch(mixed, Bit(ScadaActionType.ToggleBit, text));

                Check("[BIT20] 非 Bool / 非整型（double、string）拒绝：Error 带类型名，变量值不变（不把 \"1\" 拼到字符串上）",
                    doubleLog.Lines.Count == 1
                    && doubleLog.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && doubleLog.Lines[0].Contains("不是 Bool 或整型（Double）", StringComparison.Ordinal)
                    && stringLog.Lines.Count == 1
                    && stringLog.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && stringLog.Lines[0].Contains("不是 Bool 或整型（String）", StringComparison.Ordinal)
                    && Equals(ratio.Value, 1.5d)
                    && Equals(text.Value, "旧文本"),
                    $"{doubleLog.Lines.FirstOrDefault()} ⏎ {stringLog.Lines.FirstOrDefault()}");

                // 没选变量：Warn（配置漏了），根本不走值源
                var noVariableLog = Dispatch(values, new ScadaAction { Type = ScadaActionType.SetBit });

                Check("[BIT21] 没选变量：Warn 一句「置位：还没选变量，本条未执行」，不去问值源",
                    noVariableLog.Lines.Count == 1
                    && noVariableLog.Lines[0].StartsWith("W ", StringComparison.Ordinal)
                    && noVariableLog.Lines[0].EndsWith("置位：还没选变量，本条未执行", StringComparison.Ordinal),
                    Single(noVariableLog));

                // 找不到变量：改名/删了
                var ghost = Dispatch(values, new ScadaAction
                {
                    Type = ScadaActionType.ResetBit,
                    VariableId = Guid.NewGuid(),
                    VariableName = "已删除的字",
                    BitIndex = 2,
                });

                Check("[BIT22] 找不到变量（改名/删了）：Warn 把变量名与动作原文带出来",
                    ghost.Lines.Count == 1
                    && ghost.Lines[0].StartsWith("W ", StringComparison.Ordinal)
                    && ghost.Lines[0].Contains("复位 已删除的字.2 ← 0", StringComparison.Ordinal)
                    && ghost.Lines[0].EndsWith("找不到变量「已删除的字」，本条未执行", StringComparison.Ordinal),
                    Single(ghost));

                // 写不进：设备侧的原因原样带上来，不包一层"写入失败"把它盖掉
                var offline = new FakeValueHandle
                {
                    VariableId = Guid.NewGuid(),
                    Name = "离线字",
                    DataType = typeof(int),
                    WriteError = "设备未连接",
                };
                var offlineValues = new FakeValueSource();
                offlineValues.ById[offline.VariableId] = offline;
                offline.Value = 5;
                var offlineLog = Dispatch(offlineValues, Bit(ScadaActionType.SetBit, offline, 1));

                Check("[BIT23] 写不进（设备侧）：Error 且把变量自己给的原因原样带上来，值保持原样",
                    offlineLog.Lines.Count == 1
                    && offlineLog.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && offlineLog.Lines[0].EndsWith("写入失败：设备未连接", StringComparison.Ordinal)
                    && Equals(offline.Value, 5),
                    Single(offlineLog));

                // 当前值读不出来（null）：整型必须拒绝，绝不按 0 起算（那会把别的位全清掉）
                var blank = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "没读到", DataType = typeof(int) };
                var blankValues = new FakeValueSource();
                blankValues.ById[blank.VariableId] = blank;
                var blankLog = Dispatch(blankValues, Bit(ScadaActionType.SetBit, blank, 3));

                Check("[BIT24] 整型当前值读不出来（null）：拒绝，不按 0 起算（按 0 写回会清掉其余位）",
                    blankLog.Lines.Count == 1
                    && blankLog.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && blankLog.Lines[0].Contains("无法读-改-写", StringComparison.Ordinal)
                    && blank.Value == null,
                    Single(blankLog));
            }

            /// <summary>
            /// [BIT25~BIT26] 动作编辑器的候选与排序：三条位操作必须出现在下拉里、
            /// 与「写变量/增量/减量」相邻（同一组"改一个变量"），且显示名与日志同源。
            /// </summary>
            private static void CheckEditorChoices()
            {
                var host = new ScadaPage();
                var vm = new ScadaEventEditorViewModel(
                    host,
                    ScadaEventType.Pressed,
                    "按下",
                    null,
                    new FakeVariablePicker(),
                    new FakePagePicker(),
                    new FakeFlowPicker());

                var types = vm.ActionTypeChoices.Select(o => o.Type).ToArray();
                int index = Array.IndexOf(types, ScadaActionType.SetBit);

                Check("[BIT25] 动作下拉含位操作三条，且紧跟在「写变量/增量/减量」之后（组内顺序 SetBit→ResetBit→ToggleBit）",
                    index >= 3
                    && types[index - 1] == ScadaActionType.Decrement
                    && types[index - 2] == ScadaActionType.Increment
                    && types[index - 3] == ScadaActionType.WriteVariable
                    && types[index + 1] == ScadaActionType.ResetBit
                    && types[index + 2] == ScadaActionType.ToggleBit,
                    string.Join("/", types.Select(t => t.DisplayName())));

                Check("[BIT26] 下拉的显示名与日志/面板同源（不是另写一份中文），且三条各只出现一次",
                    vm.ActionTypeChoices.All(o => o.DisplayName == o.Type.DisplayName())
                    && new[] { ScadaActionType.SetBit, ScadaActionType.ResetBit, ScadaActionType.ToggleBit }
                        .All(t => types.Count(x => x == t) == 1),
                    string.Join("/", vm.ActionTypeChoices.Select(o => o.DisplayName)));

                // 编辑器里配一条位操作：不算危险动作（危险标记不亮），且 IsBitAction 让编辑区显形
                vm.IsConfigured = true;
                var action = vm.Actions![0];
                action.Type = ScadaActionType.SetBit;

                Check("[BIT27] 编辑器里配一条位操作：不算危险动作（危险标记不亮），IsBitAction 为真让「变量 + 位号」编辑区显形",
                    !vm.IsDangerous
                    && action.IsBitAction
                    && !action.IsMotionAction,
                    $"dangerous={vm.IsDangerous} / bit={action.IsBitAction} / motion={action.IsMotionAction}");
            }

            /// <summary>造一条指向某变量的位操作动作（Id 优先、名字兜底，与生产口径一致）</summary>
            private static ScadaAction Bit(ScadaActionType type, FakeValueHandle handle, int? bitIndex = null)
                => new()
                {
                    Type = type,
                    VariableId = handle.VariableId,
                    VariableName = handle.Name,
                    BitIndex = bitIndex,
                };

            /// <summary>把一条动作走一遍生产分发器（画面/流程/运动出口都不接线，本组用不到）</summary>
            private static LoggerStub Dispatch(FakeValueSource values, ScadaAction action)
            {
                var log = new LoggerStub();
                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                hook.Actions.Add(action);

                new ScadaActionDispatcher(log, values, new FakeNavigator()).Dispatch(hook, "按钮");

                return log;
            }

            private static string Single(LoggerStub log)
                => log.Lines.Count == 1 ? log.Lines[0] : string.Join(" ⏎ ", log.Lines);
        }
    }
}
