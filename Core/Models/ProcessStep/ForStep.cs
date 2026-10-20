using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace VisionMaster.Models
{
    /// <summary>
    /// For 循环步骤模型
    /// 支持指定次数的循环执行
    /// </summary>
    public class ForStep : StepModel, IContainerStep
    {
        /// <summary>
        /// 默认循环次数（未被上游连线覆盖时使用）。
        /// 语义属性：必须走 SetProperty —— FlowModel 靠属性名查 [RuntimeState] 排除名单，
        /// 不在名单 → Version++ → 触发重编译；画布容器头的“N 次”摘要也靠这条通知刷新。
        /// </summary>
        public int DefaultLoopCount
        {
            get => field;
            set => SetProperty(ref field, value);
        } = 10;

        private ObservableCollection<StepCollection> _children = new();
        private readonly ContainerStepCountWatcher _stepCountWatcher;

        /// <summary>
        /// 循环体步骤集合
        /// 必须"带 setter + ObjectCreationHandling.Replace"两件套齐备，理由同 ConditionStep.Children：
        /// 构造函数已预建循环体，若属性只读，Newtonsoft 走 Populate 追加而非替换 → 存盘往返后循环体翻倍。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<StepCollection> Children
        {
            get => _children;
            set
            {
                _children = value ?? new ObservableCollection<StepCollection>();
                _stepCountWatcher?.Rebind(_children);
            }
        }

        /// <summary>
        /// 循环体里的步骤总数。只读计算属性，供流程栏组头右侧统计；
        /// 变更通知由 <see cref="ContainerStepCountWatcher"/> 驱动。
        /// <para>[RuntimeState]：它派生自 Children/Steps 的结构，结构变更已经由
        /// FlowModel.OnStructureChanged 计过一次版本；若这条派生通知再计一次，
        /// 每次往循环体里加步骤都会 Version +2（表现为"改一次要重编译两轮"）。</para>
        /// </summary>
        [RuntimeState]
        [JsonIgnore]
        public int TotalStepCount
        {
            get
            {
                var total = 0;
                foreach (var branch in Children)
                    total += branch?.Steps?.Count ?? 0;
                return total;
            }
        }

        /// <summary>
        /// 创建 For 循环步骤
        /// </summary>
        public ForStep(string icon, string pluginName, string pluginTypeName, string stepName = null)
            : base(icon, pluginName, pluginTypeName, stepName)
        {
            _stepCountWatcher = new ContainerStepCountWatcher(
                () => RaisePropertyChanged(nameof(TotalStepCount)),
                e =>
                {
                    if (e.PropertyName is nameof(StepCollection.Expression) or nameof(StepCollection.BranchType))
                        RaiseContainerSummary();
                });
            Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "循环体" });
            _stepCountWatcher.Rebind(Children);
        }
    }
}
