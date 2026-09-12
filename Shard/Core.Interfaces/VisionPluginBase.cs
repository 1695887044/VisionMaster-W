using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    /// <summary>
    /// 插件基类：端口发现 + 执行 + 默认配置生命周期
    /// 设计原则：端口即数据成员——参与流程数据流的参数一律声明为 InputPort，
    /// 配置界面直接绑定端口的 TypedValue，不再需要草稿属性镜像
    /// </summary>
    public abstract class VisionPluginBase: ObservableObject, IVisionPlugin, IPluginConfigView
    {
        public string PluginID { get; set; } = Guid.NewGuid().ToString("N");

        public string InstanceName { get; set; } = "未赋值";


        private bool _isPortsDiscovered = false;

        public object? PluginConfig { get; set; } = null;

        /// <summary>
        /// 采集是否成功
        /// </summary>
        public OutputPort<bool> Success { get; } = new(
            "Success",
            "成功"
        );

        /// <summary>
        /// 错误信息
        /// </summary>
        public OutputPort<string> ErrorMessage { get; } = new(
            "ErrorMessage",
            "错误信息"
        );


        private readonly Dictionary<string, IInputPort> _inputs = new();
        private readonly Dictionary<string, IOutputPort> _outputs = new();
        public IReadOnlyDictionary<string, IInputPort> Inputs
        {
            get { EnsurePortsDiscovered(); return _inputs; }
        }

        public IReadOnlyDictionary<string, IOutputPort> Outputs
        {
            get { EnsurePortsDiscovered(); return _outputs; }
        }

        private IStepConfigData _stepData;
        /// <summary>
        /// 当前配置会话的步骤数据
        /// 供配置视图中的 LinkableValueEditor 等控件读写变量链接
        /// </summary>
        public IStepConfigData StepData
        {
            get => _stepData;
            private set => SetProperty(ref _stepData, value);
        }

        /// <summary>
        /// 释放插件持有的非托管资源（HImage、句柄等）
        /// 默认实现会释放所有输出端口承载的值（如 Halcon HImage），避免非托管内存泄漏；
        /// 子类若持有额外资源应重写并在末尾调用 base.Dispose()
        /// </summary>
        public virtual void Dispose()
        {
            // 释放输出端口承载的非托管资源（试运行/正式运行产出的 HImage 等）
            EnsurePortsDiscovered();
            foreach (var port in _outputs.Values)
            {
                if (port.Value is IDisposable disposable)
                {
                    try { disposable.Dispose(); }
                    catch { /* 释放失败不应打断整体资源回收 */ }
                }
            }
        }

        /// <summary>
        /// 计算时间 变量输入映射  变量输出映射  要可以兼容到动态注册
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public bool Execute(IExecutionContext context)
        {
            RunAlgorithm(context);
            return true;
        }
        public abstract void RunAlgorithm(IExecutionContext context);

        /// <summary>
        /// 插件级初始化（如加载模型、连接硬件等一次性准备）
        /// 仅当插件有初始化需求时才需要重写；默认空实现
        /// </summary>
        public virtual void Initialize() { }

        #region 默认配置生命周期（端口 + [StepConfig] 配置属性 ⇄ InputValues，键名 = 名称，无需映射）

        /// <summary>
        /// [StepConfig] 配置属性缓存（Name / Type / Get / Set）
        /// </summary>
        private sealed class ConfigProp
        {
            public string Name;
            public Type Type;
            public Func<object> Get;
            public Action<object> Set;
        }

        private List<ConfigProp> _configProps;

        private void EnsureConfigPropsDiscovered()
        {
            if (_configProps != null) return;

            _configProps = new List<ConfigProp>();
            foreach (var prop in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetCustomAttribute<StepConfigAttribute>() == null) continue;
                if (typeof(IInputPort).IsAssignableFrom(prop.PropertyType)) continue;
                if (typeof(IOutputPort).IsAssignableFrom(prop.PropertyType)) continue;

                var captured = prop;
                _configProps.Add(new ConfigProp
                {
                    Name = prop.Name,
                    Type = prop.PropertyType,
                    Get = () => captured.GetValue(this),
                    Set = v => captured.SetValue(this, v),
                });
            }
        }

        /// <summary>
        /// 统一灌值入口（FlowCompiler 正式运行 / PluginTestRunner 试运行 / 配置初始化共用）：
        /// InputValues → 输入端口（链接端口跳过）+ [StepConfig] 配置属性
        /// virtual：需要动态输入端口的插件可重写，在 base 之后、LinkPorts 之前重建端口
        /// </summary>
        public virtual void ApplyConfigValues(IStepConfigData stepData)
        {
            EnsureConfigPropsDiscovered();
            if (stepData?.InputValues == null) return;

            foreach (var kvp in stepData.InputValues)
            {
                if (Inputs.TryGetValue(kvp.Key, out var port))
                {
                    if (stepData.IsLinked(kvp.Key)) continue;
                    port.Value = kvp.Value;
                    continue;
                }

                var cp = _configProps.FirstOrDefault(c => c.Name == kvp.Key);
                if (cp == null) continue;
                try
                {
                    cp.Set(ValueConverter.Convert(kvp.Value, cp.Type));
                }
                catch
                {
                    // 宽容处理：类型不匹配的旧数据跳过，保留属性默认值
                }
            }
        }

        /// <summary>
        /// 默认配置初始化：保存 StepData 并把 InputValues 灌入端口与配置属性（链接端口跳过）
        /// 插件如需恢复私有配置态（如预览图），override 并调用 base
        /// </summary>
        public virtual void Initialize(IStepConfigData stepData)
        {
            StepData = stepData;
            ApplyConfigValues(stepData);
        }

        /// <summary>
        /// 默认确认：把输入端口与配置属性的值写回 InputValues
        /// （链接端口移除固定值键，避免残留脏数据，运行时以链接为准）
        /// </summary>
        public virtual void OnConfirm(IStepConfigData stepData)
        {
            EnsureConfigPropsDiscovered();
            if (stepData?.InputValues == null) return;
            //存放没有链接的端口属性
            foreach (var input in Inputs.Values)
            {
                if (stepData.IsLinked(input.Name))
                {
                    stepData.RemoveInputValue(input.Name);
                    continue;
                }
                stepData.SetInputValue(input.Name, input.Value);
            }

            foreach (var cp in _configProps)
            {
                stepData.SetInputValue(cp.Name, SnapshotConfigValue(cp.Get()));
            }
        }

        /// <summary>
        /// 统一的快照序列化配置：集中管理，避免每次调用重复 new JsonSerializerSettings()（重复构造且易遗漏配置），
        /// 后续如需加入类型白名单/容错策略，只需在此一处修改
        /// </summary>
        private static readonly JsonSerializerSettings SnapshotSettings = new JsonSerializerSettings();

        /// <summary>
        /// 配置属性存快照（JSON 往返拷贝）：复杂配置（如 List&lt;RoiItem&gt;）存进 InputValues 后
        /// 不再与界面上的活对象共享引用——确认后界面继续修改不会污染已确认的数据
        /// </summary>
        private static object SnapshotConfigValue(object value)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (type.IsPrimitive || value is string || value is Enum || value is decimal) return value;
            try
            {
                var json = JsonConvert.SerializeObject(value, type, SnapshotSettings);
                return JsonConvert.DeserializeObject(json, type, SnapshotSettings);
            }
            catch
            {
                return value; // 不可序列化的类型退回引用（宽容处理）
            }
        }

        /// <summary>
        /// 默认取消：试运行由主程序基建执行（读已保存的 InputValues，不落盘），无需还原
        /// </summary>
        public virtual void OnCancel()
        {
        }

        #endregion

        private void EnsurePortsDiscovered()
        {
            if (_isPortsDiscovered) return;

            var properties = this.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (typeof(IInputPort).IsAssignableFrom(prop.PropertyType))
                {
                    var inputPort = (IInputPort)prop.GetValue(this);
                    if (inputPort != null)
                    {
                        // 端口声明时可以不写名称：自动以属性名命名（new InputPort<T>() 的精简写法）
                        if (string.IsNullOrEmpty(inputPort.Name) && inputPort is IPortNameSettable settable)
                            settable.Name = prop.Name;

                        // 注意这里用 TryAdd，防止属性和动态添加的重名
                        _inputs.TryAdd(inputPort.Name, inputPort);
                    }
                }
                else if (typeof(IOutputPort).IsAssignableFrom(prop.PropertyType))
                {
                    var outputPort = (IOutputPort)prop.GetValue(this);
                    if (outputPort != null)
                    {
                        if (string.IsNullOrEmpty(outputPort.Name) && outputPort is IPortNameSettable settable)
                            settable.Name = prop.Name;

                        _outputs.TryAdd(outputPort.Name, outputPort);
                    }
                }
            }
            _isPortsDiscovered = true;
        }
        public void AddDynamicInput(IInputPort port)
        {
            EnsurePortsDiscovered(); // 先让反射把坑占好，再加动态的
            if (!_inputs.ContainsKey(port.Name))
            {
                _inputs.Add(port.Name, port);
            }
        }
        public void RemoveDynamicInput(string portName)
        {
            EnsurePortsDiscovered();
            if (_inputs.ContainsKey(portName))
            {
                _inputs.Remove(portName);
            }
        }

        /// <summary>
        /// 添加动态输出端口（如 Crop_ROI_1 等，由 IDynamicOutputProvider 类插件在配置变化时调用）
        /// </summary>
        public void AddDynamicOutput(IOutputPort port)
        {
            EnsurePortsDiscovered();
            if (!_outputs.ContainsKey(port.Name))
                _outputs.Add(port.Name, port);
        }

        /// <summary>
        /// 移除动态输出端口（ROI 删除时调用；固定端口不受影响）
        /// </summary>
        public void RemoveDynamicOutput(string portName)
        {
            EnsurePortsDiscovered();
            _outputs.Remove(portName);
        }

        /// <summary>
        /// 清空全部动态输出端口（重建集合前的清理；固定端口由反射重建，不受影响）
        /// 实现方式：保留反射发现的固定端口，移除其余
        /// </summary>
        public void ClearDynamicOutputs()
        {
            EnsurePortsDiscovered();
            // 固定端口 = 属性反射的端口；动态端口 = 运行时 Add 进来的
            // 用属性集合判断哪些是固定的
            var fixedNames = new HashSet<string>(
                GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => typeof(IOutputPort).IsAssignableFrom(p.PropertyType))
                    .Select(p => ((IOutputPort)p.GetValue(this))?.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
            );
            var dynamicKeys = _outputs.Keys.Where(k => !fixedNames.Contains(k)).ToList();
            foreach (var key in dynamicKeys)
                _outputs.Remove(key);
        }
    }

    /// <summary>
    /// 动态输出端口提供者接口（插件按需实现）
    /// 实现后框架在编译前会调用 RebuildDynamicOutputs() 重建端口集合
    /// </summary>
    public interface IDynamicOutputProvider
    {
        /// <summary>
        /// 按 StepModel.OutputPortNames 快照重建动态输出端口（编译/试运行前由框架调用）
        /// 插件实现此方法后，框架无需关心端口如何构造
        /// </summary>
        void RebuildDynamicOutputs();
    }
}
