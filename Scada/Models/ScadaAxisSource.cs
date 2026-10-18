using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 一条"设计期选中的轴"：稳定身份 + 当时的轴名。
    ///
    /// 与 <see cref="ScadaBinding"/> 的变量引用同一口径：<b>Id 优先、名字兜底</b>。
    /// 轴名只用于展示与旧数据兜底；机器寻址一律用 <see cref="AxisId"/>——
    /// 否则轴改名会把已经配好的轴面板打断（与变量绑定当年踩过的坑同一个形状）。
    /// </summary>
    public sealed class ScadaAxisRef
    {
        /// <summary>轴稳定身份（运动模块 <c>AxisMapping.AxisId</c>）</summary>
        public Guid AxisId { get; set; }

        /// <summary>轴名（展示串；改名后由运行态按 Id 回填最新名字）</summary>
        public string Name { get; set; } = string.Empty;

        public ScadaAxisRef() { }

        public ScadaAxisRef(Guid axisId, string? name)
        {
            AxisId = axisId;
            Name = (name ?? string.Empty).Trim();
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// 轴面板"候选轴清单"的落盘编解码。
    ///
    /// 为什么用 JSON 而不是 <c>"X,Y,Z"</c> 这种分隔串：轴名是用户给的自由文本，
    /// 完全可能含逗号、分号或竖线；分隔串必须在"转义规则"上再做一套约定，
    /// 而 JSON 的转义是现成的、可读的、Newtonsoft 已经在本工程里。
    ///
    /// 编解码刻意放在领域层（<c>VM.Scada</c>）而不是控件或宿主：
    /// 设计期属性面板、运行期轴面板、将来的离线校验都要按同一规则读同一份字符串。
    /// </summary>
    public static class ScadaAxisOptionsCodec
    {
        /// <summary>属性袋里的键（与 <see cref="ScadaElement.Properties"/> 同一命名空间）</summary>
        public const string Key = "AxisOptions";

        /// <summary>
        /// 解析候选轴清单。空串/坏 JSON 一律返回空表，<b>不抛异常</b>——
        /// 一条坏配置不该让整张画面打不开（与运行态"坏值只打角标"同一口径）。
        /// </summary>
        public static IReadOnlyList<ScadaAxisRef> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<ScadaAxisRef>();

            try
            {
                var list = JsonConvert.DeserializeObject<List<ScadaAxisRef>>(json);
                if (list == null || list.Count == 0)
                    return Array.Empty<ScadaAxisRef>();

                // 去重（按 Id，保留首次出现）并丢掉空 Id：空 Id 无法寻址，
                // 留着只会在下拉里长出一条永远显示"已移除"的项。
                var seen = new HashSet<Guid>();
                var result = new List<ScadaAxisRef>(list.Count);

                foreach (var item in list)
                {
                    if (item == null || item.AxisId == Guid.Empty) continue;
                    if (!seen.Add(item.AxisId)) continue;

                    result.Add(new ScadaAxisRef(item.AxisId, item.Name));
                }

                return result;
            }
            catch (JsonException)
            {
                return Array.Empty<ScadaAxisRef>();
            }
        }

        /// <summary>把候选轴清单写成规范串（去重、保序、名字 Trim）。</summary>
        public static string Format(IEnumerable<ScadaAxisRef>? refs)
        {
            if (refs == null)
                return string.Empty;

            var seen = new HashSet<Guid>();
            var list = new List<ScadaAxisRef>();

            foreach (var item in refs)
            {
                if (item == null || item.AxisId == Guid.Empty) continue;
                if (!seen.Add(item.AxisId)) continue;

                list.Add(new ScadaAxisRef(item.AxisId, item.Name));
            }

            return list.Count == 0 ? string.Empty : JsonConvert.SerializeObject(list);
        }
    }

    /// <summary>
    /// 运行期一根轴的"身份与可用性"快照（不含量值）。
    /// 轴面板用它刷新下拉项（名字可能被改、轴可能被停用/移除）。
    /// </summary>
    public sealed class ScadaAxisInfo
    {
        public ScadaAxisInfo(Guid axisId, string name, string cardCaption, bool enabled, bool isAvailable)
        {
            AxisId = axisId;
            Name = name ?? string.Empty;
            CardCaption = cardCaption ?? string.Empty;
            Enabled = enabled;
            IsAvailable = isAvailable;
        }

        public Guid AxisId { get; }
        public string Name { get; }
        public string CardCaption { get; }

        /// <summary>配置里是否启用（未启用的轴不应被操作，也不该出现在设计期候选里）</summary>
        public bool Enabled { get; }

        /// <summary>此刻是否真的可读（卡在线且已轮询到快照）</summary>
        public bool IsAvailable { get; }
    }

    /// <summary>
    /// 一根轴的量值快照（运行期只读）。
    ///
    /// 这是领域层自己的投影，不直接复用 <c>Core.Interfaces.AxisStatus</c>：
    /// <c>VM.Scada</c> 刻意不引用 <c>Core.Interfaces</c>（依赖方向是 Core → Scada），
    /// 由宿主侧的发布器做一次映射。好处是领域层/控件库能在无运动模块的宿主里
    /// 用假数据源直接断言（ScadaChecks 就是这么测的）。
    /// </summary>
    public readonly record struct ScadaAxisSample(
        bool IsValid,
        string? Error,
        double PositionMm,
        double VelocityMmPerS,
        bool Moving,
        bool InPosition,
        bool Enabled,
        bool Alarm,
        bool PositiveLimit,
        bool NegativeLimit,
        bool Origin,
        bool EmergencyStop,
        string StatusText,
        DateTime TimestampUtc)
    {
        /// <summary>拿不到快照时的"未知"（<b>不编 0</b>：0 是一个合法位置，会把"不知道"伪装成"在原点"）</summary>
        public static ScadaAxisSample Invalid(string? error)
            => new(false, error, 0, 0, false, false, false, false, false, false, false, false,
                   error ?? "运动卡未连接", DateTime.UtcNow);
    }

    /// <summary>
    /// 轴快照数据源（运行态）。
    ///
    /// 为什么由宿主注入、而不是让图元直接拿 <c>IMotionProvider</c>：
    /// 图元库刻意不认识运动模块（与"图元不认识变量注册表"同一条边界），
    /// 它只认这个只读接口；宿主侧的 <c>MotionStatePublisher</c> 是唯一接触
    /// <c>IMotionProvider</c> / <c>AxisStatus</c> 的地方。
    ///
    /// 读法：<b>拉模式</b>。轴面板在统一节拍（<c>ScadaBeatSource</c>）里读当前轴，
    /// 不订阅每轴事件——运动卡 10ms 轮询一次，若按值变化推事件，一屏几十个面板会把 UI 线程刷满。
    /// </summary>
    public interface IScadaAxisSource
    {
        /// <summary>轴表拓扑版本（增删/改名/启停时递增；用于面板判断"候选要不要重建"）</summary>
        int Version { get; }

        /// <summary>按稳定身份取轴的身份与可用性；找不到（轴已被删）返回 false</summary>
        bool TryGetInfo(Guid axisId, out ScadaAxisInfo info);

        /// <summary>
        /// 按稳定身份取量值快照。轴存在但离线/未轮询到时返回 true 且
        /// <see cref="ScadaAxisSample.IsValid"/> 为 false；轴根本不存在返回 false。
        /// </summary>
        bool TryGetSample(Guid axisId, out ScadaAxisSample sample);

        /// <summary>
        /// 轴表拓扑变化（可能在后台线程触发）。面板通常不必订阅它——
        /// 它在节拍里按 <see cref="Version"/> 判断是否重建候选即可。
        /// </summary>
        event EventHandler? Changed;
    }
}
