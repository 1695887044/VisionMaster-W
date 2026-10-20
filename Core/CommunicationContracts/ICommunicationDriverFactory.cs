using System;
using System.Collections.Generic;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>通讯协议驱动工厂：让"新增一个协议"从"改 6 处"变成"写一个类 + 注册一次"。</para>
    /// <para><b>为什么需要它</b>：改造前新增协议要同时改 —— <see cref="CommunicationType"/> 枚举（封闭）、
    /// <c>CommunicationConfig.TryCreateConfig</c> 的 switch、<c>CommunicationProtocols.Implemented</c> 数组、
    /// 新建配置类、实现 <see cref="ICommunicationConnection"/>、再改 <c>ConnectionFactoryManager.RegisterDefaults</c>；
    /// 且"清单与工厂一致"只靠一条断言手工钉着。本接口把后三件事合成一件。</para>
    /// <para><b>与 <see cref="IConnectionFactory"/> 的区别</b>：那个按枚举键、只能替换内建三种协议；
    /// 本接口按字符串 Id、面向"外部驱动"，且要求驱动自带配置类型，宿主据此生成配置面板。</para>
    /// <para><b>线程</b>：注册在启动期完成，运行期只读；实现必须是线程安全的。</para>
    /// </summary>
    public interface ICommunicationDriverFactory
    {
        /// <summary>
        /// 驱动稳定标识（唯一，大小写不敏感；建议 "厂商.协议"，如 "modbus.tcp"）。
        /// <para>落盘用这个字符串，**不要用显示名**——改显示名不该打断已有方案。</para>
        /// </summary>
        string DriverId { get; }

        /// <summary>界面上显示的名字（中文优先）。改它不影响已有方案。</summary>
        string DisplayName { get; }

        /// <summary>驱动说明（界面提示 / 帮助页用）。</summary>
        string Description { get; }

        /// <summary>
        /// 该驱动的链路配置类型（决定属性面板渲染哪些字段）。
        /// <para>返回 null = 不需要链路参数（极少见）。类型必须有无参构造。</para>
        /// </summary>
        Type? ConfigType { get; }

        /// <summary>按配置造一个连接实例（一个端点 = 一条 TCP / 一个 COM）。</summary>
        ICommunicationConnection Create(ConnectionConfigBase config);
    }

    /// <summary>
    /// 驱动注册表：宿主启动时把内建驱动注册进来，插件扫描接入后追加外部驱动。
    /// <para><b>单一真相源</b>：哪些协议可用只看本表。<see cref="CommunicationConfig"/> 的协议下拉
    /// 与 <c>ConnectionFactoryManager</c> 都从这里取，不再各持一份清单。</para>
    /// </summary>
    public sealed class CommunicationDriverRegistry
    {
        private readonly Dictionary<string, ICommunicationDriverFactory> _drivers =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();

        /// <summary>已注册驱动（按注册顺序，界面下拉用）。</summary>
        public IReadOnlyList<ICommunicationDriverFactory> All
        {
            get { lock (_gate) return new List<ICommunicationDriverFactory>(_drivers.Values); }
        }

        /// <summary>已注册驱动数（诊断/断言用）。</summary>
        public int Count
        {
            get { lock (_gate) return _drivers.Count; }
        }

        /// <summary>
        /// 注册驱动。同 Id 重复注册**抛异常**而不是静默覆盖：
        /// 两个程序集抢同一个协议 Id 时，静默覆盖会让"到底跑的是哪个实现"变成运气问题。
        /// 需要替换（测试夹具）请先 <see cref="Unregister"/>。
        /// </summary>
        public void Register(ICommunicationDriverFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            string id = factory.DriverId?.Trim() ?? string.Empty;
            if (id.Length == 0)
                throw new ArgumentException("驱动 Id 不能为空", nameof(factory));
            if (string.IsNullOrWhiteSpace(factory.DisplayName))
                throw new ArgumentException($"驱动「{id}」的显示名不能为空", nameof(factory));

            lock (_gate)
            {
                if (_drivers.TryGetValue(id, out var existing) && !ReferenceEquals(existing, factory))
                {
                    throw new InvalidOperationException(
                        $"重复的通讯驱动 Id「{id}」：{existing.GetType().Name} / {factory.GetType().Name}");
                }
                _drivers[id] = factory;
            }
        }

        /// <summary>注销驱动（测试夹具替换实现用；生产不调用）。</summary>
        public bool Unregister(string driverId)
        {
            if (string.IsNullOrWhiteSpace(driverId)) return false;
            lock (_gate) return _drivers.Remove(driverId.Trim());
        }

        /// <summary>按 Id 取驱动。</summary>
        public bool TryGet(string driverId, out ICommunicationDriverFactory factory)
        {
            factory = null!;
            if (string.IsNullOrWhiteSpace(driverId)) return false;
            lock (_gate) return _drivers.TryGetValue(driverId.Trim(), out factory!);
        }

        /// <summary>按链路配置类型找驱动（反序列化后按配置对象反推是哪个驱动）。</summary>
        public bool TryGetByConfigType(Type configType, out ICommunicationDriverFactory factory)
        {
            factory = null!;
            if (configType == null) return false;

            lock (_gate)
            {
                foreach (var candidate in _drivers.Values)
                {
                    if (candidate.ConfigType != null && candidate.ConfigType == configType)
                    {
                        factory = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>按配置造连接；驱动不存在时抛 <see cref="NotSupportedException"/>（带可用清单，便于现场自查）。</summary>
        public ICommunicationConnection Create(string driverId, ConnectionConfigBase config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            if (!TryGet(driverId, out var factory))
                throw new NotSupportedException(
                    $"不支持的通讯驱动「{driverId}」，当前可用：{ImplementedText}");

            return factory.Create(config);
        }

        /// <summary>可用驱动 Id 的展示文案（错误信息 / 界面提示用）。</summary>
        public string ImplementedText
        {
            get
            {
                var list = All;
                if (list.Count == 0) return "(无)";

                var parts = new List<string>(list.Count);
                foreach (var driver in list) parts.Add(driver.DriverId);
                return string.Join(" / ", parts);
            }
        }
    }
}
