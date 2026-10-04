using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;
using HalconDotNet;

namespace Core.Halcon.Models
{
    /// <summary>
    /// 图像集里的一帧：一次流程运行中某个步骤某个图像输出端口的一张图，
    /// 或一次"实时预览发布"的一张图。
    ///
    /// 所有权约定（重要）
    /// ---------
    /// <see cref="Image"/> 是采集侧 <c>CopyImage()</c> 出来的**独立副本**，
    /// 本对象独占它，<see cref="Dispose"/> 时释放。显示端（HalconBase）只读不释放，
    /// 所以"图集里换选中项"不会把别人的图释放掉。
    ///
    /// <see cref="Thumbnail"/> 是冻结过的位图（可跨线程读），仅供缩略图网格渲染；
    /// 大图仍然用 <see cref="Image"/> 交给 HALCON 窗口画，保证缩放/标注/取色能力不丢。
    /// </summary>
    public sealed class ImageFrame : IDisposable
    {
        private readonly string? _slotKeyOverride;

        /// <param name="slotKeyOverride">
        /// 唯一槽位键：传了这帧就"永不与别的帧合并"（插件主动注入的图用——列表要一条条列出来）；
        /// 不传则按"流程 + 步骤 + 端口"占槽（输出端口采集的图用，天然被端口数封顶）。
        /// </param>
        public ImageFrame(string? slotKeyOverride = null)
        {
            _slotKeyOverride = slotKeyOverride;
        }

        /// <summary>所属流程名（按流程分组的依据）。不在流程里发布时退化为固定伪流程名</summary>
        public string FlowName { get; init; } = string.Empty;

        /// <summary>步骤名（实时预览帧为"视图N"）</summary>
        public string StepName { get; init; } = string.Empty;

        /// <summary>输出端口名（实时预览帧为空）</summary>
        public string PortName { get; init; } = string.Empty;

        /// <summary>
        /// 归属窗口号（= 插件发布时的"显示窗口号" 1~9）。
        ///
        /// 多格布局下**每一格只显示属于自己那一格的图**（第 N 格 ↔ 窗口号 N），
        /// 每格各自维护自己的列表；「全部输出」那条单列表不做过滤、全收。
        /// 端口兜底采集出来的图没有窗口号，一律归到第 1 格。
        /// </summary>
        public int ViewIndex { get; set; } = 1;

        /// <summary>图像宽（像素）</summary>
        public int Width { get; init; }

        /// <summary>图像高（像素）</summary>
        public int Height { get; init; }

        /// <summary>采集时刻</summary>
        public DateTime Timestamp { get; init; } = DateTime.Now;

        /// <summary>全局递增序号，用于稳定排序（同一槽位被覆盖时也保持"最新"在末尾）</summary>
        public long Sequence { get; set; }

        /// <summary>
        /// 槽位键：同一"流程 + 步骤 + 端口"视为同一个位置，默认每轮覆盖保留最新；
        /// 构造时传了唯一键的帧（插件注入）不走这个合并。
        /// </summary>
        public string SlotKey => _slotKeyOverride ?? (FlowName + "\u0001" + StepName + "\u0001" + PortName);

        /// <summary>图像本体（本对象独占，Dispose 释放）</summary>
        public HImage? Image { get; set; }

        /// <summary>缩略图（冻结位图；转换失败时为 null，网格显示占位）</summary>
        public BitmapSource? Thumbnail { get; set; }

        /// <summary>测量标注层（与图像同帧显示）；null 表示无标注</summary>
        public IReadOnlyList<MeasureAnnotation>? Annotations { get; set; }

        /// <summary>标题（插件注入时指定，例如"缺陷检测结果"）；为空则回退到"步骤.端口"</summary>
        public string? Title { get; set; }

        /// <summary>插件注入时携带的键值信息（画布列表第二行显示）；null/空表示无</summary>
        public IReadOnlyList<ImageInfoRow>? InfoRows { get; set; }

        /// <summary>列表主标题：注入标题优先，其次"步骤.端口"，再次步骤名/流程名</summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Title)) return Title!;
                if (!string.IsNullOrEmpty(PortName)) return StepName + "." + PortName;
                return string.IsNullOrEmpty(StepName) ? FlowName : StepName;
            }
        }

        /// <summary>注入信息的单行摘要（"标签: 值   标签: 值"），列表行显示用；无信息时为空串</summary>
        public string InfoSummary =>
            InfoRows is { Count: > 0 } ? string.Join("   ", InfoRows.Select(r => r.ToString())) : string.Empty;

        /// <summary>列表副标题：有注入信息就显示信息，否则显示"流程 · 时间 · 尺寸"</summary>
        public string DetailText => InfoRows is { Count: > 0 } ? InfoSummary : MetaText;

        /// <summary>来源信息：流程 · 时间 · 尺寸（始终可用的兜底展示，也用作悬停提示）</summary>
        public string MetaText
        {
            get
            {
                // 注入帧没有端口名，此时 StepName 装的是"哪个插件注入的"，值得显出来；
                // 端口采集帧的步骤名已在主标题里（"步骤.端口"），这里就不重复了
                var source = string.IsNullOrEmpty(PortName) && !string.IsNullOrEmpty(StepName)
                    ? FlowName + "  ·  " + StepName
                    : FlowName;
                var size = Width > 0 && Height > 0 ? $"  ·  {Width}×{Height}" : string.Empty;
                return $"{source}  ·  {Timestamp:HH:mm:ss.fff}{size}";
            }
        }

        /// <summary>完整注入信息（每行一条），用于悬停提示；无信息时为 null</summary>
        public string? InfoText =>
            InfoRows is { Count: > 0 } ? string.Join(Environment.NewLine, InfoRows.Select(r => r.ToString())) : null;

        /// <summary>
        /// 悬停提示的全部内容：标题 / 注入信息（逐行） / 来源。
        ///
        /// 【为什么是整串文本，而不是在 XAML 里拼几个 TextBlock 的 ToolTip 内容】
        /// ToolTip 的内容元素游离在可视树之外，拿不到 ListBoxItem 的 DataContext，
        /// 它里面的 {Binding} 一律解析不到值（实测提示框整片空白）。
        /// 而绑定在元素自身 ToolTip 属性上的字符串，是在该元素的数据上下文里求值的，稳定可用。
        /// </summary>
        public string ToolTipText
        {
            get
            {
                var sb = new System.Text.StringBuilder(DisplayName);
                if (!string.IsNullOrEmpty(InfoSummary))
                    sb.Append(Environment.NewLine).Append(InfoSummary);

                return sb.Append(Environment.NewLine).Append(MetaText).ToString();
            }
        }

        /// <summary>释放图像本体（缩略图是托管位图，交给 GC）</summary>
        public void Dispose()
        {
            try
            {
                if (Image != null && Image.IsInitialized())
                    Image.Dispose();
            }
            catch
            {
                // 采集/释放跨越线程与 HALCON 原生层，释放失败不应影响调用方（图集淘汰是后台动作）
            }
            finally
            {
                Image = null;
            }
        }
    }
}
