using System.ComponentModel.DataAnnotations;
using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>OPC UA 协议配置类。</para>
    /// <para>用于配置 OPC UA 服务器的连接参数。</para>
    /// </summary>
    public class OpcUaConfig : EthernetConfigBase
    {
        /// <summary>
        /// <para>获取或设置端点 URL。</para>
        /// <para>默认值：opc.tcp://localhost:4840</para>
        /// </summary>
        [SuperDisplay(Name = "端点URL", GroupPath = "OPC UA参数", Order = 5, ColSpan = 12)]
        [Required(ErrorMessage = "端点URL不能为空")]
        public string EndpointUrl { get; set; } = "opc.tcp://localhost:4840";

        /// <summary>
        /// <para>获取或设置安全策略。</para>
        /// <para>默认值：None</para>
        /// </summary>
        [SuperDisplay(Name = "安全策略", GroupPath = "OPC UA参数", Order = 6, ColSpan = 6)]
        public OpcUaSecurityPolicy SecurityPolicy { get; set; } = OpcUaSecurityPolicy.None;

        /// <summary>
        /// <para>获取或设置消息安全模式。</para>
        /// <para>默认值：None</para>
        /// </summary>
        [SuperDisplay(Name = "消息安全模式", GroupPath = "OPC UA参数", Order = 7, ColSpan = 6)]
        public OpcUaMessageSecurityMode MessageSecurityMode { get; set; } = OpcUaMessageSecurityMode.None;

        /// <summary>
        /// <para>获取或设置用户名。</para>
        /// <para>默认值：空字符串（匿名访问）</para>
        /// </summary>
        [SuperDisplay(Name = "用户名", GroupPath = "OPC UA参数", Order = 8, ColSpan = 6)]
        public string UserName { get; set; } = string.Empty;

        /// <summary>
        /// <para>获取或设置密码。</para>
        /// <para>默认值：空字符串</para>
        /// </summary>
        [SuperDisplay(Name = "密码", GroupPath = "OPC UA参数", Order = 9, ColSpan = 6)]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// <para>获取或设置会话超时时间（毫秒）。</para>
        /// <para>默认值：30000ms (30秒)</para>
        /// </summary>
        [SuperDisplay(Name = "会话超时(ms)", GroupPath = "OPC UA参数", Order = 10, ColSpan = 6)]
        public int SessionTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// <para>获取或设置订阅发布间隔（毫秒）。</para>
        /// <para>默认值：1000ms (1秒)</para>
        /// </summary>
        [SuperDisplay(Name = "订阅发布间隔(ms)", GroupPath = "OPC UA参数", Order = 11, ColSpan = 6)]
        public int PublishingIntervalMs { get; set; } = 1000;

        /// <summary>
        /// <para>初始化 OPC UA 配置。</para>
        /// <para>默认端口设置为 4840（OPC UA 默认端口）。</para>
        /// </summary>
        public OpcUaConfig()
        {
            Port = 4840;
        }

        /// <summary>
        /// <para>创建 OPC UA 连接对象。</para>
        /// <para>注意：此方法尚未实现。</para>
        /// </summary>
        /// <returns>OPC UA 连接实例</returns>
        public override ICommunicationConnection CreateConnection() => throw new NotImplementedException();

        /// <summary>
        /// <para>克隆当前配置对象。</para>
        /// </summary>
        /// <returns>配置副本</returns>
        public override ConnectionConfigBase Clone() => new OpcUaConfig
        {
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            RetryIntervalMs = RetryIntervalMs,
            IpAddress = IpAddress,
            Port = Port,
            EnableKeepAlive = EnableKeepAlive,
            KeepAliveIntervalMs = KeepAliveIntervalMs,
            EndpointUrl = EndpointUrl,
            SecurityPolicy = SecurityPolicy,
            MessageSecurityMode = MessageSecurityMode,
            UserName = UserName,
            Password = Password,
            SessionTimeoutMs = SessionTimeoutMs,
            PublishingIntervalMs = PublishingIntervalMs
        };
    }
}