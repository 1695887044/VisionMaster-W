using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>连接配置基类，定义所有连接配置的通用属性和方法。</para>
    /// <para>所有具体的连接配置类都应继承此类。</para>
    /// </summary>
    public abstract class ConnectionConfigBase : INotifyPropertyChanged
    {
        /// <summary>
        /// 属性变化事件
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// <para>获取或设置通信协议类型。</para>
        /// </summary>
        public CommunicationType Type { get; set; }

        /// <summary>
        /// <para>获取或设置连接超时时间（毫秒）。</para>
        /// <para>默认值：3000ms</para>
        /// <para>范围：100-60000ms</para>
        /// </summary>
        [SuperDisplay(Name = "超时时间(ms)", GroupPath = "高级参数", Order = 10, ColSpan = 4)]
        [RangeValidation(100, 60000, "超时必须在 100 - 60000 之间")]
        public int TimeoutMs { get; set; } = 3000;

        /// <summary>
        /// <para>获取或设置重试次数。</para>
        /// <para>默认值：3次</para>
        /// <para>范围：0-10次</para>
        /// </summary>
        [SuperDisplay(Name = "重试次数", GroupPath = "高级参数", Order = 11, ColSpan = 4)]
        [RangeValidation(0, 10, "重试次数不能小于0")]
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// <para>获取或设置重试间隔时间（毫秒）。</para>
        /// <para>默认值：1000ms</para>
        /// </summary>
        [SuperDisplay(Name = "重试间隔(ms)", GroupPath = "高级参数", Order = 12, ColSpan = 4)]
        public int RetryIntervalMs { get; set; } = 1000;

        /// <summary>
        /// <para>创建对应的通信连接对象。</para>
        /// <para>由子类实现，根据配置创建具体的连接实例。</para>
        /// </summary>
        /// <returns>通信连接对象</returns>
        public abstract ICommunicationConnection CreateConnection();

        /// <summary>
        /// <para>克隆当前配置对象。</para>
        /// <para>创建一个与当前对象完全相同的新实例。</para>
        /// </summary>
        /// <returns>配置对象的克隆副本</returns>
        public abstract ConnectionConfigBase Clone();

        /// <summary>
        /// <para>验证配置是否有效。</para>
        /// </summary>
        /// <param name="errorMessage">验证失败时的错误信息</param>
        /// <returns>如果验证通过返回true，否则返回false</returns>
        public virtual bool Validate(out string errorMessage)
        {
            errorMessage = string.Empty;
            if (TimeoutMs <= 0) { errorMessage = "超时必须>0"; return false; }
            if (RetryCount < 0) { errorMessage = "重试次数不能<0"; return false; }
            return true;
        }

        /// <summary>
        /// <para>触发属性变化事件。</para>
        /// </summary>
        /// <param name="name">发生变化的属性名称</param>
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}