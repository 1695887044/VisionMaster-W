using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 流程模型
    /// </summary>
    public class FlowModel:BindableBase
    {
        public string FlowID { get; set; } = Guid.NewGuid().ToString("N");

        public string FlowName
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }
        public string Description
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        public int Version
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }
        public FlowTriggerMode TriggerMode { get; set; } = FlowTriggerMode.Continuous;

        public int TimerIntervalMs { get; set; } = 1000;

        public ObservableCollection<StepModel> Steps { get; set; } = new();

        public FlowModel()
        {
            Steps.CollectionChanged += (s, e) =>
            {
                Version++; // 结构变了，直接脏掉

                // 🌟 核心：如果是新加的算子，也要监听它的内部属性
                if (e.NewItems != null)
                {
                    foreach (StepModel item in e.NewItems)
                        item.PropertyChanged += OnStepPropertyChanged;
                }

                // 🌟 核心：如果是删除的算子，记得解绑，防止内存泄漏
                if (e.OldItems != null)
                {
                    foreach (StepModel item in e.OldItems)
                        item.PropertyChanged -= OnStepPropertyChanged;
                }
            };
        }
        private void OnStepPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 排除掉一些不影响逻辑的属性（比如“是否选中”、“运行耗时”等）
            if (e.PropertyName == "IsSelected" || e.PropertyName == "LastRunTime")
                return;

            Version++;
        }
        public void AddStepWithAutoName(StepModel newStep)
        {
            if (newStep == null) return;
            if (string.IsNullOrWhiteSpace(newStep.PluginName)) newStep.PluginName = "未知工具";

            // 1. 获取当前流程中所有的步骤名称，放到一个 HashSet 中查找速度最快
            var existingNames = new HashSet<string>(Steps.Select(s => s.StepName));

            // 2. 初始化后缀 ID
            int nextId = 1;
            string targetName = $"{newStep.PluginName}_{nextId}";

            // 3. 循环探测：只要名字被占用了，ID 就一直 +1，直到找到一个空缺的名字
            while (existingNames.Contains(targetName))
            {
                nextId++;
                targetName = $"{newStep.PluginName}_{nextId}";
            }

            // 4. 赋予安全的名称，并加入集合
            newStep.StepName = targetName;
            Steps.Add(newStep);
        }
    }
}
