using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
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

        public abstract void Dispose();

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
        public abstract void Initialize();

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
        /// </summary>
        public void ApplyConfigValues(IStepConfigData stepData)
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
                    stepData.InputValues.Remove(input.Name);
                    continue;
                }
                stepData.InputValues[input.Name] = input.Value;
            }
            //需要保存的记忆属性
            foreach (var cp in _configProps)
            {
                stepData.InputValues[cp.Name] = cp.Get();
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
                    // 注意这里用 TryAdd，防止属性和动态添加的重名
                    if (inputPort != null) _inputs.TryAdd(inputPort.Name, inputPort);
                }
                else if (typeof(IOutputPort).IsAssignableFrom(prop.PropertyType))
                {
                    var outputPort = (IOutputPort)prop.GetValue(this);
                    if (outputPort != null) _outputs.TryAdd(outputPort.Name, outputPort);
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
    }
}
