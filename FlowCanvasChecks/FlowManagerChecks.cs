using System;
using System.Linq;
using VisionMaster.Models;
using VisionMaster.ViewModels.DialogViewModels;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 流程管理 v1.1（<c>docs/UI设计基线/flow-manager/</c>）的展示层契约。
    ///
    /// 为什么值得钉（都是"静默"那一类）：
    ///  · <b>未配齐提示</b>：勾了「定时」但 TimerIntervalMs = 0 时流程**永不触发**
    ///    （FlowTimerScheduler.ShouldFire 直接 return false），勾了「变量」但 TriggerVariable
    ///    为空时永不匹配（EvaluateCore 的空串比较）。界面不提示 = 用户以为软件坏了。
    ///  · <b>运行中守卫（Q1）</b>：旧版两条命令都不判 RunState，运行中照样能禁用/锁定。
    ///    这条只在 VM 上钉得动——按钮 IsEnabled 绑定是第二道，第一道是 CanToggle/CanLock。
    ///  · <b>状态四态优先级</b>：锁定必须压过运行/启用，否则一条"已锁定"的流程会显示成"已启用"，
    ///    用户点运行没反应又查不出原因。
    ///  · <b>角色徽标文案来自 [Display(ShortName)]</b>（R40）：视图不写死中文，改显示名只改枚举。
    ///  · <b>调用参数草稿</b>：FlatPropertyGrid 是就地编辑，草稿必须是"确认才写回"，
    ///    且只写真正改过的字段（打开又直接确认不白刷版本链）。
    /// </summary>
    internal static class FlowManagerChecks
    {
        internal static void Run()
        {
            StatusPrecedence();
            RunningGuard();
            InvokeConfigNote();
            DerivedNote();
            RoleBadge();
            InvokeParamDraft();
        }

        // ==================================================================
        //  [FM1] 状态四态与优先级
        // ==================================================================
        private static void StatusPrecedence()
        {
            Section("[FM1] 流程管理：状态四态（锁定 > 运行中 > 启用/禁用）");

            var normal = new FlowItem(new FlowModel { FlowName = "A", IsEnabled = true });
            Check("已启用 → StatusKind=on / 文案「已启用」",
                normal.StatusKind == "on" && normal.StatusText == "已启用",
                $"{normal.StatusKind} / {normal.StatusText}");

            var disabled = new FlowItem(new FlowModel { FlowName = "B", IsEnabled = false });
            Check("已禁用 → StatusKind=off / 文案「已禁用」",
                disabled.StatusKind == "off" && disabled.StatusText == "已禁用",
                $"{disabled.StatusKind} / {disabled.StatusText}");

            var running = new FlowItem(new FlowModel { FlowName = "C", IsEnabled = true, RunState = FlowRunState.Running });
            Check("运行中 → StatusKind=run / 文案「运行中」",
                running.StatusKind == "run" && running.StatusText == "运行中",
                $"{running.StatusKind} / {running.StatusText}");

            var locked = new FlowItem(new FlowModel { FlowName = "D", IsEnabled = true, StepsEncrypted = true, RunState = FlowRunState.Running });
            Check("★已锁定压过运行中（锁定优先级最高，与旧 StatusText 同口径）",
                locked.StatusKind == "lock" && locked.StatusText == "已锁定" && locked.IsLocked,
                $"{locked.StatusKind} / {locked.StatusText}");

            // 模型变化必须重发：RunState 是 [JsonIgnore] 的运行时状态，靠 FlowModel.PropertyChanged 推。
            // 断言"StatusKind 在重发名单里"，不能只看最后一个——Refresh() 会一口气重发十几个派生属性，
            // 拿最后一个比会把一条正确的实现判红（本条第一次写就是这么错的）。
            var mutable = new FlowItem(new FlowModel { FlowName = "E", IsEnabled = true });
            var raised = new System.Collections.Generic.List<string>();
            mutable.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is { } n) raised.Add(n);
            };
            mutable.Flow.RunState = FlowRunState.Running;
            Check("★RunState 变化 → FlowItem 重发 StatusKind（视图不用手动刷新）",
                raised.Contains(nameof(FlowItem.StatusKind))
                && raised.Contains(nameof(FlowItem.CanToggle))
                && raised.Contains(nameof(FlowItem.CanLock)),
                raised.Count == 0 ? "(无通知)" : string.Join(", ", raised.Distinct().Take(6)) + " …");
        }

        // ==================================================================
        //  [FM2] Q1：运行中禁止锁定 / 启用变更
        // ==================================================================
        private static void RunningGuard()
        {
            Section("[FM2] 流程管理：运行中守卫（Q1 裁定「要补上对应的行为」）");

            var idle = new FlowItem(new FlowModel { FlowName = "Idle", IsEnabled = true });
            Check("未运行 → CanToggle / CanLock 都可点", idle.CanToggle && idle.CanLock,
                $"CanToggle={idle.CanToggle} CanLock={idle.CanLock}");

            var running = new FlowItem(new FlowModel { FlowName = "Run", IsEnabled = true, RunState = FlowRunState.Running });
            Check("★运行中 → CanToggle / CanLock 都为 false（行内两个键置灰）",
                !running.CanToggle && !running.CanLock,
                $"CanToggle={running.CanToggle} CanLock={running.CanLock}");
            Check("运行中 → ToolTip 解释「为什么点不了」",
                running.OpsToolTip.Contains("正在运行", StringComparison.Ordinal),
                running.OpsToolTip);

            var paused = new FlowItem(new FlowModel { FlowName = "Pause", IsEnabled = true, RunState = FlowRunState.Paused });
            Check("已暂停不算运行中（暂停态仍可改锁定/启用）", paused.CanToggle && paused.CanLock,
                $"RunState=Paused CanToggle={paused.CanToggle}");
        }

        // ==================================================================
        //  [FM3] 未配齐提示：与运行侧判据同源
        // ==================================================================
        private static void InvokeConfigNote()
        {
            Section("[FM3] 流程管理：未配齐提示（勾了定时/变量但缺参数 = 永不触发）");

            var timerUnset = new FlowItem(new FlowModel
            {
                FlowName = "T0",
                InvokeType = FlowInvokeType.Timer,
                TimerIntervalMs = 0,
            });
            Check("★勾了定时 + 间隔 0 → 提示「定时未设间隔」",
                timerUnset.InvokeConfigNote.Contains("定时未设间隔", StringComparison.Ordinal),
                timerUnset.InvokeConfigNote);

            var timerSet = new FlowItem(new FlowModel
            {
                FlowName = "T1",
                InvokeType = FlowInvokeType.Timer,
                TimerIntervalMs = 500,
            });
            Check("间隔填上 → 提示消失（空串由 DataTrigger 收起整行）",
                timerSet.InvokeConfigNote.Length == 0, $"「{timerSet.InvokeConfigNote}」");

            var varUnset = new FlowItem(new FlowModel
            {
                FlowName = "V0",
                InvokeType = FlowInvokeType.Variable,
                TriggerVariable = "  ",
            });
            Check("★勾了变量 + 触发源空白 → 提示「变量未选触发源」",
                varUnset.InvokeConfigNote.Contains("变量未选触发源", StringComparison.Ordinal),
                varUnset.InvokeConfigNote);

            var varSet = new FlowItem(new FlowModel
            {
                FlowName = "V1",
                InvokeType = FlowInvokeType.Variable,
                TriggerVariable = "Start",
            });
            Check("触发源填上 → 提示消失", varSet.InvokeConfigNote.Length == 0, $"「{varSet.InvokeConfigNote}」");

            var both = new FlowItem(new FlowModel
            {
                FlowName = "Both",
                InvokeType = FlowInvokeType.Timer | FlowInvokeType.Variable,
                TimerIntervalMs = 0,
                TriggerVariable = "",
            });
            Check("两项都缺 → 合并成一句", both.InvokeConfigNote.Contains("定时未设间隔")
                && both.InvokeConfigNote.Contains("变量未选触发源")
                && both.InvokeConfigNote.EndsWith("不会触发", StringComparison.Ordinal),
                both.InvokeConfigNote);

            var httpOnly = new FlowItem(new FlowModel
            {
                FlowName = "H",
                InvokeType = FlowInvokeType.Http,
            });
            Check("没勾定时/变量 → 不产生未配齐提示（HTTP 无参数）",
                httpOnly.InvokeConfigNote.Length == 0, $"「{httpOnly.InvokeConfigNote}」");

            // 齿轮只在勾了定时或变量时出现：没勾的流程点了也配不了东西
            Check("齿轮可见性 = 勾了定时或变量",
                timerUnset.NeedsInvokeParams && varUnset.NeedsInvokeParams
                && !httpOnly.NeedsInvokeParams,
                $"timer={timerUnset.NeedsInvokeParams} http={httpOnly.NeedsInvokeParams}");
        }

        // ==================================================================
        //  [FM4] 派生小字：删掉「调用关系」列之后"仅子程序 / 手动"的唯一表达
        // ==================================================================
        private static void DerivedNote()
        {
            Section("[FM4] 流程管理：派生小字（仅子程序 / 手动）");

            var subOnly = new FlowItem(new FlowModel
            {
                FlowName = "Sub",
                Role = FlowRole.None,
                InvokeType = FlowInvokeType.Subroutine,
            });
            Check("★只勾子程序且无其它触发 → 「仅子程序」",
                subOnly.DerivedNote == "仅子程序", $"「{subOnly.DerivedNote}」");

            var manual = new FlowItem(new FlowModel { FlowName = "Manual", InvokeType = FlowInvokeType.Manual });
            Check("一位都没勾 → 「手动」", manual.DerivedNote == "手动", $"「{manual.DerivedNote}」");

            // 工位流程通常同时勾「子程序」+「整线」：漏判会让它在列表里被标成"只能被调用"，
            // 用户就不敢按整线启动了（FlowModel.IsSubroutineOnly 注释里的原话）
            var subAndLine = new FlowItem(new FlowModel
            {
                FlowName = "Station",
                InvokeType = FlowInvokeType.Subroutine | FlowInvokeType.LineStart,
            });
            Check("★子程序 + 整线 → 不出「仅子程序」（工位不能被标成只能被调用）",
                subAndLine.DerivedNote.Length == 0, $"「{subAndLine.DerivedNote}」");

            var subAndHttp = new FlowItem(new FlowModel
            {
                FlowName = "SubHttp",
                InvokeType = FlowInvokeType.Subroutine | FlowInvokeType.Http,
            });
            Check("子程序 + HTTP → 不出「仅子程序」（有外部触发）",
                subAndHttp.DerivedNote.Length == 0, $"「{subAndHttp.DerivedNote}」");

            // 未配齐是警告，优先级高于中性小字：两种提示不同时出现，避免行高被撑成两行
            var warn = new FlowItem(new FlowModel
            {
                FlowName = "Warn",
                InvokeType = FlowInvokeType.Subroutine | FlowInvokeType.Timer,
                TimerIntervalMs = 0,
            });
            Check("未配齐提示在时，派生小字让位（同一时刻只出一行）",
                warn.DerivedNote.Length == 0 && warn.InvokeConfigNote.Length > 0,
                $"Derived=「{warn.DerivedNote}」 Config=「{warn.InvokeConfigNote}」");
        }

        // ==================================================================
        //  [FM5] 角色徽标：文案来自 FlowRole 的 [Display(ShortName)]
        //        2026-10-28 起没有"主流程"角色：生产主任务由「调用方式」里的「整线」表达
        // ==================================================================
        private static void RoleBadge()
        {
            Section("[FM5] 流程管理：角色徽标（Home / End）");

            var home = new FlowItem(new FlowModel { FlowName = "回原点", Role = FlowRole.Home });
            var end = new FlowItem(new FlowModel { FlowName = "收尾", Role = FlowRole.End });
            var lineStart = new FlowItem(new FlowModel
            {
                FlowName = "主检测",
                InvokeType = FlowInvokeType.LineStart,
            });
            var none = new FlowItem(new FlowModel { FlowName = "普通", Role = FlowRole.None });

            Check("★回零 / 收尾 两档文案来自 [Display(ShortName)]",
                home.RoleBadgeText == "回零" && end.RoleBadgeText == "收尾",
                $"{home.RoleBadgeText} / {end.RoleBadgeText}");
            Check("无角色 → 空串（徽标整体收起）；整线成员也没有角色徽标（由「整线」胶囊表达）",
                none.RoleBadgeText.Length == 0 && lineStart.RoleBadgeText.Length == 0,
                $"「{none.RoleBadgeText}」/「{lineStart.RoleBadgeText}」");
            Check("样式键 home / end",
                home.RoleBadgeKey == "home" && end.RoleBadgeKey == "end"
                && none.RoleBadgeKey.Length == 0,
                $"{home.RoleBadgeKey} / {end.RoleBadgeKey}");
            Check("ToolTip 说明骨架流程不允许删除",
                home.RoleBadgeToolTip.Contains("不允许删除", StringComparison.Ordinal),
                home.RoleBadgeToolTip);
        }

        // ==================================================================
        //  [FM6] 调用参数草稿（A 方案：EasyDialog 属性网格）
        // ==================================================================
        private static void InvokeParamDraft()
        {
            Section("[FM6] 流程管理：调用参数草稿（定时 / 变量，确认才写回）");

            var flow = new FlowModel
            {
                FlowName = "P",
                InvokeType = FlowInvokeType.Timer | FlowInvokeType.Variable,
                TimerIntervalMs = 0,
                TriggerVariable = string.Empty,
            };

            var draft = new InvokeParamEditModel(flow, new[] { "Start", "Reset" });

            var visible = UI.CustomControl.PropertyGrid.PropertyGridDefaults.GetVisibleProperties(draft)
                .Select(p => p.Name)
                .ToList();
            Check("属性网格渲染 5 项（2 可编辑 + 3 只读说明）",
                visible.Count == 5
                && visible.Contains(nameof(InvokeParamEditModel.TimerIntervalMs))
                && visible.Contains(nameof(InvokeParamEditModel.TriggerVariable))
                && visible.Contains(nameof(InvokeParamEditModel.EffectNote)),
                string.Join(", ", visible));

            Check("草稿铺开时取活模型现值",
                draft.TimerIntervalMs == 0 && draft.TriggerVariable.Length == 0,
                $"{draft.TimerIntervalMs} / 「{draft.TriggerVariable}」");

            // 取消 = 整份丢弃：不调 ApplyToModel，活模型一个字节都不动
            draft.TimerIntervalMs = 800;
            draft.TriggerVariable = "  Start  ";
            Check("★改草稿不碰活模型（取消路径靠这条成立）",
                flow.TimerIntervalMs == 0 && flow.TriggerVariable.Length == 0,
                $"{flow.TimerIntervalMs} / 「{flow.TriggerVariable}」");

            draft.ApplyToModel();
            Check("确认 → 写回活模型，且触发变量去首尾空白",
                flow.TimerIntervalMs == 800 && flow.TriggerVariable == "Start",
                $"{flow.TimerIntervalMs} / 「{flow.TriggerVariable}」");

            // 打开又直接确认：值没变，不许白刷版本链（FlowModel 的 setter 会发 PropertyChanged）
            int changes = 0;
            flow.PropertyChanged += (_, _) => changes++;
            var noop = new InvokeParamEditModel(flow, null);
            noop.ApplyToModel();
            Check("★打开又直接确认 → 不写活模型（0 次 PropertyChanged）",
                changes == 0, $"changes={changes}");

            // 生效情况必须当场说清"填了也不生效"，而不是等用户发现流程不动
            var notTicked = new InvokeParamEditModel(
                new FlowModel { FlowName = "Q", InvokeType = FlowInvokeType.Http, TimerIntervalMs = 500 },
                null);
            Check("未勾定时/变量 → 说明区写明「填了也不会用」",
                notTicked.EffectNote.Contains("也不会用", StringComparison.Ordinal),
                notTicked.EffectNote);

            var tickedUnset = new InvokeParamEditModel(
                new FlowModel { FlowName = "R", InvokeType = FlowInvokeType.Timer, TimerIntervalMs = 0 },
                null);
            Check("勾了但没配 → 说明区写明「不会触发」",
                tickedUnset.EffectNote.Contains("不会触发", StringComparison.Ordinal),
                tickedUnset.EffectNote);

            var ready = new InvokeParamEditModel(
                new FlowModel
                {
                    FlowName = "S",
                    InvokeType = FlowInvokeType.Timer | FlowInvokeType.Variable,
                    TimerIntervalMs = 200,
                    TriggerVariable = "Start",
                },
                new[] { "Start" });
            Check("两位都配齐 → 说明区报「可正常触发」",
                ready.EffectNote.Contains("可正常触发", StringComparison.Ordinal),
                ready.EffectNote);
        }
    }
}
