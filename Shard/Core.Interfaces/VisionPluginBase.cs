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
    public abstract class VisionPluginBase: IVisionPlugin
    {
        public string PluginID { get; set; } = Guid.NewGuid().ToString("N");

        public string InstanceName { get; set; } = "未赋值";


        private bool _isPortsDiscovered = false;

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
