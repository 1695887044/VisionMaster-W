using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace VisionMaster.Models
{
    /// <summary>
    /// 单个节点在画布上的布局项
    /// </summary>
    public sealed class NodeLayout
    {
        /// <summary>画布坐标系下的 X（无限画布，可为负）</summary>
        public double X { get; set; }

        /// <summary>画布坐标系下的 Y</summary>
        public double Y { get; set; }

        /// <summary>
        /// 容器节点（If/While/For）是否折叠。
        /// 折叠后子画布不在当前层展开，只显示一个带徽标的容器节点
        /// </summary>
        public bool Collapsed { get; set; }
    }

    /// <summary>
    /// 布局变更事件参数。AffectedSteps 为本次变更涉及的步骤，供画布局部刷新
    /// </summary>
    public sealed class FlowLayoutChangedEventArgs : EventArgs
    {
        public IReadOnlyCollection<Guid> AffectedSteps { get; init; } = Array.Empty<Guid>();
    }

    /// <summary>
    /// 流程画布布局存储：StepID → 节点布局。
    ///
    /// 为什么必须与语义模型分离：FlowModel.Version 由「步骤集合变更」和「步骤属性变更」驱动，
    /// 而 Version 变化会在运行前触发重新编译。若把坐标做成 StepModel 的通知属性，
    /// 用户每拖一下鼠标都会让流程变脏并重编译一次，画布直接卡死。
    ///
    /// 因此本类：
    /// 1. 不继承 BindableBase，不进入 FlowModel.Steps 的 PropertyChanged 订阅链；
    /// 2. 变更只走 LayoutChanged 显式事件，供画布订阅；
    /// 3. 不参与编译——FlowCompiler 只读 StepModel，从不读布局。
    /// </summary>
    public sealed class FlowLayoutStore
    {
        /// <summary>
        /// 布局载体，随 FlowModel 一起存盘。
        /// 保留 public setter 是为了让 Newtonsoft 反序列化时整体替换；
        /// 业务代码不应直接赋值，请走 Set / Remove / AutoLayout。
        /// </summary>
        [JsonProperty("Nodes")]
        public Dictionary<Guid, NodeLayout> Nodes { get; set; } = new();

        /// <summary>
        /// 布局变更通知。与流程语义无关，不会影响 Version。
        /// 事件成员不参与 Newtonsoft 序列化，无需额外标注
        /// </summary>
        public event EventHandler<FlowLayoutChangedEventArgs>? LayoutChanged;

        /// <summary>
        /// 本次排位生效的折叠态覆盖（见 <see cref="AutoLayout"/> 的 collapseOverride 说明）。
        /// 只在 AutoLayout 调用期间有值，try/finally 保证出口置回 null
        /// </summary>
        private Func<Guid, bool>? _collapseOverride;

        /// <summary>已记录的布局项数量</summary>
        public int Count => Nodes.Count;

        /// <summary>
        /// 读取节点布局。返回 false 时 layout 为 null，表示该步骤尚未布局
        /// （画布应先走 AutoLayout 兜底，不要拿假坐标渲染）
        /// </summary>
        public bool TryGet(Guid stepId, out NodeLayout? layout)
        {
            if (Nodes.TryGetValue(stepId, out var found))
            {
                layout = found;
                return true;
            }

            layout = null;
            return false;
        }

        /// <summary>
        /// 按步骤取布局，缺项返回 null。供编译期/画布判定"是否需要自动布局"
        /// </summary>
        public NodeLayout? Find(Guid stepId)
            => Nodes.TryGetValue(stepId, out var found) ? found : null;

        /// <summary>
        /// 写入/更新节点坐标。重复写入同值不发事件，避免拖动时刷屏
        /// </summary>
        public void Set(Guid stepId, double x, double y, bool collapsed = false)
        {
            if (stepId == Guid.Empty) return;

            if (Nodes.TryGetValue(stepId, out var item))
            {
                if (item.X == x && item.Y == y && item.Collapsed == collapsed)
                    return;

                item.X = x;
                item.Y = y;
                item.Collapsed = collapsed;
            }
            else
            {
                Nodes[stepId] = new NodeLayout { X = x, Y = y, Collapsed = collapsed };
            }

            LayoutChanged?.Invoke(this, new FlowLayoutChangedEventArgs
            {
                AffectedSteps = new[] { stepId },
            });
        }

        /// <summary>
        /// 切换容器折叠态；尚无布局项时按"折叠"创建。
        /// 返回切换后的折叠状态，便于调用方直接刷新 UI
        /// </summary>
        public bool ToggleCollapsed(Guid stepId)
        {
            if (!Nodes.TryGetValue(stepId, out var item))
            {
                Nodes[stepId] = new NodeLayout { Collapsed = true };
                LayoutChanged?.Invoke(this, new FlowLayoutChangedEventArgs
                {
                    AffectedSteps = new[] { stepId },
                });
                return true;
            }

            item.Collapsed = !item.Collapsed;
            LayoutChanged?.Invoke(this, new FlowLayoutChangedEventArgs
            {
                AffectedSteps = new[] { stepId },
            });
            return item.Collapsed;
        }

        /// <summary>
        /// 直接设定折叠态（不翻转）；尚无布局项时按该状态创建。
        ///
        /// 与 <see cref="ToggleCollapsed"/> 的分工：交互动作用 Toggle（点一下翻一下），
        /// 批量设定（如"整理"后的现场恢复、断言夹具布置前置状态）用 Set。
        /// 注意：画布的"整理"重排**不再**靠事后逐个 SetCollapsed 恢复现场——排位期就用
        /// <see cref="AutoLayout"/> 的 collapseOverride 声明最终折叠态（见该方法的说明），
        /// 否则清库重排后每个容器都被当"新项"按折叠足迹排位，恢复成展开时几何就错位
        /// （2026-10-09 审查发现）。
        /// </summary>
        public bool SetCollapsed(Guid stepId, bool collapsed)
        {
            if (!Nodes.TryGetValue(stepId, out var item))
            {
                Nodes[stepId] = new NodeLayout { Collapsed = collapsed };
                LayoutChanged?.Invoke(this, new FlowLayoutChangedEventArgs
                {
                    AffectedSteps = new[] { stepId },
                });
                return collapsed;
            }

            if (item.Collapsed != collapsed)
            {
                item.Collapsed = collapsed;
                LayoutChanged?.Invoke(this, new FlowLayoutChangedEventArgs
                {
                    AffectedSteps = new[] { stepId },
                });
            }

            return item.Collapsed;
        }

        /// <summary>
        /// 移除节点布局（步骤删除时调用，避免残留垃圾项）
        /// </summary>
        public bool Remove(Guid stepId) => Nodes.Remove(stepId);

        /// <summary>
        /// 是否存在缺布局的步骤。画布加载完成后据此决定是否自动布局
        /// </summary>
        public bool HasMissing(IEnumerable<StepModel> steps)
        {
            foreach (var step in EnumerateSelfAndNested(steps))
            {
                if (!Nodes.ContainsKey(step.StepID)) return true;
            }
            return false;
        }

        /// <summary>
        /// 为缺布局的步骤生成坐标：每层按 SortId 纵向排布，容器按"分支列平铺 + 框体实际占位"参与推进
        /// （尺寸感知，见 <see cref="PlaceLevel"/>）。
        /// 只补缺项，已有坐标的步骤一律不动，避免用户手工布局被覆盖。
        ///
        /// 超深自动折叠（<see cref="AutoCollapseDepth"/>）：本次新布局的容器若深度超过阈值，
        /// 默认带折叠标记——深嵌套图不再铺成一屏对角线，浅层可读、深层按需点开。
        /// 已有布局的老流程不受影响（HasMissing 为假时根本不进这里）；用户手动展开后
        /// 坐标已在库、折叠标记也被翻掉，重画不会折回去。
        /// 返回本次新增的项数。
        /// </summary>
        /// <param name="collapseOverride">
        /// 本次排位要用的"最终折叠态"：非空时按 StepID 逐容器查询，返回 null 的容器仍按
        /// <see cref="AutoCollapseDepth"/> 阈值判定。
        ///
        /// 为什么需要它：画布"整理"重排会先把坐标库清空再调本方法，于是每个容器在
        /// <see cref="PlaceContainer"/> 眼里都是"新项"——若不给最终态，深于阈值的容器就按
        /// 折叠足迹（<see cref="CollapsedFrameWidth"/>×<see cref="CollapsedFrameHeight"/>）
        /// 参与父级的行/列推进，而重排后它们要渲染成展开：框体远大于父级预留，
        /// 同级泳道互压、后续兄弟被吞进框内（2026-10-09 审查发现）。
        /// 传了 override 后，**足迹与标记用同一个取值**：排位时就是最终态。
        /// </param>
        public int AutoLayout(
            IEnumerable<StepModel> steps,
            double originX = 0,
            double originY = 0,
            Func<Guid, bool>? collapseOverride = null)
        {
            int added = 0;
            _collapseOverride = collapseOverride;

            try
            {
                PlaceLevel(steps.ToList(), originX, originY, 1, ref added);
            }
            finally
            {
                // 出口必须清掉：override 只对本次排位有效，留在字段里会让后续
                // （补缺项/试验性布局）也按上一次的快照折叠
                _collapseOverride = null;
            }

            if (added > 0)
            {
                LayoutChanged?.Invoke(this, new FlowLayoutChangedEventArgs
                {
                    AffectedSteps = Nodes.Keys.ToArray(),
                });
            }
            return added;
        }

        /// <summary>
        /// 超深容器自动折叠的深度阈值（1 基）：第 <c>AutoCollapseDepth + 1</c> 层及更深的新布局容器
        /// 默认折叠。3 = 前三层展开（真实工艺常见深度），更深的收起来。
        /// </summary>
        public const int AutoCollapseDepth = 3;

        // ------------------------------------------------------------------
        //  布局度量（尺寸感知）：**画布侧同名常量以本处为唯一数值源**
        //
        //  Core 不引 VM，别名引用只能是 VM → Core 这个方向：
        //  VisionMaster\ViewModels\FlowCanvasViewModel 里那批同名常量已经改成这里的别名
        //  （public const double NodeWidth = FlowLayoutStore.NodeWidth;），改数值只改本处。
        //  两边不一致会直接击穿"框 ⊆ 泳道、同级列不相交"（2026-10-09 审查 #3 防漂移）。
        // ------------------------------------------------------------------

        /// <summary>模块盒标称尺寸（画布卡片固定 210×62）</summary>
        public const double NodeWidth = 210;
        public const double NodeHeight = 62;

        /// <summary>折叠容器的框体尺寸（只剩头带，等高一个普通模块盒）</summary>
        public const double CollapsedFrameWidth = 230;
        public const double CollapsedFrameHeight = 64;

        /// <summary>泳道/框体的内边距与头带高度</summary>
        public const double LanePadding = 14;
        public const double LaneHeaderHeight = 26;
        public const double FramePadding = 16;
        public const double FrameHeaderHeight = 36;

        /// <summary>空分支泳道的占位尺寸</summary>
        public const double EmptyLaneWidth = 150;
        public const double EmptyLaneHeight = 88;

        /// <summary>行距：上一行框体底缘 + RowGap = 下一行框体顶缘（兄弟行不叠）。画布侧不用，仅本处行推进</summary>
        public const double RowGap = 28;

        /// <summary>列距：上一列泳道右缘 + ColumnGap = 下一列泳道左缘（同级分支列不互压）</summary>
        public const double ColumnGap = 24;

        /// <summary>
        /// 单层布局：本层内纵向堆叠，遇到容器则递归铺其分支列。
        ///
        /// 为什么不再是固定行高/列距（旧 RowHeight=90 / ColumnGap=260 / BranchIndent=220）：
        /// 容器框的渲染几何是"子孙外接框 + 内边距"，框右缘比它所在列起点宽出
        /// （FramePadding + LanePadding + 各分支列宽）；固定列距下任何嵌套容器的框
        /// 都会越过下一列起点。顶层靠兄弟纵向堆叠侥幸躲过，并行分组"各分支横向平铺"
        /// 就原样暴露——2026-10-09 真机截图"流程画布太乱"的根因。
        ///
        /// 现在的口径（尺寸感知的两遍布局）：
        ///   · 度量（自底向上）：叶子占位 = NodeWidth/NodeHeight；容器框宽 = 各分支泳道宽之和 + 列距 + 框内边距，
        ///     框高 = 泳道并集高 + 上下内边距 + 头带；
        ///   · 派位（自顶向下）：行距按"上一行框体实际高 + RowGap"推进，列距按"上一列泳道宽 + ColumnGap"推进。
        /// 几何公式与画布渲染 ComputeContainerGeometry 逐行对齐。
        ///
        /// 只补缺项：已有坐标的步骤沿用原位置（绝不被自动布局冲掉），但仍按实际尺寸参与行/列推进。
        /// 返回本层内容（含递归容器框）的外接矩形；本层无步骤时返回 Rect.Empty。
        /// </summary>
        /// <param name="depth">当前层深（顶层 = 1），用于超深自动折叠判定</param>
        private Rect PlaceLevel(List<StepModel> level, double columnX, double topY, int depth, ref int added)
        {
            double cursor = topY;
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool any = false;

            foreach (var step in level.OrderBy(s => s.SortId))
            {
                if (step == null) continue;

                Rect rect;
                if (step is IContainerStep container && container.Children != null)
                    rect = PlaceContainer(step, container, columnX, cursor, depth, ref added);
                else
                    rect = PlaceLeaf(step, columnX, cursor, ref added);

                if (!any)
                {
                    minX = maxX = rect.Left;
                    minY = maxY = rect.Top;
                    any = true;
                }

                minX = Math.Min(minX, rect.Left);
                minY = Math.Min(minY, rect.Top);
                maxX = Math.Max(maxX, rect.Right);
                maxY = Math.Max(maxY, rect.Bottom);

                // 行推进：上一行框体的实际底缘 + 行距 = 下一行顶缘（框体向上膨胀的那段高度也算进来）
                cursor = rect.Bottom + RowGap;
            }

            return any ? new Rect(minX, minY, maxX - minX, maxY - minY) : Rect.Empty;
        }

        /// <summary>叶子步骤：缺坐标写 (columnX, topY)，已有坐标按记录值原样返回（不覆盖）</summary>
        private Rect PlaceLeaf(StepModel step, double columnX, double topY, ref int added)
        {
            if (TryGetBox(step.StepID, out var stored))
                return stored;

            Nodes[step.StepID] = new NodeLayout { X = columnX, Y = topY };
            added++;
            return new Rect(columnX, topY, NodeWidth, NodeHeight);
        }

        /// <summary>
        /// 容器：缺坐标写锚点（框体左上角），已有坐标沿用；折叠态只吃折叠框的占位尺寸。
        ///
        /// 折叠态取值（**足迹与标记必须是同一个值**）：
        ///   · 有 collapseOverride（画布"整理"清库重排）→ 以它为准，新建/已有项都按它落标记；
        ///     清库后每个容器都是"新项"，不覆盖就会用折叠足迹排位而最终渲染成展开 → 几何失配
        ///     （2026-10-09 审查发现：外层框高 2092 而排位只预留 700，兄弟被吞进框内）；
        ///   · 无 override（首次渲染补缺）→ 新建项按 <see cref="AutoCollapseDepth"/> 自动折叠、
        ///     已有项沿用库里的标记（用户手动展开的深容器不能被"补缺"重新按折叠足迹排位）。
        /// </summary>
        private Rect PlaceContainer(StepModel step, IContainerStep container, double columnX, double topY, int depth, ref int added)
        {
            bool? forced = _collapseOverride?.Invoke(step.StepID);

            if (!Nodes.TryGetValue(step.StepID, out var existing))
            {
                Nodes[step.StepID] = new NodeLayout
                {
                    X = columnX,
                    Y = topY,
                    Collapsed = forced ?? depth > AutoCollapseDepth,
                };
                added++;
                existing = Nodes[step.StepID];
            }
            else if (forced.HasValue && existing.Collapsed != forced.Value)
            {
                // 已有项也要跟随 override：本次排位的最终态就是它，否则又是"排位按 A、渲染按 B"
                existing.Collapsed = forced.Value;
            }

            var frame = PlaceExpanded(container, existing.X, existing.Y, depth, ref added);

            // 折叠态：渲染时框体只剩头带（等高一枚模块盒），但子孙坐标照铺——
            // 展开时不必先跑一次整理也能看到内容，也让 HasMissing 不会长期为真
            return existing.Collapsed
                ? new Rect(existing.X, existing.Y, CollapsedFrameWidth, CollapsedFrameHeight)
                : frame;
        }

        /// <summary>
        /// 展开容器的框体与分支列：先按列铺各分支内容（缺坐标的补上），再由泳道并集反算框体。
        /// 与画布渲染 ComputeContainerGeometry 同一套公式：
        ///   泳道 = 分支内容外接框 ± LanePadding（上侧再加 LaneHeaderHeight）；
        ///   框体 = 泳道并集 ± FramePadding（上侧再加 FrameHeaderHeight）。
        /// 返回框体矩形。
        ///
        /// 两遍走：第一遍按列序落位（列左缘依赖上一列实宽），第二遍定各列顶边——
        /// 空分支的顶边取"有内容列的最小顶"，与渲染侧 ComputeLaneRects 同一口径。
        /// 旧实现给空列固定顶（anchorY + 头带 + 内边距），内容被拖到更高处时框体比渲染多出一段
        ///（2026-10-09 审查 #4）。
        /// </summary>
        private Rect PlaceExpanded(IContainerStep container, double anchorX, double anchorY, int depth, ref int added)
        {
            double laneTop = anchorY + FrameHeaderHeight + FramePadding;   // 各列泳道统一顶边（并列/分支等高起点）
            double contentTop = laneTop + LaneHeaderHeight + LanePadding;
            double originX = anchorX + FramePadding;                       // 首列泳道左缘

            var columns = new List<(double Left, double Width, double Height, double? Top)>();
            double? populatedTop = null;   // 有内容列的最小顶（含头带）——空列与它对齐

            foreach (var branch in container.Children)
            {
                if (branch?.Steps == null) continue;

                double laneLeft, laneWidth, laneHeight;
                double? laneTopHere;

                if (branch.Steps.Count == 0)
                {
                    // 空分支：占位泳道，按列序落位（多条空泳道依次排开，不与内容同址）；
                    // 顶边留空，第二遍按有内容列的 min top 统一
                    laneLeft = originX;
                    laneTopHere = null;
                    laneWidth = EmptyLaneWidth;
                    laneHeight = EmptyLaneHeight;
                }
                else
                {
                    var content = PlaceLevel(branch.Steps.ToList(), originX + LanePadding, contentTop, depth + 1, ref added);
                    // 有内容的列：泳道左/上缘随内容外接框（用户拖过的旧坐标也不会漏在泳道外）
                    laneLeft = content.Left - LanePadding;
                    laneTopHere = content.Top - LanePadding - LaneHeaderHeight;
                    laneWidth = content.Width + LanePadding * 2;
                    laneHeight = content.Height + LanePadding * 2 + LaneHeaderHeight;
                    populatedTop = populatedTop == null
                        ? laneTopHere
                        : Math.Min(populatedTop.Value, laneTopHere.Value);
                }

                columns.Add((laneLeft, laneWidth, laneHeight, laneTopHere));

                // 列推进：上一列泳道右缘 + 列距 = 下一列泳道左缘（容器框宽度由度量结果决定，不再用固定列距）
                originX = laneLeft + laneWidth + ColumnGap;
            }

            if (columns.Count == 0)
            {
                // 畸形容器（一条分支都没有）：渲染侧同样画不出泳道，按折叠框占位兜底
                return new Rect(anchorX, anchorY, CollapsedFrameWidth, CollapsedFrameHeight);
            }

            // 全空容器：空列仍自框内左上起（渲染侧同口径）；混合容器：与有内容列的最小顶对齐
            double unifiedTop = populatedTop ?? laneTop;

            double minLaneLeft = double.MaxValue, minLaneTop = double.MaxValue;
            double maxLaneRight = double.MinValue, maxLaneBottom = double.MinValue;
            foreach (var column in columns)
            {
                double columnTop = column.Top ?? unifiedTop;
                minLaneLeft = Math.Min(minLaneLeft, column.Left);
                minLaneTop = Math.Min(minLaneTop, columnTop);
                maxLaneRight = Math.Max(maxLaneRight, column.Left + column.Width);
                maxLaneBottom = Math.Max(maxLaneBottom, columnTop + column.Height);
            }

            return new Rect(
                minLaneLeft - FramePadding,
                minLaneTop - FramePadding - FrameHeaderHeight,
                (maxLaneRight - minLaneLeft) + FramePadding * 2,
                (maxLaneBottom - minLaneTop) + FramePadding * 2 + FrameHeaderHeight);
        }

        /// <summary>读已有布局项的占位盒（缺项返回 false，调用方负责补坐标）</summary>
        private bool TryGetBox(Guid stepId, out Rect box)
        {
            if (Nodes.TryGetValue(stepId, out var layout))
            {
                box = new Rect(layout.X, layout.Y, NodeWidth, NodeHeight);
                return true;
            }

            box = Rect.Empty;
            return false;
        }

        /// <summary>
        /// 递归展开自身与所有嵌套容器内的步骤（含分支），供完整性检查使用
        /// </summary>
        private static IEnumerable<StepModel> EnumerateSelfAndNested(IEnumerable<StepModel> steps)
        {
            foreach (var step in steps)
            {
                yield return step;

                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        foreach (var nested in EnumerateSelfAndNested(branch.Steps))
                            yield return nested;
                    }
                }
            }
        }
    }
}
