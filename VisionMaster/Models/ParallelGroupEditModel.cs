using System;
using Prism.Mvvm;
using UI.Attributes;
using VisionMaster.Services;

namespace VisionMaster.Models
{
    /// <summary>
    /// 「并行分组参数」弹窗（EasyDialog.ShowPropertyGridSync + FlatPropertyGrid）的**编辑草稿**。
    ///
    /// 【为什么要有这个 DTO，而不是把 ParallelStep 直接交给弹窗】
    ///   ① FlatPropertyGrid 是**就地编辑**：TextBox/ComboBox 双向写回传进去的实例，
    ///      点"取消"不会回滚（见 MotionCardIdentityEdit 同款注释）——把活模型交出去，
    ///      取消之后它已经被改脏。草稿 + 确认才 ApplyToModel，取消 = 整份丢弃。
    ///   ② FlatPropertyGrid 只渲染带 <c>[SuperDisplay]</c> 的属性（PropertyGridDefaults
    ///      的契约边界）。ParallelStep 是序列化模型 + 语义属性，不该为了"弹窗长什么样"
    ///      往它身上贴界面特性；显示名/分组/顺序属于界面知识，放在这里才对。
    ///   ③ 分组名不在这里编辑（用户裁决：右键已有「重命名」，参数面板不再重复造重命名入口），
    ///      本草稿只承载三个语义属性：执行模式 / 失败聚合 / 汇合超时。
    ///
    /// 【校验与越界口径】汇合超时贴 [RangeValidation(0, 600000)]（0 = 未配置 → 引擎回落
    /// 默认 15000 ms，这是**有专门含义的值**，所以下限是 0 而不是 100）；行内红字由
    /// ValidationProcessor 呈现。用户仍可能带着越界值点确认——ApplyToModel 按原样写回，
    /// 不做二次夹取：≤0 由引擎回落默认（ParallelStep 注释与 P32 断言守着这条口径），
    /// 保持"草稿所见 = 写回所得"，别在写回层再发明一套夹取规则。
    /// </summary>
    public sealed class ParallelGroupEditModel : BindableBase
    {
        private readonly ParallelStep _target;

        /// <summary>打开弹窗这一刻活模型的执行模式（草稿铺开用）</summary>
        private ParallelExecutionMode _executionMode;

        /// <summary>打开弹窗这一刻活模型的失败聚合三态</summary>
        private FailFastMode _failFastMode;

        /// <summary>汇合超时草稿（0 = 未配置，引擎回落默认 15000 ms）</summary>
        private int _joinTimeoutMs;

        /// <summary>
        /// 从活模型铺开草稿。workspace 只用于说明文本的实时取值（继承全局的"当前＝开/关"），
        /// 参数写回走 ApplyToModel，本草稿不持有工作区引用——避免"草稿丢一半状态在工作区上"。
        /// </summary>
        public ParallelGroupEditModel(ParallelStep target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _executionMode = target.ExecutionMode;
            _failFastMode = target.FailFastMode;
            _joinTimeoutMs = target.JoinTimeoutMs;
        }

        /// <summary>执行模式：Sequential（顺序逐分支）/ Parallel（真并发）</summary>
        [SuperDisplay(
            Name = "执行模式",
            Group = new[] { "执行" },
            Order = 0,
            Description = "真并发由编译期把分支扇出到各自任务并同步汇合；顺序执行即一期行为，逐条跑完再往下走")]
        public ParallelExecutionMode ExecutionMode
        {
            get => _executionMode;
            set => SetProperty(ref _executionMode, value);
        }

        /// <summary>失败聚合三态：Inherit（继承全局）/ On（强制开）/ Off（强制关）</summary>
        [SuperDisplay(
            Name = "失败聚合",
            Group = new[] { "失败聚合" },
            Order = 0,
            Description = "分支业务失败（步骤 Success=false）时是否取消其余兄弟分支；只做提前取消 + 容器标 Failed，不产生异常、不置会话 Faulted")]
        public FailFastMode FailFastMode
        {
            get => _failFastMode;
            set => SetProperty(ref _failFastMode, value);
        }

