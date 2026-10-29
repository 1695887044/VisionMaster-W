using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisionMaster.Scada;
using VisionMaster.ViewModels;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [SM] 文本列表匹配模式（v6）：精确值 / 范围 / 位 / 位号。
        ///
        /// 四种模式的判定全部收在 <see cref="ScadaStateList.Find"/> 一个方法里，
        /// 所以这里只读模型与 JSON，不建窗口、不碰 WPF——断它比断界面更靠近事实源。
        /// 界面那侧（列显隐、提示文案）不是另写一套匹配，而是读同一个
        /// <see cref="ScadaStateList.ParseMode"/>，因此 [SM8] 只钉"开关接对了没有"：
        /// 四项候选、写副本、IsRangeMode 跟着模式翻、提交把 Mode 一起写回。
        ///
        /// 为什么每种模式都要断"无匹配"：默认文字是回退路径，
        /// 只断命中会漏掉"越界值被硬塞进最后一档"这类静默错档。
        /// </summary>
        private static class StateListModeChecks
        {
            public static void Run()
            {
                Section("[SM] 文本列表匹配模式：精确/范围/位/位号 + 克隆写回 + JSON 往返");
                RunStateListModeChecksCore();
            }
        }

        private static void RunStateListModeChecksCore()
        {
            // ---------------------------------------------------------------
            // ① 默认模式：精确值，Find/DisplayText 与 v5 完全一致
            // ---------------------------------------------------------------
            var exact = new ScadaStateList { Name = "设备状态", DefaultText = "未知" };
            exact.Entries.Add(new ScadaStateEntry { Value = 0, Text = "停机" });
            exact.Entries.Add(new ScadaStateEntry { Value = 1, Text = "运行" });

            var defaultOk = exact.Mode == ScadaStateList.ModeExact
                && exact.ModeKind == ScadaStateMatchMode.Exact
                && exact.Find(0)?.Text == "停机"
                && exact.Find(1)?.Text == "运行"
                && exact.Find(2) == null
                && exact.DisplayText(2) == "未知";

            // 坏值/空值不许抛，也不许"读成别的模式"：一律回到精确值
            exact.Mode = "这不是一个模式";
            var badNormalized = exact.Mode == ScadaStateList.ModeExact
                && exact.ModeKind == ScadaStateMatchMode.Exact;

            Check("[SM1] 默认是精确值：Find/DisplayText 与 v5 一致；坏模式串归一回精确值",
                defaultOk && badNormalized,
                $"mode={exact.Mode}/{exact.ModeKind} / 0={exact.DisplayText(0)} / 2={exact.DisplayText(2)} / bad→{exact.Mode}");

            // ---------------------------------------------------------------
            // ② 范围：闭区间，边界值各归各档；"值"列完全不参与
            //    三条条目的 Value 全是 0（默认值），能命中 51/999 就证明没读 Value
            // ---------------------------------------------------------------
            var range = new ScadaStateList { Name = "温度区间", Mode = ScadaStateList.ModeRange, DefaultText = "越界" };
            range.Entries.Add(new ScadaStateEntry { Min = 0, Max = 50, Text = "正常" });
            range.Entries.Add(new ScadaStateEntry { Min = 51, Max = 80, Text = "预警" });
            range.Entries.Add(new ScadaStateEntry { Min = 81, Max = 999, Text = "报警" });

            Check("[SM2] 范围：0~50 / 51~80 / 81~999 闭区间，边界各归各档、越界回退默认文字",
                range.ModeKind == ScadaStateMatchMode.Range
                && range.Find(50)?.Text == "正常"
                && range.Find(51)?.Text == "预警"
                && range.Find(999)?.Text == "报警"
                && range.Find(1000) == null
                && range.DisplayText(1000) == "越界",
                $"50={range.DisplayText(50)} / 51={range.DisplayText(51)} / 999={range.DisplayText(999)} / 1000={range.DisplayText(1000)}");

            // ---------------------------------------------------------------
            // ③ 位：变量非 0 → "值=1"的条目，为 0 → "值=0"的条目
            // ---------------------------------------------------------------
            var bit = new ScadaStateList { Name = "运行状态", Mode = ScadaStateList.ModeBit, DefaultText = "未知" };
            bit.Entries.Add(new ScadaStateEntry { Value = 0, Text = "停机" });
            bit.Entries.Add(new ScadaStateEntry { Value = 1, Text = "运行" });

            Check("[SM3] 位：变量 0 → 停机，非 0（5 / -1）→ 运行",
                bit.ModeKind == ScadaStateMatchMode.Bit
                && bit.Find(0)?.Text == "停机"
                && bit.Find(5)?.Text == "运行"
                && bit.Find(-1)?.Text == "运行",
                $"0={bit.DisplayText(0)} / 5={bit.DisplayText(5)} / -1={bit.DisplayText(-1)}");

            // ---------------------------------------------------------------
            // ④ 位号：条目 Value 是位号，该位为 1 即命中；多条命中取第一条
            // ---------------------------------------------------------------
            var bitIndex = new ScadaStateList { Name = "报警字", Mode = ScadaStateList.ModeBitIndex, DefaultText = "无" };
            bitIndex.Entries.Add(new ScadaStateEntry { Value = 0, Text = "急停" });
            bitIndex.Entries.Add(new ScadaStateEntry { Value = 3, Text = "超温" });

            Check("[SM4] 位号：1 → 位0、8 → 位3、9 → 两条都置位取第一条、0 → 无匹配",
                bitIndex.ModeKind == ScadaStateMatchMode.BitIndex
                && bitIndex.Find(1)?.Text == "急停"
                && bitIndex.Find(8)?.Text == "超温"
                && bitIndex.Find(9)?.Text == "急停"
                && bitIndex.Find(0) == null
                && bitIndex.DisplayText(0) == "无",
                $"1={bitIndex.DisplayText(1)} / 8={bitIndex.DisplayText(8)} / 9={bitIndex.DisplayText(9)} / 0={bitIndex.DisplayText(0)}");

            // ---------------------------------------------------------------
            // ⑤ 副本：Clone/CopyFrom 都带上 Mode 与 Min/Max，改副本不回写原对象
            //    （管理器就是"改副本 → Commit 原地覆盖"，这一条漏了就是取消也丢数据）
            // ---------------------------------------------------------------
            var original = new ScadaStateList { Name = "原始", Mode = ScadaStateList.ModeRange };
            original.Entries.Add(new ScadaStateEntry { Min = 10, Max = 20, Text = "低" });

            var clone = original.Clone();
            var cloneInherited = clone.Mode == ScadaStateList.ModeRange
                && clone.Entries.Count == 1
                && clone.Entries[0].Min == 10
                && clone.Entries[0].Max == 20;

            clone.Mode = ScadaStateList.ModeBit;
            clone.Entries[0].Min = 99;
            clone.Entries[0].Max = 199;
            clone.Entries[0].Text = "改过";

            var target = new ScadaStateList { Name = "目标", Mode = ScadaStateList.ModeExact };
            target.Entries.Add(new ScadaStateEntry { Value = 7, Text = "旧条目" });
            target.CopyFrom(original);
            var copied = target.Mode == ScadaStateList.ModeRange
                && target.Entries.Count == 1
                && target.Entries[0].Min == 10
                && target.Entries[0].Max == 20;

            target.Entries[0].Min = 555;

            Check("[SM5] Clone/CopyFrom 带上 Mode 与 Min/Max，改副本不回写原对象",
                cloneInherited
                && copied
                && original.Mode == ScadaStateList.ModeRange
                && original.Entries[0].Min == 10
                && original.Entries[0].Max == 20
                && original.Entries[0].Text == "低"
                && clone.Mode == ScadaStateList.ModeBit
                && clone.Entries[0].Min == 99
                && target.Entries[0].Min == 555,
                $"原={original.Mode}/{original.Entries[0].Min}~{original.Entries[0].Max}「{original.Entries[0].Text}」"
                + $" / 克隆={clone.Mode}/{clone.Entries[0].Min}~{clone.Entries[0].Max}"
                + $" / 覆盖={target.Mode}/{target.Entries[0].Min}~{target.Entries[0].Max}");

            // ---------------------------------------------------------------
            // ⑥ JSON 往返：Mode / Min / Max 真的落盘再读回
            // ---------------------------------------------------------------
            var document = new ScadaDocument();
            document.StateLists.Add(new ScadaStateList
            {
                Name = "区间",
                Mode = ScadaStateList.ModeRange,
                Entries =
                {
                    new ScadaStateEntry { Min = 3, Max = 9, Text = "中" },
                },
            });

            var json = JsonConvert.SerializeObject(document);
            var roundTrip = JsonConvert.DeserializeObject<ScadaDocument>(json)!;

            Check("[SM6] .vms 往返：Mode / Min / Max 落盘（键名与模型一致）再读回不丢",
                json.Contains("\"Mode\":\"Range\"", StringComparison.Ordinal)
                && json.Contains("\"Min\":3", StringComparison.Ordinal)
                && json.Contains("\"Max\":9", StringComparison.Ordinal)
                && roundTrip.StateLists.Count == 1
                && roundTrip.StateLists[0].Mode == ScadaStateList.ModeRange
                && roundTrip.StateLists[0].Entries.Count == 1
                && roundTrip.StateLists[0].Entries[0].Min == 3
                && roundTrip.StateLists[0].Entries[0].Max == 9
                && roundTrip.StateLists[0].Entries[0].Text == "中",
                $"lists={roundTrip.StateLists.Count} / mode={roundTrip.StateLists[0].Mode}"
                + $" / {roundTrip.StateLists[0].Entries[0].Min}~{roundTrip.StateLists[0].Entries[0].Max}");

            // ---------------------------------------------------------------
            // ⑦ 旧 JSON（没有 Mode/Min/Max 三个键）：读回必须仍是精确值 + 0/0
            //    这是"旧工程零改动"的唯一证据：默认值来自字段初始化，不是迁移代码
            // ---------------------------------------------------------------
            var legacyJson = JObject.Parse(json);
            var legacyList = legacyJson["StateLists"]?[0] as JObject;
            legacyList?.Remove("Mode");
            if (legacyList?["Entries"] is JArray legacyEntries)
            {
                foreach (var entry in legacyEntries.OfType<JObject>())
                {
                    entry.Remove("Min");
                    entry.Remove("Max");
                }
            }

            var legacy = legacyJson.ToObject<ScadaDocument>()!;

            Check("[SM7] 旧 JSON（无 Mode/Min/Max 键）：读回 Mode=精确值、Min/Max=0，条目其余字段不丢",
                legacy.StateLists.Count == 1
                && legacy.StateLists[0].Mode == ScadaStateList.ModeExact
                && legacy.StateLists[0].ModeKind == ScadaStateMatchMode.Exact
                && legacy.StateLists[0].Entries.Count == 1
                && legacy.StateLists[0].Entries[0].Min == 0
                && legacy.StateLists[0].Entries[0].Max == 0
                && legacy.StateLists[0].Entries[0].Text == "中",
                $"mode={legacy.StateLists[0].Mode} / {legacy.StateLists[0].Entries[0].Min}~{legacy.StateLists[0].Entries[0].Max}"
                + $" / text={legacy.StateLists[0].Entries[0].Text}");

            // ---------------------------------------------------------------
            // ⑧ 管理器：四项候选、写副本、列显隐开关、提示文案、提交把 Mode 一起写回
            // ---------------------------------------------------------------
            var managerDocument = new ScadaDocument();
            var managerList = new ScadaStateList { Name = "设备状态" };
            managerDocument.StateLists.Add(managerList);

            var manager = new StateListManagerViewModel(managerDocument);

            var optionValues = manager.ModeOptions.Select(option => option.Value).ToArray();
            var optionsOk = optionValues.SequenceEqual(new[]
                {
                    ScadaStateList.ModeExact,
                    ScadaStateList.ModeRange,
                    ScadaStateList.ModeBit,
                    ScadaStateList.ModeBitIndex,
                })
                && manager.ModeOptions.All(option => !string.IsNullOrWhiteSpace(option.Name));

            var exactHint = manager.ModeHint;
            manager.Mode = ScadaStateList.ModeRange;
            var copyMode = manager.SelectedList!.Mode;
            var rangeHint = manager.ModeHint;
            var rangeFlags = manager.IsRangeMode && !manager.IsExactMode && !manager.IsBitIndexMode;

            manager.SelectedList.Entries.Add(new ScadaStateEntry { Min = 10, Max = 20, Text = "低" });
            manager.Commit();

            manager.Mode = ScadaStateList.ModeBitIndex;
            var bitIndexHint = manager.ModeHint;

            Check("[SM8] 管理器：四项模式 / 只改副本 / IsRangeMode 跟着模式翻 / 提示随模式变 / 提交写回 Mode",
                optionsOk
                && copyMode == ScadaStateList.ModeRange          // 只改了副本
                && managerList.Mode == ScadaStateList.ModeRange  // Commit 后落回文档
                && rangeFlags
                && managerList.Entries.Count == 1
                && managerList.Entries[0].Min == 10
                && managerList.Entries[0].Max == 20
                && manager.IsBitIndexMode && !manager.IsRangeMode
                && !string.IsNullOrWhiteSpace(exactHint)
                && rangeHint != exactHint
                && rangeHint.Contains("闭区间")
                && bitIndexHint.Contains("位号"),
                $"候选={string.Join("/", optionValues)} / 副本={copyMode} / 文档={managerList.Mode}"
                + $" / range={rangeFlags} / 提示={rangeHint} | {bitIndexHint}");
        }
    }
}
