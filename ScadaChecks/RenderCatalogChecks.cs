using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [RC] 渲染目录（M1a）：<see cref="ScadaRenderCatalog"/> 把 WPF 图元描述符投影成
        /// 无 WPF 的只读 DTO，供 Web 渲染器补默认值与画属性面板。
        ///
        /// 覆盖的七件事
        /// ---------
        /// ① 目录非空，且**逐项覆盖**注册表（投影不许漏项——漏一个类型就是 Web 工具箱少一个图元，不报错）；
        /// ② 十个常用类型键都在（文本/矩形/底板/IO 域/棒图/指示灯/灯/罐体/阀门/表盘）；
        /// ③ 每个图元都有正的默认宽高（Web 新建图元时用，为 0 会画成一条线）；
        /// ④ 每个图元的 <see cref="ScadaElementDescriptorInfo.Properties"/> 都含五个几何键
        ///    （<c>$X/$Y/$Width/$Height/$Rotation</c>）——属性面板与渲染器都假定它们永远在；
        /// ⑤ <see cref="ScadaElementDescriptorInfo.PropertyKeys"/> 与 <see cref="ScadaElementDescriptorInfo.Properties"/>
        ///    同序同键（两份视图由同一投影生成，任何一侧漂移都说明有人手改了一边）；
        /// ⑥ <c>Hmi.Tank</c> 的 <c>Level</c> 默认值可读（<c>"50"</c>、Number 型、可绑变量）——
        ///    "默认值能取到"正是 M1 渲染器补默认值的前提，取不到就会画成 0%；
        /// ⑦ 事件名全部是 <see cref="ScadaEventType"/> 的成员名（存名字而不是枚举值，Web 侧按名字配钩子）。
        ///
        /// 为什么先调 <c>ScadaChartElements.Register()</c>
        /// ---------
        /// 目录是**懒构建**的，而趋势图在另一个程序集里、必须由宿主显式注册（跨程序集的类型静态构造
        /// 不会因为"引用了 DLL"就跑）。这里先调一次（幂等），让断言与宿主启动链的时序一致；
        /// 否则"目录里少了 Hmi.ChartTrend"会是断言自身制造出来的假红。
        ///
        /// 接线一行：<c>RenderCatalogChecks.Run();</c>（放在 <c>ModuleLoaderChecks.Run();</c> 之后，
        /// 那之前 <c>ElementControlChecks</c> 已经调过曲线注册，两处顺序都安全）
        /// </summary>
        public static class RenderCatalogChecks
        {
            /// <summary>接线一行：<c>RenderCatalogChecks.Run();</c></summary>
            public static void Run() => RunRenderCatalogChecks();
        }

        /// <summary>每个图元都必须声明的几何键（与 <c>GeometryProperties.Standard</c> 同源，见 <c>ElementValueAccess</c>）</summary>
        private static readonly string[] RenderCatalogGeometryKeys =
        {
            ElementValueAccess.XKey,
            ElementValueAccess.YKey,
            ElementValueAccess.WidthKey,
            ElementValueAccess.HeightKey,
            ElementValueAccess.RotationKey,
        };

        /// <summary>M1 渲染器首屏必须认得的一批图元（缺哪个就是 Web 上少哪个图元）</summary>
        private static readonly string[] RenderCatalogRequiredTypes =
        {
            "Hmi.Text", "Hmi.Rectangle", "Hmi.Panel", "Hmi.IOField", "Hmi.ProgressBar",
            "Hmi.Indicator", "Hmi.Lamp", "Hmi.Tank", "Hmi.Valve", "Hmi.Gauge",
        };

        private static void RunRenderCatalogChecks()
        {
            Section("[RC] 渲染目录：图元投影 / 默认尺寸 / 属性默认值与几何键");

            // 幂等：宿主启动时也调过一次。见上面类注释"为什么先调"。
            VisionMaster.Scada.Controls.Charts.ScadaChartElements.Register();

            var log = new LoggerStub();
            var catalog = new ScadaRenderCatalog(log);
            var all = catalog.All;

            Check("[RC1] 渲染目录非空（且非空时不该刷 Warn）",
                all.Count > 0 && log.Warnings.Count == 0,
                $"投影 {all.Count} 类图元；Warn={string.Join(" | ", log.Warnings)}");

            var registryCount = ElementRegistry.All.Count;
            Check("[RC2] 目录逐项覆盖图元注册表（投影漏项 = Web 上少一个图元，且不报错）",
                all.Count == registryCount,
                $"目录 {all.Count} / 注册表 {registryCount}；缺：" +
                string.Join("、", ElementRegistry.All.Select(d => d.TypeKey)
                    .Where(key => !all.Any(info => string.Equals(info.TypeKey, key, StringComparison.OrdinalIgnoreCase)))));

            var missingTypes = RenderCatalogRequiredTypes
                .Where(key => !all.Any(info => string.Equals(info.TypeKey, key, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            Check("[RC3] M1 首批图元类型键齐全（文本/矩形/底板/IO 域/棒图/指示灯/灯/罐体/阀门/表盘）",
                missingTypes.Length == 0,
                missingTypes.Length == 0 ? string.Join("、", RenderCatalogRequiredTypes) : "缺：" + string.Join("、", missingTypes));

            var badSize = all.Where(info => !(info.DefaultWidth > 0) || !(info.DefaultHeight > 0)).ToArray();
            Check("[RC4] 每个图元都有正的默认宽高（为 0 会在 Web 上画成一条线）",
                badSize.Length == 0,
                badSize.Length == 0
                    ? string.Join("、", all.Select(info => $"{info.TypeKey} {info.DefaultWidth}×{info.DefaultHeight}").Take(3)) + " …"
                    : string.Join("、", badSize.Select(info => $"{info.TypeKey} {info.DefaultWidth}×{info.DefaultHeight}")));

            var missingGeometry = new List<string>();
            var keyListDrift = new List<string>();

            foreach (var info in all)
            {
                foreach (var geometryKey in RenderCatalogGeometryKeys)
                {
                    if (!info.Properties.Any(p => string.Equals(p.Key, geometryKey, StringComparison.Ordinal)))
                        missingGeometry.Add($"{info.TypeKey} 缺 {geometryKey}");
                }

                var propertyKeys = info.Properties.Select(p => p.Key).ToArray();
                if (!propertyKeys.SequenceEqual(info.PropertyKeys, StringComparer.Ordinal))
                    keyListDrift.Add($"{info.TypeKey}：Properties=[{string.Join(",", propertyKeys)}] PropertyKeys=[{string.Join(",", info.PropertyKeys)}]");
            }

            Check("[RC5] 每个图元的 Properties 都含 $X/$Y/$Width/$Height/$Rotation",
                missingGeometry.Count == 0,
                missingGeometry.Count == 0 ? $"{all.Count} 类图元全部含五个几何键" : string.Join("；", missingGeometry.Take(5)));

            Check("[RC6] PropertyKeys 与 Properties 同序同键（两份视图由同一投影生成，不许各说各话）",
                keyListDrift.Count == 0,
                keyListDrift.Count == 0 ? "" : string.Join("；", keyListDrift.Take(3)));

            var tank = all.FirstOrDefault(info => string.Equals(info.TypeKey, "Hmi.Tank", StringComparison.OrdinalIgnoreCase));
            var level = tank?.Properties.FirstOrDefault(p => string.Equals(p.Key, "Level", StringComparison.Ordinal));
            var levelReadable = level != null
                                && string.Equals(level.Kind, nameof(ElementPropertyKind.Number), StringComparison.Ordinal)
                                && level.IsBindable
                                && double.TryParse(level.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var levelValue)
                                && Math.Abs(levelValue - 50d) < 0.0001;

            Check("[RC7] Hmi.Tank 的 Level 默认值可读（\"50\"、Number 型、可绑变量）——渲染器补默认值的前提",
                levelReadable,
                level == null
                    ? "Hmi.Tank 或 Level 属性未投影出来"
                    : $"Kind={level.Kind} Default={level.DefaultValue} IsBindable={level.IsBindable}");

            var badEventNames = all
                .SelectMany(info => info.EventNames.Select(name => (info.TypeKey, name)))
                .Where(pair => !Enum.TryParse<ScadaEventType>(pair.name, ignoreCase: false, out _))
                .ToArray();

            Check("[RC8] 事件名全部是 ScadaEventType 的成员名（Web 侧按名字配钩子）",
                badEventNames.Length == 0,
                badEventNames.Length == 0
                    ? $"共 {all.Sum(info => info.EventNames.Count)} 条事件声明"
                    : string.Join("、", badEventNames.Select(pair => $"{pair.TypeKey}.{pair.name}")));
        }
    }
}
