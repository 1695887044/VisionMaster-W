using Core.Interfaces;
using VisionMaster.Communications;
using Prism.Mvvm;
using System.Windows;
using VisionMaster.Helpers;

using Newtonsoft.Json;

namespace VisionMaster.Models
{
    public class NetworkVariableModel : BindableBase, IVariable, IVariableDisplaySource
    {
        private string _dataTypeString;
        private Type _dataType;
        private object? _value;

        [JsonIgnore]
        private ICommunicationManager? CommunicationManager;

        /// <summary>
        /// 绑定通信管理器（建网络变量后必须调用，否则 Value 读写不会触达设备）。
        /// 创建方负责在方案加载/变量新建时注入，保证镜像值与设备值联动
        /// </summary>
        public void Bind(ICommunicationManager manager) => CommunicationManager = manager;

        /// <summary>
        /// 轮询镜像更新：由通信管理器的轮询链路调用，仅更新本地镜像值并触发通知，
        /// 不走 Value setter（避免回写设备造成读写自激）
        /// </summary>
        public void UpdateMirrorValue(object? newValue)
        {
            if (SetProperty(ref _value, newValue))
                _valueChanged?.Invoke(this, EventArgs.Empty);
        }

        public string Name { get; set; } = string.Empty;
        public VariableType VariableType => VariableType.Communication;
        public string? ConnectionName { get; set; }
        public DeviceAddressBase? AddressConfig { get; set; }
        public int PollIntervalMs { get; set; } = 500;

        public string DataTypeString
        {
            get => _dataTypeString ?? TypeCache.GetTypeKey(DataType);
            set
            {
                _dataTypeString = value;
                _dataType = TypeCache.GetType(value);
            }
        }

        [JsonIgnore]
        public Type DataType
        {
            get
            {
                if (_dataType == null && !string.IsNullOrEmpty(_dataTypeString))
                {
                    _dataType = TypeCache.GetType(_dataTypeString);
                }
                return _dataType ?? typeof(string);
            }
            set
            {
                _dataType = value;
                _dataTypeString = TypeCache.GetTypeKey(value);
            }
        }

        public string Description { get; set; } = string.Empty;
        public object? DefaultValue { get; set; }

        public object? Value
        {
            get => _value; // 轮询镜像值：设备同步由通信管理器轮询链路负责，getter 不再直读设备（避免绑定高频同步 IO）
            set
            {
                if (SetProperty(ref _value, value))
                {
                    WriteToDevice(value);
                }
            }
        }

        /// <summary>
        /// 显式写值到设备（工业写操作正门）：
        /// ① 无条件下发——不经过 Value setter，规避"镜像与目标值相同被 SetProperty 短路而实际没写"的陷阱
        ///   （设备侧可能被外部改回同值，"再写一次"是命令不是状态设置）；
        /// ② 返回真实结果——离线/未配置地址/写异常都给出明确原因，UI 必须按结果反馈，禁止无条件报成功。
        /// 写成功后同步镜像值（走 UpdateMirrorValue，不再二次下发）。
        /// </summary>
        public bool TryWriteToValue(object? value, out string? error)
        {
            error = null;

            if (AddressConfig == null)
            {
                error = $"变量 [{Name}] 未配置设备地址，无法写入";
                return false;
            }
            if (CommunicationManager == null)
            {
                error = $"变量 [{Name}] 未绑定通信管理器（连接可能已删除，请在变量管理中重新挂接）";
                return false;
            }
            if (string.IsNullOrWhiteSpace(ConnectionName))
            {
                error = $"变量 [{Name}] 缺少连接名，无法定位设备";
                return false;
            }

            // 离线预检：只读连接状态（拿裸连接读状态是允许的，做 I/O 不允许——I/O 必须走 Manager）
            var connection = CommunicationManager.GetConnection(ConnectionName);
            if (connection == null || !connection.IsConnected)
            {
                error = $"连接 [{ConnectionName}] 离线，变量 [{Name}] 未写入设备";
                return false;
            }

            try
            {
                var rawValue = AddressConfig.ConvertToRaw(value);
                if (rawValue == null)
                {
                    error = $"变量 [{Name}] 的值无法转换为设备原始值（检查数据类型与地址配置）";
                    return false;
                }
                // 经 Manager → 连接专属线程下发：与轮询、其他写命令在同一条线程上串行执行，
                // 不再出现"两个线程同时操作一个 socket"（旧实现直接拿裸连接写，绕过了 Worker）
                CommunicationManager.WriteVariable(ConnectionName, AddressConfig.Address, rawValue);
                UpdateMirrorValue(value); // 写确认成功 → 镜像同步并通知 UI
                return true;
            }
            catch (Exception ex)
            {
                error = $"变量 [{Name}] 写入失败：{ex.Message}";
                return false;
            }
        }

        [JsonIgnore]
        private EventHandler? _valueChanged;
        
        public event EventHandler ValueChanged
        {
            add => _valueChanged += value;
            remove => _valueChanged -= value;
        }

        private void WriteToDevice(object? value)
        {
            if (AddressConfig == null || CommunicationManager == null) return;

            var connection = CommunicationManager.GetConnection(ConnectionName);
            if (connection != null && connection.IsConnected)
            {
                try
                {
                    var rawValue = AddressConfig.ConvertToRaw(value);
                    if (rawValue != null)
                        CommunicationManager.WriteVariable(ConnectionName, AddressConfig.Address, rawValue);
                }
                catch (Exception ex)
                {
                    // setter 不是用户的显式命令，不弹窗打扰；但也不能像旧实现那样 catch {} 一吞了之——
                    // 写失败必须留痕，否则"赋值了却没进设备"永远查不出来
                    System.Diagnostics.Debug.WriteLine($"[NetworkVariable] 变量 {Name} 隐式写失败：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 恢复初始值 = 一次"把默认值写进设备"的命令：
        /// 走 TryWriteToValue 无条件下发（镜像恰与默认值相同时旧实现会被 SetProperty 短路、设备根本没写）；
        /// 离线时退化为仅更新镜像，让 UI 至少反映用户意图
        /// </summary>
        public void ResetToDefault()
        {
            if (!TryWriteToValue(DefaultValue, out _))
                Value = DefaultValue;
        }

        #region IVariableDisplaySource（HMI 画布组态预留，显式实现避免与模型接口冲突）

        string IVariableDisplaySource.BindingKey => Name;

        string IVariableDisplaySource.DisplayLabel => string.IsNullOrEmpty(Description) ? Name : Description;

        Type IVariableDisplaySource.DataType => DataType;

        object? IVariableDisplaySource.Value => Value;

        string IVariableDisplaySource.SourceLabel => ConnectionName ?? "网络";

        /// <summary>只读存储区来源不可写（Modbus 离散输入 / S7 输入映像 I）</summary>
        bool IVariableDisplaySource.IsReadOnly
        {
            get
            {
                if (AddressConfig is DeviceAddressBase<ModbusArea> ma)
                    return ma.Area == ModbusArea.DiscreteInputs;
                if (AddressConfig is DeviceAddressBase<S7Area> sa)
                    return sa.Area == S7Area.I;
                return false;
            }
        }

        void IVariableDisplaySource.WriteValue(object? newValue) => Value = newValue;

        #endregion
    }
}
