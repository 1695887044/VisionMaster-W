using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using static FlowCanvasChecks.Program;
// VisionMaster.Models.ParallelExecutionMode 与 System.Linq.ParallelExecutionMode 撞名（本文件 using System.Linq），别名固定
using ParallelExecutionMode = VisionMaster.Models.ParallelExecutionMode;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「并行分组参数」的守门断言（W1-W7）。
    ///
    /// 【2026-10-09 面板收编】用户裁决"右键已有重命名，参数没必要新建一个视图"——
    /// 专属视图面板（ParallelGroupConfigView/ViewModel + Prism 注册 "ParallelGroupConfig"）退役，
    /// 参数面板改为标准属性面板：EasyDialog.ShowPropertyGridSync + FlatPropertyGrid 反射渲染
    /// ParallelGroupEditModel 草稿（[SuperDisplay] 契约），分组名改走右键「重命名分组」。
    ///
    /// 断言口径（headless，不开真窗）：
    ///  · 面板侧直接构造 ParallelGroupEditModel 并调它的公开面（草稿铺开 / ApplyToModel 写回 / 取消不落）；
    ///    EasyDialog 是静态弹窗、不经 IDialogService——打开链路的断言在 ProcessTreeInteractionChecks V1；
    ///  · FlatPropertyGrid 的反射契约（[SuperDisplay] 才渲染、枚举 [Description]）用
    ///    PropertyGridDefaults.GetVisibleProperties / 枚举成员反射直接验，不 new WPF 控件；
    ///  · 右键命令族（分组重命名 RenameParallelGroupCommand 三态 + 版本号口径对照）补在 W5-W7，
    ///    与 V8 的分支命令互相不重叠（那边管分支增删改，这边管组头重命名）。
    ///
    /// 旧 W 段（面板草稿铺开/保存事务性/分组名与分支只读总览）已随视图删除，由本文件的
    /// EditModel 断言替代；分支结构的断言全部留在 ProcessTreeInteractionChecks V8 不动。
    /// 真并发的执行语义不在本文件的射程（归 ParallelExecutionChecks 的 P1-P31）。
    /// </summary>
    internal static class ParallelGroupConfigChecks
    {
        public static void Run()
        {
            Section("[W1-W7] 并行分组参数（FlatPropertyGrid 草稿 + 右键命令族；旧专属视图已删除）");

            W1_DraftSpreadsFromLiveModel();
            W2_ApplyToModelWritesBackViaSetters();
            W3_CancelDiscardsDraft();
            W4_JoinTimeoutWriteBack();
            W5_PropertyGridContract();
            W6_RenameParallelGroupCommand();
            W7_VersionBumpDiscipline();
        }

        // ------------------------------------------------------------------
        //  面板侧（ParallelGroupEditModel）
        // ------------------------------------------------------------------

        /// <summary>W1：草稿铺开——打开弹窗那一刻，活模型三值进草稿（这是 FlatPropertyGrid 渲染的初始态）。</summary>
        private static void W1_DraftSpreadsFromLiveModel()
        {
            var h = new Harness();
            var par = h.Parallel("并行分组_0");
            h.Add(par);

            var edit = new ParallelGroupEditModel(par);
            Check("W1：草稿按活模型铺开（默认 Sequential / Inherit / 默认超时）",
                edit.ExecutionMode == ParallelExecutionMode.Sequential
                && edit.FailFastMode == FailFastMode.Inherit
                && edit.JoinTimeoutMs == ParallelStep.DefaultJoinTimeoutMs,
                $"模式={edit.ExecutionMode} 失败={edit.FailFastMode} 超时={edit.JoinTimeoutMs}");

            // 非默认值的活模型也要原样进草稿（铺开是"拷贝"而不是"贴默认"）
            var custom = h.Parallel("非默认组");
            custom.ExecutionMode = ParallelExecutionMode.Parallel;
            custom.FailFastMode = FailFastMode.On;
            custom.JoinTimeoutMs = 8000;
            h.Add(custom);

            var edit2 = new ParallelGroupEditModel(custom);
            Check("W1：非默认值活模型 → 草稿原样（Parallel / On / 8000）",
                edit2.ExecutionMode == ParallelExecutionMode.Parallel
                && edit2.FailFastMode == FailFastMode.On
                && edit2.JoinTimeoutMs == 8000,
                $"模式={edit2.ExecutionMode} 失败={edit2.FailFastMode} 超时={edit2.JoinTimeoutMs}");

            // 草稿编辑期间活模型不被污染（FlatPropertyGrid 就地写草稿，真身要等"确认"）
            edit2.ExecutionMode = ParallelExecutionMode.Sequential;
            edit2.JoinTimeoutMs = 42;
            Check("W1：编辑只落草稿，活模型零污染（确认前不写回）",
                custom.ExecutionMode == ParallelExecutionMode.Parallel && custom.JoinTimeoutMs == 8000,
                $"活模型 模式={custom.ExecutionMode} 超时={custom.JoinTimeoutMs}");
        }

        /// <summary>W2：ApplyToModel 三值写回——全走语义属性 setter，逐个落活模型。</summary>
        private static void W2_ApplyToModelWritesBackViaSetters()
        {
            var h = new Harness();
            var par = h.Parallel("并行分组_0");
            h.Add(par);

            var edit = new ParallelGroupEditModel(par)
            {
                ExecutionMode = ParallelExecutionMode.Parallel,
                FailFastMode = FailFastMode.On,
                JoinTimeoutMs = 3000,
            };

            int v0 = h.Flow.Version;
            edit.ApplyToModel();
            bool applied = par.ExecutionMode == ParallelExecutionMode.Parallel
                && par.FailFastMode == FailFastMode.On
                && par.JoinTimeoutMs == 3000;

            // 语义属性走 setter → FlowModel 版本链自动推进（三个属性各一次）
            Check("W2：ApplyToModel 三值写回 + 版本链自动推进（setter 语义属性）",
                applied && h.Flow.Version > v0,
                $"ok={applied} 模式={par.ExecutionMode} 失败={par.FailFastMode} 超时={par.JoinTimeoutMs} 版本 {v0} → {h.Flow.Version}");

            // 三值来回改都能落（写回是"覆盖"，不是"只能改一次"）
            var edit2 = new ParallelGroupEditModel(par)
            {
                ExecutionMode = ParallelExecutionMode.Sequential,
                FailFastMode = FailFastMode.Off,
                JoinTimeoutMs = 0,
            };
            edit2.ApplyToModel();
            Check("W2：二次写回（顺序 / Off / 0=未配置）",
                par.ExecutionMode == ParallelExecutionMode.Sequential
                && par.FailFastMode == FailFastMode.Off
                && par.JoinTimeoutMs == 0,
                $"模式={par.ExecutionMode} 失败={par.FailFastMode} 超时={par.JoinTimeoutMs}");

            // 三态枚举逐一回到中间档（Inherit），证明三档都能写、不是只认两档
            var edit3 = new ParallelGroupEditModel(par) { FailFastMode = FailFastMode.Inherit };
            edit3.ApplyToModel();
            Check("W2：失败聚合三档逐一可写（Inherit 中间档）",
                par.FailFastMode == FailFastMode.Inherit, $"{par.FailFastMode}");

            // 值没变就不写（口径承自旧面板 TryApplyToModel 的"只写不同值"：打开又直接确认不白刷）
            var vBeforeNoop = h.Flow.Version;
            var edit4 = new ParallelGroupEditModel(par);
            edit4.ApplyToModel();
            Check("W2：值没变 → ApplyToModel 零写回（版本号不白推）",
                h.Flow.Version == vBeforeNoop,
                $"版本 {vBeforeNoop} → {h.Flow.Version}");
        }

        /// <summary>W3：取消路径——不调 ApplyToModel 活模型零改动（EasyDialog 取消 = 草稿整份丢弃）。</summary>
        private static void W3_CancelDiscardsDraft()
        {
            var h = new Harness();
            var par = h.Parallel("并行分组_0");
            h.Add(par);

            var edit = new ParallelGroupEditModel(par)
            {
                ExecutionMode = ParallelExecutionMode.Parallel,
                FailFastMode = FailFastMode.Off,
                JoinTimeoutMs = 2500,
            };

            // 模拟"取消"：StepParameterDialog 只有 ShowPropertyGridSync 返回 true 才调 ApplyToModel——
            // 返回 false 的路径什么都不做，草稿直接丢弃（活模型从未被碰）
            int v0 = h.Flow.Version;
            bool cancelled = true; // 取消：不调 ApplyToModel
            if (!cancelled)
                edit.ApplyToModel();

            Check("W3：取消（不 ApplyToModel）→ 活模型零改动、版本号不变",
                par.ExecutionMode == ParallelExecutionMode.Sequential
                && par.FailFastMode == FailFastMode.Inherit
                && par.JoinTimeoutMs == ParallelStep.DefaultJoinTimeoutMs
                && h.Flow.Version == v0,
                $"模式={par.ExecutionMode} 失败={par.FailFastMode} 超时={par.JoinTimeoutMs} 版本 {v0} → {h.Flow.Version}");
        }

        /// <summary>
        /// W4：汇合超时草稿 0 / 合法值 / 越界值的写回行为。
        /// 口径（与 ApplyToModel 注释、P32 断言互指）：草稿上贴 [RangeValidation(0,600000)]，
        /// 行内红字提示；用户仍点确认 → 原样写回，**不做二次夹取**——≤0 由引擎回落默认 15000。
        /// </summary>
        private static void W4_JoinTimeoutWriteBack()
        {
            var h = new Harness();
            var par = h.Parallel("并行分组_0");
            h.Add(par);
            par.JoinTimeoutMs = 3000;

            // 0 = 未配置：写回 0（引擎回落默认），不是夹成某个下限
            var editZero = new ParallelGroupEditModel(par) { JoinTimeoutMs = 0 };
            editZero.ApplyToModel();
            Check("W4：草稿 0 → 原样写回 0（= 未配置，引擎回落默认；0 不许被夹成别的值）",
                par.JoinTimeoutMs == 0, $"值={par.JoinTimeoutMs}");

            // 合法值
            var editValid = new ParallelGroupEditModel(par) { JoinTimeoutMs = 3000 };
            editValid.ApplyToModel();
            Check("W4：合法值 3000 → 写回 3000", par.JoinTimeoutMs == 3000, $"值={par.JoinTimeoutMs}");

            // 越界值：原样写回（行内 RangeValidation 已提示；引擎回落由 P32 守，这里不做第二套夹取）
            var editHigh = new ParallelGroupEditModel(par) { JoinTimeoutMs = 999999 };
            editHigh.ApplyToModel();
            var highKept = par.JoinTimeoutMs == 999999;

            var editLow = new ParallelGroupEditModel(par) { JoinTimeoutMs = -5 };
            editLow.ApplyToModel();
            var lowKept = par.JoinTimeoutMs == -5;

            Check("W4：越界值（999999 / -5）→ 原样写回不夹取（行内已提示，引擎回落归 P32）",
                highKept && lowKept,
                $"999999 原样保留={highKept}；-5 原样保留={lowKept}；当前值={par.JoinTimeoutMs}");

            // [RangeValidation] 特性本身贴对了（行内提示文案里的"0 = 使用默认"是口径的一部分）
            var rangeAttr = typeof(ParallelGroupEditModel)
                .GetProperty(nameof(ParallelGroupEditModel.JoinTimeoutMs))!
                .GetCustomAttribute<UI.Attributes.RangeValidationAttribute>();
            Check("W4：草稿 JoinTimeoutMs 贴 [RangeValidation(0, 600000)]（0 = 未配置的有义值，下限是 0 不是 100）",
                rangeAttr != null && rangeAttr.Min == 0 && rangeAttr.Max == 600000
                && rangeAttr.ErrorMessage.Contains("0 = 使用默认"),
                $"Min={rangeAttr?.Min} Max={rangeAttr?.Max} 文案='{rangeAttr?.ErrorMessage}'");
        }

        /// <summary>
        /// W5：FlatPropertyGrid 反射契约——[SuperDisplay] 的属性才渲染、枚举 [Description] 已补、
        /// 说明文本动态值实时算。这是"参数面板 = 标准属性面板"的成色证明。
        /// </summary>
        private static void W5_PropertyGridContract()
        {
            var h = new Harness();
            var par = h.Parallel("并行分组_0");
            h.Add(par);

            // 契约边界：GetVisibleProperties 只认 [SuperDisplay] 且 Visible==true——
            // 草稿的公开属性必须全部贴上（三个可编辑值 + 四条只读说明），否则面板上静默缺行
            var visible = UI.CustomControl.PropertyGrid.PropertyGridDefaults
                .GetVisibleProperties(new ParallelGroupEditModel(par))
                .Select(p => p.Name)
                .ToList();
            bool allShown = new[]
            {
                nameof(ParallelGroupEditModel.ExecutionMode),
                nameof(ParallelGroupEditModel.FailFastMode),
                nameof(ParallelGroupEditModel.JoinTimeoutMs),
                nameof(ParallelGroupEditModel.FailFastInheritHintText),
                nameof(ParallelGroupEditModel.ForceSequentialHintText),
                nameof(ParallelGroupEditModel.DebugHintText),
                nameof(ParallelGroupEditModel.GroupScopeText),
            }.All(visible.Contains);
            Check("W5：草稿公开属性全部带 [SuperDisplay]（FlatPropertyGrid 一行不缺地渲染）",
                allShown && visible.Count == 7,
                $"可见 {visible.Count} 行：{string.Join("/", visible)}");

            // 枚举 [Description]：EnumGenerator 优先读它显示（没贴就上屏英文枚举名）
            var modeDesc = GetEnumDescription(ParallelExecutionMode.Parallel);
            var failOnDesc = GetEnumDescription(FailFastMode.On);
            var failOffDesc = GetEnumDescription(FailFastMode.Off);
            Check("W5：枚举成员 [Description] 齐备（顺序/真并发 + 强制开/强制关）",
                modeDesc == "真并发（分支各自执行，按失败规则汇合）"
                && failOnDesc == "强制开：任一分支业务失败即取消其余分支"
                && failOffDesc == "强制关：各跑各的，不取消兄弟",
                $"模式='{modeDesc}' 开='{failOnDesc}' 关='{failOffDesc}'");

            // 说明区动态值：「继承全局的当前＝开/关」实时读全局静态策略（进程级静态，测完立即复位）
            var edit = new ParallelGroupEditModel(par);
            GlobalParallelConfig.FailFastByDefault = false;
            string textOff = edit.FailFastInheritHintText;
            GlobalParallelConfig.FailFastByDefault = true;
            string textOn = edit.FailFastInheritHintText;
            GlobalParallelConfig.FailFastByDefault = false;
            Check("W5：「继承全局」当前值跟随 GlobalParallelConfig.FailFastByDefault",
                textOff.Contains("关") && !textOff.Contains("开") && textOn.Contains("开"),
                $"false → '{textOff}'；true → '{textOn}'");

            // 强制顺序总闸提示：开 = 有内容；关 = 空串（进程级静态，测完立即复位）
            GlobalParallelConfig.ForceSequential = true;
            string forced = new ParallelGroupEditModel(par).ForceSequentialHintText;
            GlobalParallelConfig.ForceSequential = false;
            string normal = new ParallelGroupEditModel(par).ForceSequentialHintText;
            Check("W5：ForceSequential 开 → 提示有内容；关 → 空串",
                forced.Contains("强制顺序") && normal.Length == 0,
                $"开='{forced}' 关='{normal}'");

            // 入口指引文案把"去哪改名字"说清楚（重命名入口已从参数面板撤走，唯一入口是右键）
            Check("W5：说明区给出分组名与分支的入口指引（右键，不在本面板）",
                new ParallelGroupEditModel(par).GroupScopeText.Contains("流程栏右键")
                && new ParallelGroupEditModel(par).GroupScopeText.Contains("重命名分组"),
                $"'{edit.GroupScopeText}'");
        }

        // ------------------------------------------------------------------
        //  右键命令族（RenameParallelGroupCommand，2026-10-09 分组名改走右键）
        // ------------------------------------------------------------------

        /// <summary>流程栏 VM 的公共夹具（口径同 ProcessTreeInteractionChecks：可注入出口 + 构造完 Deactivate）</summary>
        private static ProcessViewModel NewProcessVm(Harness h, List<string> feedback)
        {
            var vm = new ProcessViewModel(h.Workspace, new SilentDialogService())
            {
                ShowFeedback = (message, success) => feedback.Add((success ? "成功:" : "警示:") + message),
                ConfirmRenameParallelBranch = (title, defaultValue) => (false, defaultValue),
                ConfirmRenameParallelGroup = (title, defaultValue) => (false, defaultValue),
            };
            vm.Deactivate();
            return vm;
        }

        /// <summary>
        /// W6：组头右键「重命名分组」三态——取消不动 / 空白拒 / 确认写回（裁空白）。
        /// 与分支重命名（V8）同款体验，但命令靶是 ParallelStep（组头）。
        /// </summary>
        private static void W6_RenameParallelGroupCommand()
        {
            var h = new Harness();
            var par = h.Parallel("并行分组_0");
            h.Add(par);

            var feedback = new List<string>();
            var pvm = NewProcessVm(h, feedback);

            // 取消：什么都不做（连版本号都不动）
            int v0 = h.Flow.Version;
            pvm.RenameParallelGroupCommand.Execute(par);
            Check("W6：分组改名取消 → StepName 与版本号都不动",
                par.StepName == "并行分组_0" && h.Flow.Version == v0,
                $"名='{par.StepName}' 版本 {v0} → {h.Flow.Version}");

            // 空白名：拒绝并给提示
            pvm.ConfirmRenameParallelGroup = (title, defaultValue) => (true, "   ");
            pvm.RenameParallelGroupCommand.Execute(par);
            Check("W6：改成空白名 → 拒绝（名字不变 + 提示「分组名不能为空」）",
                par.StepName == "并行分组_0" && feedback.Any(f => f.Contains("分组名不能为空")),
                $"名='{par.StepName}' 反馈={string.Join("|", feedback)}");

            // 确认改名：两端空白裁掉，写回走 setter
            feedback.Clear();
            pvm.ConfirmRenameParallelGroup = (title, defaultValue) => (true, "  左右工位并行  ");
            pvm.RenameParallelGroupCommand.Execute(par);
            Check("W6：确认改名 → StepName 写回（两端空白裁掉）",
                par.StepName == "左右工位并行",
                $"名='{par.StepName}'");

            // 名字没变：什么都不做（连 setter 都不碰，版本号不白推）
            int v1 = h.Flow.Version;
            pvm.ConfirmRenameParallelGroup = (title, defaultValue) => (true, "左右工位并行");
            pvm.RenameParallelGroupCommand.Execute(par);
            Check("W6：名字没变 → 版本号不白推一次",
                h.Flow.Version == v1, $"版本 {v1} → {h.Flow.Version}");

            // 命令靶为 null（没选中组头）时空跑不抛
            pvm.RenameParallelGroupCommand.Execute(null);
            Check("W6：命令靶 null → 空跑不抛（名字不变）",
                par.StepName == "左右工位并行", $"名='{par.StepName}'");
        }

        /// <summary>
        /// W7：版本号口径对照——**分组名不需要手动 Version++**（ParallelStep.StepName 是
        /// StepModel 的语义属性，setter 自动推）；分支名（StepCollection.StepName）不在
        /// FlowModel 的监听面里、右键命令**必须手动推**。两条对照着守，防止后人搞混"哪个要手动推"。
        /// </summary>
        private static void W7_VersionBumpDiscipline()
        {
            var h = new Harness();
            var par = h.Parallel("并行分组_0");
            h.Add(par);

            var feedback = new List<string>();
            var pvm = NewProcessVm(h, feedback);

            // 分组改名：setter 自动推进版本号（RenameParallelGroup 内部没有手动 Version++）
            int v0 = h.Flow.Version;
            pvm.ConfirmRenameParallelGroup = (title, defaultValue) => (true, "改名后的组");
            pvm.RenameParallelGroupCommand.Execute(par);
            bool groupRenamed = par.StepName == "改名后的组" && h.Flow.Version > v0;

            // 对照：分支改名同样递增——但那是右键命令手动推的（StepCollection.StepName 没人监听）。
            // 把分支的 StepName 直接赋值证"手动推是必须的"：直接改不发版本链，
            // 而命令路径改完显式 Version++（下面再跑一次命令证命令路径有推）。
            var branch = par.Children[0];
            int v1 = h.Flow.Version;
            branch.StepName = "直接改分支名";
            bool directAssignNoBump = h.Flow.Version == v1; // 直接赋值：版本链不动（证明监听面不含它）
            pvm.ConfirmRenameParallelBranch = (title, defaultValue) => (true, "命令改分支名");
            pvm.RenameParallelBranchCommand.Execute(branch);
            bool commandPathBumps = branch.StepName == "命令改分支名" && h.Flow.Version > v1;

            Check("W7：分组改名版本号自动递增（语义属性 setter）；分支改名靠命令手动推（两条口径并存）",
                groupRenamed && directAssignNoBump && commandPathBumps,
                $"组改名 递增={groupRenamed}；分支直接赋值不递增={directAssignNoBump}；分支走命令递增={commandPathBumps}；"
                + $"版本 {v0} → {v1} → {h.Flow.Version}");
        }

        // ------------------------------------------------------------------
        //  反射小工具
        // ------------------------------------------------------------------

        /// <summary>取枚举成员的 [Description] 文案（EnumGenerator 的显示口径，没贴返回枚举名）</summary>
        private static string GetEnumDescription(Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attr = field?.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
            return attr?.Description ?? value.ToString();
        }
    }
}
