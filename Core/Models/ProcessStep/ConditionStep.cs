﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace VisionMaster.Models
{
    /// <summary>
    /// 条件步骤模型
    /// 支持 If-ElseIf-Else 分支结构
    /// </summary>
    public class ConditionStep : StepModel, IContainerStep
    {
        /// <summary>
        /// 本地变量列表
        /// 用于描述条件局部变量和上游端口变量的关系
        /// </summary>
        public ObservableCollection<LocalVariableItem> LocalVariables { get; set; } = new();

        /// <summary>
        /// 引用的运行时变量列表
        /// 这些变量在流程执行中由 VariableDefinitionPlugin 等节点动态写入 context.LocalVariables
        /// 条件表达式可直接引用这些变量名，如 "Score > 80"，Score 即为运行时变量
        /// </summary>
        public ObservableCollection<LocalVariableItem> RuntimeVariableRefs { get; set; } = new();

        private ObservableCollection<StepCollection> _children = new();
        private readonly ContainerStepCountWatcher _stepCountWatcher;

        /// <summary>
        /// 子分支集合
        ///
        /// 【为什么必须"带 setter + ObjectCreationHandling.Replace"两件套齐备】
        /// Newtonsoft 对"无 setter 的集合属性"只会 Populate —— 往构造函数预建的集合里 Add，
        /// 并不整体替换；于是存盘往返后分支翻倍：If 的 [If, Else] 变成 [If, Else, If, Else]，
        /// 且首个 If 分支的条件为空 → 图纸重新打开后直接编译不过。
        /// 但只加 Replace 却不给 setter 同样不行：新建的集合无法赋回属性，文件里的分支会被整体丢弃
        /// （实测 Count 正常却全是构造函数预建的默认值）。两件必须同时具备。
        /// setter 兜 null 是因为 .vm 可被人工编辑，一句 "Children": null 不该让流程打不开。
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
        /// 子步骤总数（各分支 Steps.Count 之和）。只读计算属性，供流程栏组头右侧统计；
        /// 变更通知由 <see cref="ContainerStepCountWatcher"/> 驱动。
        /// <para>[RuntimeState]：派生自结构，结构变更已单独计过版本；再计一次会让
        /// 每次往分支里加步骤 Version +2。</para>
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
        /// 是否为 If 型条件算子 —— 全链路唯一的识别口径。
        /// 判据取算子类型名（PluginTypeName）而不是显示名（PluginName）：
        /// 显示名是给用户看的、可能被本地化或改名，类型名才是算子身份。
        /// 构造函数与 UI（右键菜单可见性）都读这里，避免"两处各写一份 Contains"的口径漂移。
        /// </summary>
        [JsonIgnore]
        public bool IsIfLike => PluginTypeName?.Contains("If") == true;

        /// <summary>
        /// 创建条件步骤
        /// </summary>
        public ConditionStep(
            string icon,
            string pluginName,
            string pluginTypeName,
            string stepName = null
        )
            : base(icon, pluginName, pluginTypeName, stepName)
        {
            _stepCountWatcher = new ContainerStepCountWatcher(() => RaisePropertyChanged(nameof(TotalStepCount)));

            if (IsIfLike)
            {
                // If 算子默认创建 If 和 Else 两个分支
                Children.Add(
                    new StepCollection
                    {
                        BranchType = BranchType.If,
                        StepName = "If",
                        Expression = "", // 用户填写条件，如 "Score > 80"
                    }
                );

                Children.Add(
                    new StepCollection { BranchType = BranchType.Else, StepName = "Else" }
                );
            }
            else
            {
                // 兜底逻辑
                Children.Add(
                    new StepCollection { BranchType = BranchType.If, StepName = "默认分支" }
                );
            }

            _stepCountWatcher.Rebind(Children);
        }
    }
}
