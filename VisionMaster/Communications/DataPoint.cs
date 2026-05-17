using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 数据质量码（遵循OPC UA标准）
    /// </summary>
    public enum DataQuality
    {
        Good,       // 数据有效
        Bad,        // 数据无效（通讯失败）
        Uncertain,  // 数据不确定（超时、值超出范围）
        NotConnected // 未连接
    }

    /// <summary>
    /// 泛型数据点类：存储运行时读取到的值和状态
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class DataPoint<T> : BindableBase
    {
        private T _value;
        private DataQuality _quality = DataQuality.NotConnected;
        private DateTime _timestamp = DateTime.MinValue;
        private string _errorMessage = string.Empty;

        /// <summary>
        /// 关联的通讯地址（只读，创建后不可修改）
        /// </summary>
        public DeviceAddressBase Address { get; }

        /// <summary>
        /// 数据点名称（用于UI显示和日志）
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 读取到的最新值
        /// </summary>
        public T Value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;

                _value = value;
                Timestamp = DateTime.Now;
                Quality = DataQuality.Good;
                ErrorMessage = string.Empty;

                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// 数据质量
        /// </summary>
        public DataQuality Quality
        {
            get => _quality;
            set => SetProperty(ref _quality, value);
        }

        /// <summary>
        /// 最后更新时间戳
        /// </summary>
        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        /// <summary>
        /// 错误信息（当Quality为Bad时有效）
        /// </summary>
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="name">数据点名称</param>
        /// <param name="address">关联的通讯地址</param>
        public DataPoint(string name, DeviceAddressBase address)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Address = address ?? throw new ArgumentNullException(nameof(address));
        }

        /// <summary>
        /// 标记数据为无效
        /// </summary>
        /// <param name="errorMessage">错误信息</param>
        public void MarkAsBad(string errorMessage)
        {
            Quality = DataQuality.Bad;
            ErrorMessage = errorMessage;
            Timestamp = DateTime.Now;
            RaisePropertyChanged(nameof(Value)); // 通知UI数据已失效
        }

        /// <summary>
        /// 隐式转换为值类型，简化代码
        /// </summary>
        public static implicit operator T(DataPoint<T> dataPoint) => dataPoint.Value;

        public override string ToString()
        {
            return $"{Name}: {Value} [{Quality}] @ {Timestamp:HH:mm:ss.fff}";
        }
    }
}
