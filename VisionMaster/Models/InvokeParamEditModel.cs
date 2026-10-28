using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Mvvm;
using UI.Attributes;
using VisionMaster.Services;

namespace VisionMaster.Models
{
    /// <summary>
    /// 「调用参数」弹窗（<c>EasyDialog.ShowPropertyGridSync</c> + FlatPropertyGrid）的**编辑草稿**。
    /// 基线：<c>docs/UI设计基线/flow-manager/</c> v1.1 §1.8 / §9.2 的 A 方案（行内齿轮 → 就地小窗）。
    ///
    /// 【为什么需要这个弹窗 —— 这不是"锦上添花"】
    ///   「定时」读 <see cref="FlowModel.TimerIntervalMs"/>、「变量」读 <see cref="FlowModel.TriggerVariable"/>，
    ///   而这两个字段在全仓**没有任何界面入口**：流程管理旧版只有五个勾选框，流程右键菜单 11 项里
    ///   也没有定时设置。于是：
    ///     · TimerIntervalMs 恒为 0 → <c>FlowTimerScheduler.ShouldFire</c> 的
    ///       <c>if (flow.TimerIntervalMs &lt;= 0) return false;</c> 直接不触发；
    ///     · TriggerVariable 恒为空串 → <c>FlowVariableTriggerService.EvaluateCore</c> 拿空串和变量名
    ///       比较永不相等，永远等不到上升沿。
    ///   勾了却什么都不发生，界面也不提示——正是 PendingReason 机制想防的"勾了没反应 = 软件坏了"。
    ///
    /// 【为什么是草稿而不是直接编辑 FlowModel】
    ///   FlatPropertyGrid 是**就地编辑**：点"取消"不会回滚（与 ParallelGroupEditModel 同一理由）。
    ///   草稿 + 确认才 ApplyToModel，取消 = 整份丢弃，活模型一个字节都不动。
    ///
    /// 【为什么两个字段同框，而不是按勾选只显示一个】
    ///   FlatPropertyGrid 的可见性来自 <c>[SuperDisplay].Visible</c>，那是**属性级**的静态特性，
    ///   做不到"这一条流程勾了定时才显示间隔"。所以两个字段都放出来，用说明区讲清
    ///   "当前哪一位在生效"——少一个字段的清爽，换掉一整套动态特性管线。
    /// </summary>
    public sealed class InvokeParamEditModel : BindableBase
    {
        private readonly FlowModel _target;

        private int _timerIntervalMs;
        private string _triggerVariable;

        /// <summary>打开弹窗这一刻的调用方式快照（说明区用，避免边填边漂）</summary>
        private readonly FlowInvokeType _invokeType;

        /// <summary>
        /// 从活模型铺开草稿。<paramref name="candidateVariables"/> 只用于说明区的候选清单，
        /// 本草稿不持有工作区引用（避免"草稿丢一半状态在工作区上"）。
        /// </summary>
        public InvokeParamEditModel(FlowModel target, IEnumerable<string>? candidateVariables = null)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _timerIntervalMs = target.TimerIntervalMs;
            _triggerVariable = target.TriggerVariable ?? string.Empty;
            _invokeType = target.InvokeType;

            var names = (candidateVariables ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Take(8)
                .ToList();
            VariableCandidates = names.Count == 0
                ? "当前方案没有全局变量；变量触发需要先有变量"
                : string.Join("、", names) + (names.Count >= 8 ? " …" : string.Empty);
        }

        /// <summary>
        /// 定时间隔（毫秒）。0 = 未配置 → <b>该流程不会被定时触发</b>（不是"每 20ms 跑"）。
        /// 下限 <c>FlowTimerScheduler.MinIntervalMs = 20</c>，低于它的值由调度器夹到 20；
        /// 这里允许填 0（= 关掉），所以范围从 0 起。
        /// </summary>
        [SuperDisplay(
            Name = "定时间隔（毫秒）",
            Group = new[] { "定时" },
            Order = 0,
            Description = "按此周期自动执行一次。0 = 未配置（该流程不会被定时触发）；下限 20ms，低于它按 20ms 执行。"
                        + "节拍精度取决于生效的节拍源，启动日志会写明；流程正在运行时错过的拍不补跑")]
        [RangeValidation(0, 3600000, "定时间隔必须是 0 ~ 3600000 之间的毫秒数；0 = 未配置，不会触发")]
        public int TimerIntervalMs
        {
            get => _timerIntervalMs;
            set => SetProperty(ref _timerIntervalMs, value);
        }

        /// <summary>
        /// 触发变量名：该变量**由假变真**（上升沿）时跑一次。空 = 未配置 → 不会被变量触发。
        /// 名字必须与全局变量表里的名字逐字相同（Ordinal 比较，大小写敏感）。
        /// </summary>
        [SuperDisplay(
            Name = "触发变量名",
            Group = new[] { "变量" },
            Order = 0,
            Description = "该变量由假变真（上升沿）时触发一次。留空 = 未配置（该流程不会被变量触发）。"
                        + "名字按 Ordinal 逐字比较，大小写敏感；改名后要同步改这里")]
        public string TriggerVariable
        {
            get => _triggerVariable;
            set => SetProperty(ref _triggerVariable, value ?? string.Empty);
        }

        /// <summary>当前调用方式快照（只读）：告诉用户这两个字段此刻哪几个真的在生效</summary>
        [SuperDisplay(Name = "当前调用方式", Group = new[] { "说明" }, Order = 0, IsReadOnly = true)]
        public string InvokeSummary => _invokeType.DisplayText();

        /// <summary>生效判定（只读）：把"填了但不生效"当场说清楚，而不是等用户发现流程不动</summary>
        [SuperDisplay(Name = "生效情况", Group = new[] { "说明" }, Order = 1, IsReadOnly = true)]
        public string EffectNote
        {
            get
            {
                var notes = new List<string>(2);

                if ((_invokeType & FlowInvokeType.Timer) == 0)
                    notes.Add("未勾「定时」，间隔填了也不会用");
                else if (TimerIntervalMs <= 0)
                    notes.Add("勾了「定时」但间隔为 0 —— 不会触发");

                if ((_invokeType & FlowInvokeType.Variable) == 0)
                    notes.Add("未勾「变量」，触发变量填了也不会用");
                else if (string.IsNullOrWhiteSpace(TriggerVariable))
                    notes.Add("勾了「变量」但没填触发变量 —— 不会触发");

                return notes.Count == 0
                    ? "两个参数都已配齐，对应调用方式可正常触发"
                    : string.Join("；", notes);
            }
        }

        /// <summary>可选变量（只读，前 8 个）：名字类字段按 R33 应给候选，这里至少把现有名字摆出来</summary>
        [SuperDisplay(Name = "可选变量（前 8 个）", Group = new[] { "说明" }, Order = 2, IsReadOnly = true)]
        public string VariableCandidates { get; }

        /// <summary>
        /// 把草稿写回活模型。**只写与活模型不同的值**（打开又直接确认不白刷版本链）。
        /// 越界不做二次夹取：行内 RangeValidation 已提示，且 0 是有专门含义的值（未配置），
        /// 与 ParallelGroupEditModel 的"草稿所见 = 写回所得"口径一致。
        /// </summary>
        public void ApplyToModel()
        {
            if (_target.TimerIntervalMs != TimerIntervalMs)
                _target.TimerIntervalMs = TimerIntervalMs;

            var trigger = (TriggerVariable ?? string.Empty).Trim();
            if (!string.Equals(_target.TriggerVariable ?? string.Empty, trigger, StringComparison.Ordinal))
                _target.TriggerVariable = trigger;
        }
    }
}
