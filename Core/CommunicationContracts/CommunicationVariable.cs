using System;
using Newtonsoft.Json;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 通讯变量类，用于定义和管理通讯变量
    /// </summary>
    public class CommunicationVariable
    {
        /// <summary>
        /// 所属连接名称
        /// </summary>
        public string ConnectionName { get; set; } = string.Empty;

        /// <summary>
        /// 变量名称
        /// </summary>
        public string VariableName { get; set; } = string.Empty;

        /// <summary>
        /// 通讯地址
        /// </summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>
        /// 值类型
        /// </summary>
        public string ValueType { get; set; } = typeof(object).AssemblyQualifiedName;

        /// <summary>
        /// 访问权限模式
        /// </summary>
        public VariableAccessMode AccessMode { get; set; } = VariableAccessMode.ReadOnly;

        /// <summary>
        /// 当前值
        /// </summary>
        public object? CurrentValue { get; private set; }

        /// <summary>
        /// 镜像回调（可选）：轮询读到新值时推送给绑定的网络变量（NetworkVariableModel.UpdateMirrorValue），
        /// 实现"轮询→镜像→UI 通知"链路；未设置则只走 ValueChanged 事件
        /// </summary>
        [JsonIgnore]
        public Action<object?>? MirrorCallback { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdateTime { get; private set; }

        /// <summary>
        /// 值变化事件
        /// </summary>
        public event EventHandler<object?>? ValueChanged;

        /// <summary>是否已推送过首次值（首次无论是否变化都强制推送，保证 UI 拿到设备真实初值）</summary>
        [JsonIgnore]
        private bool _hasPublished;

        /// <summary>
        /// 更新变量值
        /// </summary>
        /// <param name="newValue">新值</param>
        public void UpdateValue(object? newValue)
        {
            // 首次强制推送（设备值与初始默认相等时也通知，否则 UI 一直显示空/旧值）；
            // 之后仅在值变化时触发事件通知
            if (!_hasPublished || !Equals(CurrentValue, newValue))
            {
                _hasPublished = true;
                CurrentValue = newValue;
                LastUpdateTime = DateTime.Now;
                MirrorCallback?.Invoke(newValue);
                ValueChanged?.Invoke(this, newValue);
            }
        }
    }
}
