using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaRenderCatalog"/> 的默认实现：把 <see cref="ElementRegistry.All"/> 里的
    /// WPF 描述符投影成无 WPF 的只读 DTO（M1a：Web 渲染器要"类型键 + 默认尺寸 + 属性默认值"）。
    ///
    /// 为什么需要这一层
    /// ---------
    /// <c>ElementDescriptor</c> / <c>ElementPropertyDescriptor</c> 带着
    /// <c>ControlType</c>、<c>DependencyProperty</c>、<c>Group</c> 这些只有 WPF 侧认得的东西；
    /// Web 运行态模块只引用 <c>VM.Scada</c>（领域层），看不到 <c>VM.Scada.Controls</c>。
    /// 本类就是那条单向投影线：WPF 侧进、DTO 出，Web 侧不必反向依赖控件库。
    ///
    /// <b>构建时机：首次访问时懒构建，且宿主必须先注册趋势图</b>
    /// ---------
    /// 目录内容取决于"此刻注册表里有什么"，而趋势图（<c>Hmi.ChartTrend</c>）在<b>另一个程序集</b>，
    /// 跨程序集的类型静态构造不会因为"引用了 DLL"就跑——必须由宿主在启动时显式调
    /// <c>ScadaChartElements.Register()</c>（见该类注释与 <c>App.Initialize</c> 那一行）。
    /// 所以：
    ///   · <b>不要</b>在 <c>App.RegisterTypes</c> 阶段解析本服务（那一刻趋势图还没注册）；
    ///   · 首次访问必须晚于 <c>ScadaChartElements.Register()</c>，否则快照里会永久少掉趋势图，
    ///     且<b>不报任何错</b>——只是 Web 工具箱里少一个图元。
    /// 懒构建（而不是构造时构建）就是为了让"注册先于解析"这条时序成为调用方的事，
    /// 而不是被容器解析时机绑死。
    ///
    /// 快照只建一次：图元注册表在启动后不再变化（运行期不注册新图元类型），
    /// 每次访问都重新投影只会白费 CPU，且会让 Web 侧拿到的两份目录"看起来不一样"。
    ///
    /// 空表不抛异常：注册表为空时返回空表并记一条 Warn。抛异常会把"Web 模块起不来"
    /// 变成"宿主启动失败"，而 Web 是可选模块（与 <see cref="ScadaWebModuleLoader"/> 同一口径）。
    /// </summary>
    public sealed class ScadaRenderCatalog : IScadaRenderCatalog
    {
        private readonly object _gate = new();
        private readonly ILogService _log;
        private IReadOnlyList<ScadaElementDescriptorInfo>? _all;

        public ScadaRenderCatalog(ILogService log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <inheritdoc/>
        public IReadOnlyList<ScadaElementDescriptorInfo> All
        {
            get
            {
                var snapshot = _all;
                if (snapshot != null)
                    return snapshot;

                // 双检锁：Web 侧可能从多个会话线程同时读，而懒构建要投影 40+ 类图元的上百条属性。
                lock (_gate)
                {
                    return _all ??= Build();
                }
            }
        }

        private IReadOnlyList<ScadaElementDescriptorInfo> Build()
        {
            var descriptors = ElementRegistry.All;

            if (descriptors.Count == 0)
            {
                _log.Warn("[渲染目录] 图元注册表为空，Web 渲染器将拿不到任何图元"
                    + "（检查 VM.Scada.Controls 是否加载成功、ScadaChartElements.Register() 是否已调用）");
                return Array.Empty<ScadaElementDescriptorInfo>();
            }

            var result = new ScadaElementDescriptorInfo[descriptors.Count];
            for (int i = 0; i < descriptors.Count; i++)
                result[i] = Map(descriptors[i]);

            _log.Info($"[渲染目录] 已投影 {result.Length} 类图元的渲染元数据（含属性默认值与候选项）");
            return result;
        }

        /// <summary>
        /// 单个描述符的投影。属性顺序逐字保留（描述符里的次序就是属性面板/Web 面板上的次序）。
        /// </summary>
        private static ScadaElementDescriptorInfo Map(ElementDescriptor descriptor)
        {
            var source = descriptor.Properties;
            var properties = new ScadaElementPropertyInfo[source.Count];
            var keys = new string[source.Count];

            for (int i = 0; i < source.Count; i++)
            {
                var property = source[i];
                keys[i] = property.Key;
                properties[i] = new ScadaElementPropertyInfo
                {
                    Key = property.Key,
                    DisplayName = property.DisplayName,
                    DefaultValue = property.DefaultValue ?? string.Empty,
                    // 存枚举成员名（长期标识）：枚举本身住在带 WPF 的 VM.Scada.Controls 里，Web 侧引用不到。
                    Kind = property.Kind.ToString(),
                    Choices = property.Choices is { Count: > 0 } choices ? choices.ToArray() : Array.Empty<string>(),
                    IsBindable = property.IsBindable,
                };
            }

            var events = descriptor.Events;
            var eventNames = new string[events.Count];
            for (int i = 0; i < eventNames.Length; i++)
                eventNames[i] = events[i].ToString();

            return new ScadaElementDescriptorInfo
            {
                TypeKey = descriptor.TypeKey,
                DisplayName = descriptor.DisplayName,
                Category = descriptor.Category,
                DefaultWidth = descriptor.DefaultWidth,
                DefaultHeight = descriptor.DefaultHeight,
                ShowInToolbox = descriptor.ShowInToolbox,
                PropertyKeys = keys,
                Properties = properties,
                EventNames = eventNames,
            };
        }
    }
}