        /// <summary>
        /// 汇合超时（毫秒）。0 = 未配置 → 运行期回落默认 15000 ms（0 是有专门含义的值，
        /// 所以下限贴 0 而不是 100）；越界原样写回，≤0 由引擎回落（P32 断言守这条口径）。
        /// </summary>
        [SuperDisplay(
            Name = "汇合超时（毫秒）",
            Group = new[] { "汇合" },
            Order = 0,
            Description = "并行组汇合阶段等全部分支收尾的软期限：超时按「容器失败 + Error 日志 + 放弃等待」收口。留空或 0 = 使用默认 15000 ms")]
        [RangeValidation(0, 600000, "汇合超时必须是 0 ~ 600000 之间的毫秒数；0 = 使用默认 15000 ms")]
        public int JoinTimeoutMs
        {
            get => _joinTimeoutMs;
            set => SetProperty(ref _joinTimeoutMs, value);
        }

        // ------------------------------------------------------------------
        //  只读说明区（Group="说明"）：把"动态值"与"去哪改"写清楚
        // ------------------------------------------------------------------

        /// <summary>「继承全局」的当前值是动态的（读全局静态策略），写进说明区实时算</summary>
        [SuperDisplay(Name = "继承全局的当前值", Group = new[] { "说明" }, Order = 0, IsReadOnly = true)]
        public string FailFastInheritHintText =>
            $"继承全局 = 当前「{(GlobalParallelConfig.FailFastByDefault ? "开" : "关")}」"
            + "（GlobalParallelConfig.FailFastByDefault，软件级设置实时变化）";

        /// <summary>全局强制顺序总闸的提示（未开启时为空串，行就不显内容）</summary>
        [SuperDisplay(Name = "强制顺序总闸", Group = new[] { "说明" }, Order = 1, IsReadOnly = true)]
        public string ForceSequentialHintText =>
            GlobalParallelConfig.ForceSequential
                ? "⚠ 全局已开启强制顺序（ForceSequential）：所有并行组都会退化顺序执行"
                : string.Empty;

        /// <summary>调试语义：调试运行/试运行一律退化顺序（与执行模式无关）</summary>
        [SuperDisplay(Name = "调试运行", Group = new[] { "说明" }, Order = 2, IsReadOnly = true)]
        public string DebugHintText => "调试运行 / 试运行时，并行组会就地退化为顺序执行";

        /// <summary>分组名与分支结构的入口指引（都不在本面板）</summary>
        [SuperDisplay(Name = "名称与分支", Group = new[] { "说明" }, Order = 3, IsReadOnly = true)]
        public string GroupScopeText =>
            "分组名与分支的增删改名在流程栏右键：组头右键 = 添加分支 / 重命名分组，分支胶囊右键 = 重命名 / 删除";

        // ------------------------------------------------------------------
        //  写回（弹窗"确认"才由 StepParameterDialog 调用；取消 = 草稿丢弃不落模型）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把三个语义属性从草稿写回活模型。**只写与活模型不同的值**（口径与旧面板
        /// TryApplyToModel 一致：打开又直接确认不白刷版本链）。全走 setter——
        /// StepModel 的语义属性发 PropertyChanged → FlowModel 版本链 → 重编译。
        /// 汇合超时不做二次校验/夹取：行内 RangeValidation 已提示，越界原样写回、
        /// ≤0 由引擎回落默认（与 ParallelStep.JoinTimeoutMs 注释、P32 断言互指）。
        /// </summary>
        public void ApplyToModel()
        {
            if (_target.ExecutionMode != ExecutionMode)
                _target.ExecutionMode = ExecutionMode;

            if (_target.FailFastMode != FailFastMode)
                _target.FailFastMode = FailFastMode;

            if (_target.JoinTimeoutMs != JoinTimeoutMs)
                _target.JoinTimeoutMs = JoinTimeoutMs;
        }
    }
}
