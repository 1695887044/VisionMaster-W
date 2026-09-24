using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 配置态插件视图的宿主上下文（只读快照）
    ///
    /// 为什么需要它
    /// ---------
    /// 插件 DLL 是独立程序集，只引用 Core.Interfaces（见 Plugin.*.csproj），
    /// 拿不到宿主的 AppSettingsService / HttpImageServer —— 也就读不到
    /// AppConfig.json 里的监听端口、访问令牌，更不知道"当前流程叫什么"。
    /// 于是由宿主在打开配置窗口时把这份快照推给插件视图（见 PluginConfigShellViewModel）。
    ///
    /// 为什么是"快照"而不是"活对象"
    /// ---------
    /// 插件视图只用于"照着当前配置拼一次请求"（如本地图片推送测试），
    /// 不需要感知宿主配置的热变更；传值类型可以彻底避免插件反向持有宿主对象、
    /// 在窗口关闭后仍引用已释放资源。
    /// </summary>
    public class PluginConfigContext
    {
        /// <summary>
        /// 当前流程名
        /// HTTP 收图按流程名分槽，URL 路径 /flow/{流程名} 里也要用（拼 URL 前必须转义）
        /// </summary>
        public string FlowName { get; set; } = string.Empty;

        /// <summary>
        /// 宿主 HTTP 收图服务在 AppConfig.json 里是否启用
        /// </summary>
        public bool HttpEnabled { get; set; }

        /// <summary>
        /// 宿主 HTTP 收图服务此刻是否真的在监听
        /// 与 <see cref="HttpEnabled"/> 可能不一致：端口被占用、启动抛异常时配置是启用的但没在听
        /// </summary>
        public bool HttpListening { get; set; }

        /// <summary>
        /// 推送目标主机
        /// 已由宿主把 0.0.0.0 / [::] 这类"监听任意网卡"的地址映射为可连接的本地回环地址
        /// </summary>
        public string HttpHost { get; set; } = "127.0.0.1";

        /// <summary>
        /// 推送目标端口
        /// </summary>
        public int HttpPort { get; set; }

        /// <summary>
        /// 访问令牌（请求头 Authorization: Bearer {HttpToken}）
        /// </summary>
        public string HttpToken { get; set; } = string.Empty;

        /// <summary>
        /// 服务端等待流程执行完成的上限（毫秒）
        /// 客户端自身的超时应略大于它，否则服务端还在跑，客户端先断，看到的错误会失真
        /// </summary>
        public int RequestTimeoutMs { get; set; }
    }

    /// <summary>
    /// 插件配置视图的上下文接收者（可选实现）
    ///
    /// 实现者：既实现了 <see cref="IPluginCustomViewProvider"/>，又需要知道宿主运行环境的插件。
    /// 不实现此接口的插件不受任何影响 —— 宿主在 <c>OnDialogOpened</c> 里做一次
    /// <c>is IPluginConfigContextProvider</c> 判定，不匹配就跳过，属于非破坏性扩展。
    ///
    /// 调用时机：宿主在配置窗口打开、且已把插件视图挂进内容区之后调用一次。
    /// 实现方应把值存进公开可绑定属性（视图 DataContext 即插件实例，绑定自动生效）。
    /// </summary>
    public interface IPluginConfigContextProvider
    {
        /// <summary>
        /// 接收宿主透传的配置上下文快照
        /// </summary>
        /// <param name="context">上下文快照，宿主保证非 null</param>
        void SetConfigContext(PluginConfigContext context);
    }
}
