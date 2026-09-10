using GongSolutions.Wpf.DragDrop;
using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Media;
using Core.Events;
using VisionMaster.EventModel;

namespace VisionMaster.Models
{
    [JsonDerivedType(typeof(ActionStep), typeDiscriminator: "ActionStep")]
    [JsonDerivedType(typeof(ConditionStep), typeDiscriminator: "ConditionStep")]
    [JsonDerivedType(typeof(BreakStep), typeDiscriminator: "BreakStep")]
    [JsonDerivedType(typeof(ContinueStep), typeDiscriminator: "ContinueStep")]
    [JsonDerivedType(typeof(ReturnStep), typeDiscriminator: "ReturnStep")]
    [JsonDerivedType(typeof(ForStep), typeDiscriminator: "ForStep")]
    public abstract class StepModel:BindableBase, IStepConfigData
    {
        public Guid StepID { get; set; } = Guid.NewGuid();

        // IStepConfigData 显式实现
        Guid IStepConfigData.StepId => StepID;

        public string Icon { get; init; }
        public string PluginName { get; set; }

        public string PluginTypeName { get; init; }


        public string StepName
        {
            get => field;
            set
            {
                string oldName = field;
                if (SetProperty(ref field, value))
                {
                    if (!string.IsNullOrEmpty(oldName) && oldName != value)
                    {
                        GlobalEventBus.Publish(new StepRenamedMessage(oldName, value));
                    }
                }
            }
        }
        public string Description
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public int SortId { get; set; }

        public bool IsDisEnable
        {
            get => field;
            set => SetProperty(ref field, value);
        } 

        public StepState State
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 是否为当前运行焦点
        /// </summary>
        [JsonIgnore]
        public bool IsRunningFocus
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 最后运行开始时间
        /// </summary>
        [JsonIgnore]
        public DateTime? LastRunStartTime
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 最后运行时间（毫秒）
        /// </summary>
        [JsonIgnore]
        public long LastRunTimeMs
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 当前运行时间（毫秒，实时更新）
        /// </summary>
        [JsonIgnore]
        public long CurrentRunTimeMs
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public Dictionary<string, object> InputValues { get; set; } = new Dictionary<string, object>();

        public Dictionary<string, LinkReference> LinkedSources { get; set; } = new();

        /// <summary>
        /// 动态输出端口定义快照（名字+类型，存盘）
        /// 供 IDynamicOutputProvider 类插件在配置实例重建端口后同步回写，
        /// 编译器和 LinkableValueEditor 从此读取可用端口表（不依赖配置实例存活）
        /// 格式约定：[{Name="Crop_ROI_1", DataTypeName="HalconDotNet.HImage"}, ...]
        /// </summary>
        public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();

        // 统一写路径：所有对 InputValues 的写操作必须走这里，
        // 通过 PropertyChanged 通知 FlowModel.OnStepPropertyChanged 递增流程版本，
        // 从而让运行前的版本检查触发重新编译（否则改参数不会同步到已编译的运行实例）

        /// <summary>
        /// 写入输入参数（确认配置/落盘专用），并触发版本通知
        /// </summary>
        public void SetInputValue(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;
            InputValues[key] = value;
            RaisePropertyChanged(nameof(InputValues));
        }

        /// <summary>
        /// 移除输入参数（清除链接端口的固定值等），并触发版本通知
        /// </summary>
        public void RemoveInputValue(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (InputValues.Remove(key))
            {
                RaisePropertyChanged(nameof(InputValues));
            }
        }

        // IStepConfigData 绑定 API：供插件自定义配置视图读写变量链接
        public bool IsLinked(string inputPortName)
            => !string.IsNullOrEmpty(inputPortName) && LinkedSources.ContainsKey(inputPortName);

        public string GetLinkedAddress(string inputPortName)
            => LinkedSources.TryGetValue(inputPortName ?? "", out var link)
                ? link.DisplayAddress
                : null;

        public LinkReference GetLink(string inputPortName)
            => LinkedSources.TryGetValue(inputPortName ?? "", out var link)
                ? link
                : null;

        public void SetLink(string inputPortName, LinkReference link)
        {
            if (string.IsNullOrEmpty(inputPortName) || link == null)
                return;
            LinkedSources[inputPortName] = link;
            RaisePropertyChanged(nameof(LinkedSources));
        }

        public void RemoveLink(string inputPortName)
        {
            if (!string.IsNullOrEmpty(inputPortName))
            {
                if (LinkedSources.Remove(inputPortName))
                {
                    RaisePropertyChanged(nameof(LinkedSources));
                }
            }
        }

        public StepModel(string icon,string pluginName, string pluginTypeName, string stepName =null)
        {
            Icon=icon;
            PluginName=pluginName;
            this.PluginTypeName = pluginTypeName;
            StepName = stepName == null ? pluginName: stepName;
            Description = pluginName;
        }

        /// <summary>
        /// 复位步序状态到Idle
        /// </summary>
        public void ResetState()
        {
            State = StepState.Idle;
            IsRunningFocus = false;
            CurrentRunTimeMs = 0;
            LastRunStartTime = null;
        }

    }
    public class ActionStep : StepModel
    {
        public ActionStep(string icon, string pluginName, string pluginTypeName, string stepName = null) : base(icon, pluginName, pluginTypeName, stepName)
        {

        }
    }



}
