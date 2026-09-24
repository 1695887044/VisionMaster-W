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
        /// 默认循环次数（未被上游连线覆盖时使用）
        /// </summary>
        public int DefaultLoopCount { get; set; } = 10;

        private ObservableCollection<StepCollection> _children = new();

        /// <summary>
        /// 循环体步骤集合
        /// 必须"带 setter + ObjectCreationHandling.Replace"两件套齐备，理由同 ConditionStep.Children：
        /// 构造函数已预建循环体，若属性只读，Newtonsoft 走 Populate 追加而非替换 → 存盘往返后循环体翻倍。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<StepCollection> Children
        {
            get => _children;
            set => _children = value ?? new ObservableCollection<StepCollection>();
        }

        /// <summary>
        /// 创建 For 循环步骤
        /// </summary>
        public ForStep(string icon, string pluginName, string pluginTypeName, string stepName = null)
            : base(icon, pluginName, pluginTypeName, stepName)
        {
            Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "循环体" });
        }
    }
}
