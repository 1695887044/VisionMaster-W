using System;
using Prism.Mvvm;
using UI.Attributes;
using VisionMaster.Services;

namespace VisionMaster.Models
{
    /// <summary>
    /// 「批量设置显示窗口」弹窗（<c>EasyDialog.ShowPropertyGridSync</c> + FlatPropertyGrid）的编辑草稿。
    ///
    /// 【为什么是草稿而不是直接写流程】FlatPropertyGrid 是**就地编辑**：点"取消"不会回滚
    ///（与 ParallelGroupEditModel 同一理由）。这里只承载一个待写入的窗口号，
    /// 真正的写回发生在调用方拿到"确认"之后，走 <see cref="BatchDisplayWindowService.Apply"/>。
    ///
    /// 【为什么只给一个数字】第一期按"统一设成同一个值"实现：0 = 全部不显示，
    /// 1~9 = 全部投到同一格。运行时主界面每格默认只保留最新一帧，多个插件投同一格时
    /// 只有最后发布的那张可见——这是用户在选择窗口号时需要知道的事实，写在说明区里。
    /// </summary>
    public sealed class BatchDisplayWindowEditModel : BindableBase
    {
        private readonly BatchDisplayWindowPlan _plan;
        private int _displayViewIndex;

        public BatchDisplayWindowEditModel(BatchDisplayWindowPlan plan)
        {
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _displayViewIndex = plan.SuggestInitialValue();
        }

        /// <summary>
        /// 目标显示窗口号：0 = 不发布；1~9 = 主界面第 N 格画布。
        /// 越界由 <see cref="RangeValidationAttribute"/> 行内提示，调用方确认前再判一次
        ///（<see cref="BatchDisplayWindowService.IsValidValue"/>）——越界值不写进流程。
        /// </summary>
        [SuperDisplay(
            Name = "显示窗口",
            Group = new[] { "显示窗口" },
            Order = 0,
            Description = "0 = 不发布；1~9 = 主界面第 N 格画布。写入后等价于逐个打开参数面板点「确定」，下一轮运行自动生效")]
        [RangeValidation(0, 9, "显示窗口号必须是 0~9（0 = 不显示）")]
        public int DisplayViewIndex
        {
            get => _displayViewIndex;
            set => SetProperty(ref _displayViewIndex, value);
        }

        /// <summary>影响范围摘要（可写目标数 / 已禁用数 / 已链接跳过数 / 当前值分布）</summary>
        [SuperDisplay(Name = "影响范围", Group = new[] { "说明" }, Order = 0, IsReadOnly = true)]
        public string ScopeText => _plan.BuildSummary();

        /// <summary>范围边界：脚本插件的逐变量窗口不在本功能里；运行中禁止修改</summary>
        [SuperDisplay(Name = "范围说明", Group = new[] { "说明" }, Order = 1, IsReadOnly = true)]
        public string ScopeNote =>
            "只改带「显示窗口」的算子（采集 / 预处理 / 匹配 / 检测 / 结果显示等 15 种）；"
            + "脚本插件的逐输出变量窗口不在此列。运行中禁止修改，暂停或停止后可改。";

        /// <summary>同格覆盖提示：多个插件投同一格时只有最后发布的一张可见</summary>
        [SuperDisplay(Name = "显示效果", Group = new[] { "说明" }, Order = 2, IsReadOnly = true)]
        public string SameWindowNote =>
            "主界面每格默认只保留最新一帧：多个插件设成同一个窗口号时，运行时只有最后发布的那张图可见。"
            + "想同时看多路画面，请分到不同窗口号。";
    }
}
