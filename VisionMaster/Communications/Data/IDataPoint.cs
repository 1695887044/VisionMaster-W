using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 数据点通用接口（所有数据点必须实现）
    /// </summary>
    public interface IDataPoint
    {
        /// <summary>
        /// 数据点名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 关联的地址配置
        /// </summary>
        DeviceAddressBase Address { get; }

        /// <summary>
        /// 数据值（装箱后的值）
        /// </summary>
        object? Value { get; }


        /// <summary>
        /// 数据类型
        /// </summary>
        Type ValueType { get; }
        /// <summary>
        /// 数据质量
        /// </summary>
        DataQuality Quality { get; }

        /// <summary>
        /// 时间戳（UTC）
        /// </summary>
        DateTime Timestamp { get; }

        /// <summary>
        /// 错误消息
        /// </summary>
        string? ErrorMessage { get; }

        /// <summary>
        /// 数据点是否已变化
        /// </summary>
        bool HasChanged { get; }

        /// <summary>
        /// 确认变化并重置标志
        /// </summary>
        void AcceptChanges();

        /// <summary>
        /// 标记数据为无效状态
        /// </summary>
        void MarkAsBad(string errorMessage);

        /// <summary>
        /// 更新数据点值
        /// </summary>
        void UpdateValue(object? value);
    }
}
