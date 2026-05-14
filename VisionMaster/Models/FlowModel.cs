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
    /// 表示一个完整的视觉检测流程
    /// </summary>
    public class FlowModel : BindableBase
    {
        /// <summary>
        /// 流程唯一标识
        /// </summary>
        public string FlowID { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 流程名称
        /// </summary>
        public string FlowName
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 流程描述
        /// </summary>
        public string Description
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 版本号（自动递增）
        /// </summary>
        public int Version
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 触发模式（单次/连续/外部/定时）
        /// </summary>
        public FlowTriggerMode TriggerMode { get; set; } = FlowTriggerMode.Continuous;

        /// <summary>
        /// 定时触发间隔（毫秒）
        /// </summary>
        public int TimerIntervalMs { get; set; } = 1000;

        /// <summary>
        /// 步骤列表
        /// </summary>
        public ObservableCollection<StepModel> Steps { get; set; } = new();

        /// <summary>
        /// 初始化流程模型
        /// </summary>
        public FlowModel()
        {
            Steps.CollectionChanged += (s, e) =>
            {
                Version++;

                if (e.NewItems != null)
                {
                    foreach (StepModel item in e.NewItems)
                        item.PropertyChanged += OnStepPropertyChanged;
                }

                if (e.OldItems != null)
                {
                    foreach (StepModel item in e.OldItems)
                        item.PropertyChanged -= OnStepPropertyChanged;
                }
            };
        }

        /// <summary>
        /// 步骤属性变更处理
        /// 更新版本号（排除不影响逻辑的属性）
        /// </summary>
        private void OnStepPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsSelected" || e.PropertyName == "LastRunTime")
                return;

            Version++;
        }

        /// <summary>
        /// 添加步骤并自动生成唯一名称
        /// </summary>
        public void AddStepWithAutoName(StepModel newStep)
        {
            if (newStep == null) return;
            if (string.IsNullOrWhiteSpace(newStep.PluginName)) newStep.PluginName = "未知工具";

            var existingNames = new HashSet<string>(Steps.Select(s => s.StepName));

            int nextId = 1;
            string targetName = $"{newStep.PluginName}_{nextId}";

            while (existingNames.Contains(targetName))
            {
                nextId++;
                targetName = $"{newStep.PluginName}_{nextId}";
            }

            newStep.StepName = targetName;
            Steps.Add(newStep);
        }
    }
}
