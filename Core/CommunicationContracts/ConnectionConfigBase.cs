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
        /// <para>获取或设置<b>连接超时</b>时间（毫秒）——只约束"建立连接"这一步。</para>
        /// <para>默认值：3000ms</para>
        /// <para>范围：100-60000ms</para>
        /// <para>去向：TCP 连接下发到 HSL 的 ConnectTimeOut；同时被 Manager 当作等待首次建连的预算。</para>
        /// </summary>
        [SuperDisplay(Name = "超时时间(ms)", Group = new string[] { "高级参数" }, Order = 10, ColSpan = 4)]
        [RangeValidation(100, 60000, "超时必须在 100 - 60000 之间")]
        public virtual int TimeoutMs { get; set; } = 3000;

        /// <summary>
        /// <para>获取或设置<b>读重试次数</b>。</para>
        /// <para>默认值：3 次</para>
        /// <para>范围：0-10 次</para>
        /// <para>作用范围：<b>只作用于「立刻读」这条读路径</b>（「立刻读」算子 / 网络变量的立刻读）。
        /// 一次读失败后按 <see cref="RetryIntervalMs"/> 间隔重试，最多重试这么多次
        /// （总尝试次数 = 1 + 本值；重试用尽仍失败则按原错误出口抛出）。</para>
        /// <para><b>只管读，不管写</b>：写命令绝不重试——重复下发在工业现场可能造成二次动作
        /// （双联阀、气缸二次伸出这类事故）；周期轮询也不重试，它的"重试"就是下一拍。</para>
        /// <para>断线重连的次数由管理器自己的上限控制（<c>MaxReconnectAttempts</c>），与本字段无关；
        /// 本字段只影响"一次读失败后要不要再试几次"。</para>
        /// </summary>
        [SuperDisplay(Name = "重试次数", Group = new string[] { "高级参数" }, Order = 11, ColSpan = 4)]
        [RangeValidation(0, 10, "重试次数不能小于0")]
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// <para>获取或设置重试间隔时间（毫秒）。</para>
        /// <para>默认值：1000ms</para>
        /// <para>两个消费者：① <b>断线重连</b>的退避基准（首次退避 = 本值，之后按 2 的幂放大，封顶 30 秒）；
        /// ② <b>「立刻读」失败后的重试间隔</b>（见 <see cref="RetryCount"/>）。</para>
        /// <para>填 0 = 立即重试（不睡），负数按 0 处理。</para>
        /// </summary>
        [SuperDisplay(Name = "重试间隔(ms)", Group = new string[] { "高级参数" }, Order = 12, ColSpan = 4)]
        public int RetryIntervalMs { get; set; } = 1000;

        /// <summary>
        /// <para>获取或设置<b>读超时</b>时间（毫秒）——约束每一次读写帧的等待，与"连接超时"是两件事。</para>
        /// <para>默认值：5000ms（与 HSL 自身的 ReceiveTimeOut 默认值一致，故不填也等于旧行为）</para>
        /// <para>范围：100-60000ms</para>
        /// <para>去向：统一下发到 HSL 的 ReceiveTimeOut（TCP / 串口共用同一入口）。</para>
        /// </summary>
        [SuperDisplay(Name = "读超时(ms)", Group = new string[] { "高级参数" }, Order = 13, ColSpan = 4)]
        [RangeValidation(100, 60000, "读超时必须在 100 - 60000 之间")]
        public int ReadTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// <para>创建对应的通信连接对象。</para>
        /// <para>由子类实现，根据配置创建具体的连接实例。</para>
        /// </summary>
        /// <returns>通信连接对象</returns>

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
            if (ReadTimeoutMs <= 0) { errorMessage = "读超时必须>0"; return false; }
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

