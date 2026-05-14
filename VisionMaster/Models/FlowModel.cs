using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 流程调用类型枚举
    /// </summary>
    public enum FlowInvokeType
    {
        /// <summary>手动调用</summary>
        Manual = 0,
        /// <summary>定时调用</summary>
        Timer = 1,
        /// <summary>变量触发调用</summary>
        Variable = 2,
        /// <summary>作为子程序调用</summary>
        Subroutine = 3
    }

    /// <summary>
    /// 流程运行时状态
    /// </summary>
    public enum FlowRunState
    {
        /// <summary>停止</summary>
        Stopped = 0,
        /// <summary>运行中</summary>
        Running = 1,
        /// <summary>暂停</summary>
        Paused = 2
    }

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
        /// 流程是否启用
        /// </summary>
        public bool IsEnabled
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 调用类型
        /// </summary>
        public FlowInvokeType InvokeType
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 触发变量名称（当InvokeType为Variable时使用）
        /// </summary>
        public string TriggerVariable
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 定时调用间隔（毫秒）
        /// </summary>
        public int TimerIntervalMs
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 步序内容是否加密
        /// </summary>
        public bool StepsEncrypted
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 加密密钥（加密后的密钥由系统生成）
        /// </summary>
        public string? EncryptedKey { get; set; }

        /// <summary>
        /// 触发模式（单次/连续/外部/定时）
        /// </summary>
        public FlowTriggerMode TriggerMode { get; set; } = FlowTriggerMode.Continuous;

        /// <summary>
        /// 运行时状态
        /// </summary>
        [JsonIgnore]
        public FlowRunState RunState
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 当前运行步骤索引
        /// </summary>
        [JsonIgnore]
        public int CurrentStepIndex
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 开始运行时间
        /// </summary>
        [JsonIgnore]
        public DateTime? StartRunTime
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 当前运行时间（毫秒）
        /// </summary>
        [JsonIgnore]
        public long CurrentRunTimeMs
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 步骤列表
        /// </summary>
        public ObservableCollection<StepModel> Steps { get; set; } = new();

        /// <summary>
        /// 初始化流程模型
        /// </summary>
        [JsonConstructor]
        public FlowModel()
        {
            // 反序列化时不订阅事件
            if (Steps != null)
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
