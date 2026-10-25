using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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

            // 退订输入端口对上游 LinkedSource 的 ValueChanged 订阅。
            // 缺了这一步：上游 OutputPort 若被长生命周期根（如跨 session 的
            // RuntimeVariableProxyPort）持有，事件委托会反向钉住本插件实例，
            // 构成逐轮编译累积的托管泄漏（InputPort<T>.Dispose 内做幂等退订）。
            foreach (var port in _inputs.Values)
            {
                if (port is IDisposable d)
                {
                    try { d.Dispose(); }
                    catch { /* 退订失败不应打断整体资源回收 */ }
                }
            }
        }

        /// <summary>
        /// 计算时间 变量输入映射  变量输出映射  要可以兼容到动态注册
        /// 契约：默认成功、显式失败——进入 RunAlgorithm 前预置 Success=true 并清空 ErrorMessage；
        /// 业务失败路径用 Fail("原因") 一行表达（或直接抛异常，由本方法统一转成失败——见 RethrowOnException）。
        /// 这里把结果传播给引擎层（CompiledPluginNode 据此标记状态并写日志）。
        /// </summary>
        public bool Execute(IExecutionContext context)
        {
            // 预置"默认成功"：纯计算类插件无需每次写 Success；
            // 在 RunAlgorithm 开头自行清 false 的插件（采集/延时等）会覆盖此初值，行为不变
            Success.Value = true;
            ErrorMessage.Value = string.Empty;

            // 轮首回收上一轮输出的非托管值（HImage 等），插件不再需要手写 DisposeOldOutputs
            if (AutoDisposeRoundOutputs)
                DisposePreviousRoundOutputs();

            try
            {
                RunAlgorithm(context);
            }
            catch (OperationCanceledException) when (context?.CancellationToken.IsCancellationRequested == true)
            {
                // 取消不是失败（A9）：急停/停止请求让插件从 RunAlgorithm 里脱出来，与"算子算错了"
                // 是两件完全不同的事。这里**不写 Success=false、不写 ErrorMessage**，原样上抛，
                // 由节点层（CompiledPluginNode）按"令牌已取消"记成 Skipped —— 界面上一行灰，
                // 不是一行红；流程收尾也不会多出一条"执行失败"。
                // 判据带 when 令牌已取消：插件自己抛的 OCE（内部超时之类）仍按普通异常转失败。
                throw;
            }
            catch (Exception ex)
            {
                // 未捕获异常自动落到失败契约： ErrorMessage 有原因、日志有记录、
                // 归因仍在本节点——插件开发者不写 try/catch 也守得住约定
                // ErrorMessage 保持短文案（界面/输出端口要的是"一句话原因"），
                // 完整堆栈走日志：现场最常见的是 NullReferenceException，只留 Message 等于没有线索。
                Fail($"{ex.GetType().Name}: {ex.Message}");
                context?.Logger?.Error(ex);
                if (RethrowOnException)
                    throw; // 策略声明型例外：信息已落 ErrorMessage，仍交引擎中断当轮
            }
            return Success.Value is true;
        }

        /// <summary>
        /// 异常策略（默认 false = 异常转失败、流程继续）。
        /// 个别"出错后绝不能带着脏状态继续跑"的插件（如安全互锁类）可重写为 true：
        /// 异常信息照样写入 ErrorMessage，但继续上抛让引擎中断当轮。
        /// </summary>
        protected virtual bool RethrowOnException => false;

        /// <summary>
        /// 是否轮首自动回收输出端口的非托管旧值（默认 true）。
        /// 回收时机 = RunAlgorithm 之前，与插件手写的"开轮 Dispose 上一轮输出"完全等价；
        /// 与上游链接当前值同引用的值（透传场景）自动跳过，不会误杀上游图像。
        /// 确有特殊生命周期需求的插件可重写为 false 并自行管理。
        /// </summary>
        protected virtual bool AutoDisposeRoundOutputs => true;

        /// <summary>
        /// 失败契约一行表达：置 Success=false + 写 ErrorMessage。
        /// 用法：if (img == null) { Fail("输入图像为空"); return; }
        /// </summary>
        protected void Fail(string message)
        {
            Success.Value = false;
            ErrorMessage.Value = message;
        }

        /// <summary>
        /// 释放上一轮输出端口承载的非托管值并置空（置空防下轮重复释放）。
        /// Success/ErrorMessage（bool/string）天然不是 IDisposable，不受影响。
        /// </summary>
        private void DisposePreviousRoundOutputs()
        {
            EnsurePortsDiscovered();
            foreach (var port in _outputs.Values)
            {
                if (port.Value is not IDisposable old)
                    continue;

                // 透传防御：本端口当前值若与某条上游链接的当前值是同一引用，
                // 说明"我输出的其实是上游的图"——释放权归生产者，跳过
                if (IsUpstreamLinkedValue(old))
                    continue;

                try { old.Dispose(); }
                catch { /* 释放失败不打断整体回收 */ }
                port.Value = null;
            }
        }

        /// <summary>
        /// 判断值是否与某输入端口的上游链接当前值同引用（透传场景识别，端口数量级为个位，线性扫描即可）
        /// </summary>
        private bool IsUpstreamLinkedValue(object value)
        {
            foreach (var input in _inputs.Values)
            {
                if (input.LinkedSource != null && ReferenceEquals(input.LinkedSource.Value, value))
                    return true;
            }
            return false;
        }

        /// <summary>最近一次执行的错误信息（业务失败时由 RunAlgorithm 写入 ErrorMessage）。</summary>
        public string LastError => ErrorMessage.Value as string ?? string.Empty;
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

        /// <summary>
        /// 与 <see cref="_configProps"/> 一一对应的只读描述（面板渲染用）。
        /// 同样懒构建、与属性缓存同生共死。
        /// </summary>
        private List<StepConfigPropertyDescriptor> _configPropDescriptors;

        private void EnsureConfigPropsDiscovered()
        {
            if (_configProps != null) return;

            _configProps = new List<ConfigProp>();
            _configPropDescriptors = new List<StepConfigPropertyDescriptor>();

            // 按 MetadataToken 排序 = 按声明顺序（界面上的行序必须稳定，
            // 且不能随反射实现/运行时版本漂移）。
            var properties = GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(p => p.MetadataToken);

            foreach (var prop in properties)
            {
                if (prop.GetCustomAttribute<StepConfigAttribute>() == null) continue;
                if (typeof(IInputPort).IsAssignableFrom(prop.PropertyType)) continue;
                if (typeof(IOutputPort).IsAssignableFrom(prop.PropertyType)) continue;

                var captured = prop;

                // 显示名/说明读属性上的 [Display]：插件作者已经用它写中文名，
                // 面板不必再猜（没标就回退属性名——绝不回退成空白）。
                // 资源型 [Display]（ResourceType）解析失败时回退属性名：一条元数据坏掉
                // 不该让整个参数面板打不开。
                var display = captured.GetCustomAttribute<DisplayAttribute>();
                string? displayName = null;
                var description = string.Empty;
                try
                {
                    displayName = display?.GetName();
                    description = display?.GetDescription() ?? string.Empty;
                }
                catch
                {
                    // 回退：displayName 保持 null → 描述为空
                }

                _configProps.Add(new ConfigProp
                {
                    Name = prop.Name,
                    Type = prop.PropertyType,
                    Get = () => captured.GetValue(this),
                    Set = v => captured.SetValue(this, v),
                });

                _configPropDescriptors.Add(new StepConfigPropertyDescriptor(
                    prop.Name,
                    displayName,
                    description,
                    prop.PropertyType,
                    () => captured.GetValue(this),
                    v => captured.SetValue(this, v),
                    prop.GetCustomAttribute<StepConfigOptionsAttribute>()?.Kind));
            }
        }

        /// <summary>
        /// 取 [StepConfig] 配置属性的只读快照（按声明顺序），供没有自定义视图的插件
        /// 在通用参数面板（AutoPortConfigView）里渲染。
        ///
        /// 返回**每次一份新数组**：调用方拿不到内部缓存，也就无法通过它改动插件的配置面；
        /// 键名仍是属性名（与 <see cref="ApplyConfigValues"/> / <see cref="OnConfirm"/> 同一口径）。
        /// </summary>
        public IReadOnlyList<StepConfigPropertyDescriptor> GetConfigProperties()
        {
            EnsureConfigPropsDiscovered();
            return _configPropDescriptors.ToArray();
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
            // 端口名是反射按属性名补的（`new InputPort<T>()` 不写名字），而连线键
            //（StepModel.LinkedSources / LinkableValueEditor 的 🔗 / 绑定弹窗的 TargetPortName）
            // 一律按 Name 取。必须在这里发现一次：**新建步骤的 InputValues 是空的**，
            // ApplyConfigValues 的循环一次都不跑，端口名会一直停在 null ——
            // 配置视图里的 🔗 于是发出空 InputPortName，宿主退回"完整绑定弹窗"，
            // 绑定只写进模型、插件视图收不到 OnBound 回调（真机：选完变量点确认，
            // 图像路径那行仍显示"需链接变量"，要关掉配置窗重开才回显）。
            EnsurePortsDiscovered();
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
