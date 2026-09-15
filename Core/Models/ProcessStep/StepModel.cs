using GongSolutions.Wpf.DragDrop;
using Core.Interfaces;
using System;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using Core.Events;
using VisionMaster.EventModel;

namespace VisionMaster.Models
{
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
        /// 最后运行起始的高精度时间戳（Stopwatch.GetTimestamp 的原始读数），null 表示未开始计时。
        /// 不用 DateTime：墙钟受系统校时影响，且 Tick 粒度粗，测不出亚毫秒耗时。
        /// </summary>
        [JsonIgnore]
        public long? LastRunStartTimestamp
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 最后运行耗时（毫秒，亚毫秒精度）
        /// 跨线程读写（引擎线程写 / UI 线程定时器读）：double 为 8 字节，
        /// 依赖 x64 进程下对齐 64 位写入的原子性（CLR 实现保证）。若将来出现 32 位宿主，
        /// 必须改用 Interlocked/Volatile 或加锁，否则存在撕裂读风险。
        /// </summary>
        [JsonIgnore]
        public double LastRunTimeMs
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 当前运行耗时（毫秒，运行中由 UI 定时器实时刷新）
        /// 原子性前提同 LastRunTimeMs：依赖 x64 进程。
        /// </summary>
        [JsonIgnore]
        public double CurrentRunTimeMs
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>开始计时：记录 Stopwatch 起始读数并清零实时耗时</summary>
        public void BeginTiming()
        {
            LastRunStartTimestamp = Stopwatch.GetTimestamp();
            CurrentRunTimeMs = 0;
        }

        /// <summary>
        /// 结束计时：把区间耗时冻结到 LastRunTimeMs 并返回；未开始计时则返回 0
        /// </summary>
        public double EndTiming()
        {
            if (!LastRunStartTimestamp.HasValue)
                return 0;

            double elapsedMs = Stopwatch.GetElapsedTime(LastRunStartTimestamp.Value).TotalMilliseconds;
            LastRunTimeMs = elapsedMs;
            CurrentRunTimeMs = elapsedMs;
            return elapsedMs;
        }

        /// <summary>读取"此刻已耗时"（不冻结），供 UI 定时器刷新运行中的实时数字</summary>
        public double LiveElapsedMs()
        {
            if (!LastRunStartTimestamp.HasValue)
                return 0;

            return Stopwatch.GetElapsedTime(LastRunStartTimestamp.Value).TotalMilliseconds;
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
            LastRunStartTimestamp = null;
        }

    }
    public class ActionStep : StepModel
    {
        public ActionStep(string icon, string pluginName, string pluginTypeName, string stepName = null) : base(icon, pluginName, pluginTypeName, stepName)
        {

        }
    }



}
