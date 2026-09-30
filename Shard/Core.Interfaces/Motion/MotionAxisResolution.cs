using System;
using System.Linq;

namespace Core.Interfaces
{
    /// <summary>
    /// 插件侧的**唯一**取轴入口：给一个轴名，直接拿到"设备 + 轴配置"。
    ///
    /// 【为什么是静态注入】插件是运行期 Assembly.LoadFrom 进来的独立程序集，
    /// 拿不到宿主的 DI 容器。宿主与插件之间要递一个运行期才知道的东西，
    /// 只能走静态 —— 这与既有的 <see cref="StepConfigOptionSource"/>（下拉候选）
    /// 是同一个手法，插件侧不必知道宿主是怎么装配的。
    ///
    /// 【兼容规则（关键，不能改）】
    ///   · <paramref name="cardKey"/> **非空** → 走老路径（按地址取卡 → 卡内找轴名），
    ///     行为与改造前逐字一致。已保存的流程里存着卡地址，
    ///     而且历史方案可能存在跨卡重名 —— 那时"按名字全局解析"会命中错误的那根轴。
    ///     老流程必须按老规则跑，一个字节都不能变。
    ///   · <paramref name="cardKey"/> **为空** → 走全局路径（注册表按轴名解析 → 按卡 Id 取设备）。
    ///     新流程只填轴名即可，插件与流程里彻底不再出现"选哪张卡"这件事。
    /// </summary>
    public static class MotionAxisResolution
    {
        private static MotionAxisRegistry? _registry;
        private static IMotionProvider? _motions;

        /// <summary>由宿主在启动阶段注入一次（见 MotionModule.Initialize）</summary>
        public static void Attach(MotionAxisRegistry registry, IMotionProvider motions)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
        }

        /// <summary>是否已注入（未注入时全局路径不可用，但老路径仍可跑）</summary>
        public static bool IsAttached => _registry != null && _motions != null;

        /// <summary>
        /// 卡级操作的取设备入口（IO / 急停这类没有"轴"概念的命令）。
        /// IO 天然是卡级的，所以这里**必须**给卡 —— 没有轴名可借，也不该去猜。
        /// </summary>
        public static bool TryResolveDevice(string? cardKey, out IMotionDevice device, out string error)
        {
            device = null!;

            if (string.IsNullOrWhiteSpace(cardKey))
            {
                error = "这是卡级操作（IO / 急停），请指定运动卡地址";
                return false;
            }

            if (_motions == null)
            {
                error = "运动卡服务尚未就绪：请在主程序内运行流程";
                return false;
            }

            if (!_motions.TryGetByKey(cardKey.Trim(), out device!))
            {
                error = $"找不到运动卡「{cardKey}」：请确认该卡已在「运动卡设置」里配置并连接";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 轴级操作的取轴入口：返回"设备 + 轴配置"，调用方不需要知道卡地址与物理轴号。
        /// </summary>
        /// <param name="cardKey">运动卡地址。**留空**即走全局按名解析（推荐）；填了则按老规则走（兼容旧流程）</param>
        /// <param name="axisName">轴名（全局唯一，对外唯一标识）</param>
        public static bool TryResolveAxis(
            string? cardKey, string? axisName,
            out IMotionDevice device, out AxisMapping mapping, out string error)
        {
            device = null!;
            mapping = null!;

            var name = (axisName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                error = "没有指定轴名：请在「Axis」端口填轴名（或用下拉选一个），也可由上游变量给出";
                return false;
            }

            return string.IsNullOrWhiteSpace(cardKey)
                ? ResolveByGlobalName(name, out device, out mapping, out error)
                : ResolveByLegacyCard(cardKey.Trim(), name, out device, out mapping, out error);
        }

        /// <summary>全局路径：轴名 →（卡 Id + 轴号）→ 设备</summary>
        private static bool ResolveByGlobalName(
            string name, out IMotionDevice device, out AxisMapping mapping, out string error)
        {
            device = null!;
            mapping = null!;

            if (_motions == null)
            {
                error = "运动卡服务尚未就绪：请在主程序内运行流程";
                return false;
            }

            if (_registry == null)
            {
                // 注册表没注入时**不**退回老路径：老路径需要卡地址，而这里没有 ——
                // 退回只会变成一句"找不到运动卡「」"，比直说原因更难查。
                error = "轴注册表尚未注入（宿主未完成初始化）：请重启主程序；"
                        + "若必须兼容旧流程，请在「Card」端口填上卡地址";
                return false;
            }

            var resolved = _registry.Resolve(name);
            if (!resolved.Success || resolved.Binding == null)
            {
                error = resolved.Message;
                return false;
            }

            var binding = resolved.Binding;

            if (!binding.Enabled)
            {
                error = $"轴「{name}」在卡「{binding.CardCaption}」上处于停用状态："
                        + "请到「运动卡设置」把它启用，或换一个轴名";
                return false;
            }

            if (!_motions.TryGetDevice(binding.CardId, out device!))
            {
                error = $"找不到运动卡「{binding.CardCaption}」的运行态设备："
                        + "请确认该卡已在「运动卡设置」里配置并保存到方案";
                return false;
            }

            mapping = binding.Mapping;
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 老路径：卡地址 → 卡内按名找轴。
        /// 行为与改造前逐字一致（包括错误文案），只为兼容已保存的流程。
        /// </summary>
        private static bool ResolveByLegacyCard(
            string cardKey, string name,
            out IMotionDevice device, out AxisMapping mapping, out string error)
        {
            device = null!;
            mapping = null!;

            if (_motions == null)
            {
                error = "运动卡服务尚未就绪：请在主程序内运行流程";
                return false;
            }

            if (!_motions.TryGetByKey(cardKey, out device!))
            {
                error = $"找不到运动卡「{cardKey}」：请确认该卡已在「运动卡设置」里配置并连接";
                return false;
            }

            var hit = device.Descriptor.Axes.FirstOrDefault(a =>
                a.Enabled && string.Equals(a.LogicalName, name, StringComparison.OrdinalIgnoreCase));

            if (hit == null)
            {
                // 顺手把"这卡上有哪些轴"列出来 —— 现场最常见的原因就是名字抄错，
                // 只说"没有这个轴"会让人以为配置丢了。
                var available = string.Join("、",
                    device.Descriptor.Axes.Where(a => a.Enabled).Select(a => a.LogicalName));

                error = $"运动卡「{device.Descriptor.Caption}」上没有启用名为「{name}」的轴"
                        + (string.IsNullOrEmpty(available) ? "（该卡还没有配置任何轴）" : $"（现有：{available}）");
                return false;
            }

            mapping = hit;
            error = string.Empty;
            return true;
        }
    }
}
