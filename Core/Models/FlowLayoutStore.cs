using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

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
        /// 为缺布局的步骤生成坐标：每层按 SortId 纵向排布，容器整体占位后下层右移。
        /// 只补缺项，已有坐标的步骤一律不动，避免用户手工布局被覆盖。
        /// 返回本次新增的项数。
        /// </summary>
        public int AutoLayout(IEnumerable<StepModel> steps, double originX = 0, double originY = 0)
        {
            int added = LayoutLevel(steps.ToList(), originX, originY);
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
        /// 单层布局：本层内纵向堆叠，遇到容器则递归排布其分支并为本层预留高度。
        /// 返回本层新增（含递归）的项数。
        /// </summary>
        private int LayoutLevel(List<StepModel> level, double x, double y)
        {
            const double RowHeight = 90;
            const double ColumnGap = 260;
            const double BranchIndent = 220;

            int added = 0;
            double cursor = y;

            foreach (var step in level.OrderBy(s => s.SortId))
            {
                if (!Nodes.ContainsKey(step.StepID))
                {
                    Nodes[step.StepID] = new NodeLayout { X = x, Y = cursor };
                    added++;
                }
                else
                {
                    // 已有坐标：沿用其位置，但仍要为子层预留出向下的空间
                    var existing = Nodes[step.StepID];
                    cursor = Math.Max(cursor, existing.Y);
                }

                cursor += RowHeight;

                if (step is IContainerStep container)
                {
                    // 分支横向错开排布，避免不同分支的节点重叠
                    double branchX = x + BranchIndent;
                    foreach (var branch in container.Children)
                    {
                        added += LayoutLevel(branch.Steps.ToList(), branchX, cursor);
                        branchX += ColumnGap;
                    }
                }
            }

            return added;
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
