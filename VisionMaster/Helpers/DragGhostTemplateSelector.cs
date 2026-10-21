using System.Collections;
using System.Windows;
using System.Windows.Controls;
using VisionMaster.Models;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// 流程栏拖拽浮层的模板选择器：步骤卡 / 分支卡。
    ///
    /// 为什么需要它：流程栏的树里混着 <see cref="StepModel"/>（算子/容器）与
    /// <see cref="StepCollection"/>（If/Else/并行分支卡片）两种条目，而 gong 的
    /// DragAdornerTemplate 是"一个拖拽源一份模板"——写死一种，另一种会显示成空卡片。
    /// 两者都用同一个 <c>VisionMaster.Controls.DragGhostCard</c>，只是字段映射不同。
    /// </summary>
    public sealed class DragGhostTemplateSelector : DataTemplateSelector
    {
        /// <summary>步骤（StepModel）：Icon / StepName / Category</summary>
        public DataTemplate? StepTemplate { get; set; }

        /// <summary>分支卡片（StepCollection）：分支图标 / DisplayName / "分支"</summary>
        public DataTemplate? BranchTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is StepModel)
                return StepTemplate;

            if (item is StepCollection)
                return BranchTemplate;

            // gong 多选时 Data 是 List<object>：按第一个元素的类型判定（流程栏是单选，
            // 这里只是不把"列表"误判成分支卡）
            if (item is IEnumerable enumerable and not string)
            {
                foreach (var first in enumerable)
                {
                    if (first is StepModel) return StepTemplate;
                    if (first is StepCollection) return BranchTemplate;
                    break;
                }
            }

            return StepTemplate;
        }
    }
}
