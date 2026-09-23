using GongSolutions.Wpf.DragDrop;
using Core.Interfaces;
using System;
using System.Diagnostics;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
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
                        GlobalEventBus.Publish(new StepRenamedMessage(StepID, oldName, value));
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

        /// <summary>
        /// 步骤执行状态。纯运行时，不改变流程语义 → 不递增 Version
        /// </summary>
        [RuntimeState]
        public StepState State
        {
            get => field;
            set => SetRuntimeState(ref field, value);
        }

        /// <summary>
        /// 是否为当前运行焦点
        /// </summary>
        [RuntimeState]
        [JsonIgnore]
        public bool IsRunningFocus
        {
            get => field;
            set => SetRuntimeState(ref field, value);
        }

        /// <summary>
        /// 最后运行起始的高精度时间戳（Stopwatch.GetTimestamp 的原始读数），null 表示未开始计时。
        /// 不用 DateTime：墙钟受系统校时影响，且 Tick 粒度粗，测不出亚毫秒耗时。
        /// </summary>
        [RuntimeState]
        [JsonIgnore]
        public long? LastRunStartTimestamp
        {
            get => field;
            set => SetRuntimeState(ref field, value);
        }

        /// <summary>
        /// 最后运行耗时（毫秒，亚毫秒精度）
        /// 跨线程读写（引擎线程写 / UI 线程定时器读）：double 为 8 字节，
        /// 依赖 x64 进程下对齐 64 位写入的原子性（CLR 实现保证）。若将来出现 32 位宿主，
        /// 必须改用 Interlocked/Volatile 或加锁，否则存在撕裂读风险。
        /// </summary>
        [RuntimeState]
        [JsonIgnore]
        public double LastRunTimeMs
        {
            get => field;
            set => SetRuntimeState(ref field, value);
        }

        /// <summary>
        /// 当前运行耗时（毫秒，运行中由 UI 定时器实时刷新）
        /// 原子性前提同 LastRunTimeMs：依赖 x64 进程。
        /// </summary>
        [RuntimeState]
        [JsonIgnore]
        public double CurrentRunTimeMs
        {
            get => field;
            set => SetRuntimeState(ref field, value);
        }

        /// <summary>
        /// 批量作用域的嵌套深度。大于 0 期间，运行状态属性只改值、不发 PropertyChanged，
        /// 变更的属性名先记在 _pendingRuntimeNotify 账上，最外层作用域结束时统一补发。
        /// </summary>
        private int _runtimeNotifyDepth;

        /// <summary>批量作用域内被改过的属性名（去重），供作用域结束时按名字补发通知</summary>
        private List<string> _pendingRuntimeNotify;

        /// <summary>
        /// 运行状态属性的统一写入口：值真变了才发通知；批量模式下只记名字不发。
        ///
        /// 为什么不直接用 SetProperty（B2）：一个步骤跑一轮要动 4~6 个运行状态属性
        /// （置 Running、开始计时、结束计时、置 Success），每个都发一条 PropertyChanged；
        /// 而这些通知来自引擎线程，WPF 得逐条编组回 UI 线程才能刷新绑定。
        /// 100 步的流程按 10ms 一拍循环，就是每秒上万次跨线程通知 ——
        /// UI 什么算法都没看，光处理通知就排满了队，表现为画布发木、高亮滞后。
        /// 加上判等闸门后，"没变的属性"一条通知也不发（例如反复复位一个本来就 Idle 的步骤）。
        /// </summary>
        private void SetRuntimeState<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return;

            storage = value;

            if (_runtimeNotifyDepth == 0)
            {
                RaisePropertyChanged(propertyName);
                return;
            }

            // 批量模式：记名待发。同一属性在一个作用域内被写两次只记一次，
            // 于是"改四次 → 发四条"能压成"发一条"，UI 只刷一遍。
            var pending = _pendingRuntimeNotify;
            if (propertyName != null && pending != null && !pending.Contains(propertyName))
                pending.Add(propertyName);
        }

        /// <summary>
        /// 开一段"批量改运行状态"的作用域：作用域内的赋值憋着不发，
        /// Dispose 时把真正变过的属性名逐条补发。
        ///
        /// 【为什么不能图省事用 RaisePropertyChanged("") 一把全刷】
        /// 空串在 WPF 里确实是"本对象所有属性都可能变了"的标准约定，UI 侧没问题；
        /// 问题是 FlowModel 也订阅了步骤的 PropertyChanged，它靠**属性名**去查
        /// [RuntimeState] 名单，名字在名单里才不递增 Version。
        /// 空串显然不在名单里 → 被判定成语义变更 → Version++ →
        /// 每轮运行前都被迫全量重编译，正是这套排除名单当初要修掉的坑。
        /// 所以补发必须用真实属性名，宁可多发几条也不能丢掉名字。
        /// </summary>
        private IDisposable BatchRuntimeNotify()
        {
            if (_runtimeNotifyDepth++ == 0)
                _pendingRuntimeNotify = new List<string>();

            return new RuntimeNotifyScope(this);
        }

        private sealed class RuntimeNotifyScope : IDisposable
        {
            private StepModel _owner;

            public RuntimeNotifyScope(StepModel owner) => _owner = owner;

            public void Dispose()
            {
                var owner = _owner;
                if (owner == null) return;
                _owner = null;

                // 还有外层作用域没结束，名字留在账上，等最外层统一发
                if (--owner._runtimeNotifyDepth > 0) return;

                var pending = owner._pendingRuntimeNotify;
                owner._pendingRuntimeNotify = null;
                if (pending == null) return;

                for (int i = 0; i < pending.Count; i++)
                    owner.RaisePropertyChanged(pending[i]);
            }
        }

        /// <summary>开始计时：记录 Stopwatch 起始读数并清零实时耗时</summary>
        public void BeginTiming()
        {
            // 必须写成 using(...)：光秃秃的 using Xxx() 会被编译器当成 using 别名声明
            using (BatchRuntimeNotify())
            {
                LastRunStartTimestamp = Stopwatch.GetTimestamp();
                CurrentRunTimeMs = 0;
            }
        }

        /// <summary>
        /// 结束计时：把区间耗时冻结到 LastRunTimeMs 并返回；未开始计时则返回 0
        /// </summary>
        public double EndTiming()
        {
            if (!LastRunStartTimestamp.HasValue)
                return 0;

            double elapsedMs = Stopwatch.GetElapsedTime(LastRunStartTimestamp.Value).TotalMilliseconds;

            using (BatchRuntimeNotify())
            {
                LastRunTimeMs = elapsedMs;
                CurrentRunTimeMs = elapsedMs;
            }

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
            // 先自检：本来就已经干净就直接返回。
            // 调度层每轮都会对全部步骤调一次 ResetState，而稳态下大量步骤本来就停在 Idle，
            // 少了这道闸门，"复位"就是在给没变的东西反复发通知。
            if (State == StepState.Idle
                && !IsRunningFocus
                && CurrentRunTimeMs == 0
                && LastRunStartTimestamp == null)
                return;

            using (BatchRuntimeNotify())
            {
                State = StepState.Idle;
                IsRunningFocus = false;
                CurrentRunTimeMs = 0;
                LastRunStartTimestamp = null;
            }
        }

    }
    public class ActionStep : StepModel
    {
        public ActionStep(string icon, string pluginName, string pluginTypeName, string stepName = null) : base(icon, pluginName, pluginTypeName, stepName)
        {

        }
    }



}
