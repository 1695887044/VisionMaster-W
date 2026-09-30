using System;
using System.Collections.Generic;
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
    /// 【寻址口径（项目上线前已定死，不要再往回加地址）】
    ///   · 轴级操作 → **只给轴名**。轴名全局唯一，注册表按它一次解出"哪张卡 + 哪个轴号"，
    ///     插件与流程里彻底不存在"选哪张卡""填什么 IP"这件事。
    ///   · 卡级操作（IO 读写端子、停某张卡的全部轴）→ **给卡名**。卡名同样是用户给的身份，
    ///     与 IP/槽位这些物理接线无关。
    ///   地址（IP/槽位）只活在「运动卡设置」里，是驱动连接用的，不进流程。
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

        /// <summary>是否已注入</summary>
        public static bool IsAttached => _registry != null && _motions != null;

        /// <summary>
        /// 卡级操作的取设备入口（IO / 急停这类没有"轴"概念的命令）：**按卡名**取。
        ///
        /// 用卡名而不是地址：地址是物理接线，换网段/换卡就失效；
        /// 卡名是用户在「运动卡设置」里给出的身份，与接线无关。
        /// </summary>
        public static bool TryResolveDevice(string? cardName, out IMotionDevice device, out string error)
        {
            device = null!;

            var name = (cardName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                error = "这是卡级操作（IO / 急停），请在「运动卡」端口选择一张运动卡";
                return false;
            }

            if (_motions == null)
            {
                error = "运动卡服务尚未就绪：请在主程序内运行流程";
                return false;
            }

            if (!_motions.TryGetByName(name, out device!))
            {
                error = $"找不到名为「{name}」的运动卡：请到「运动卡设置」确认卡名，或换一张卡";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 轴级操作的取轴入口：返回"设备 + 轴配置"，调用方不需要知道卡地址与物理轴号。
        /// </summary>
        /// <param name="axisName">轴名（全局唯一，对外唯一标识）</param>
        public static bool TryResolveAxis(
            string? axisName,
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

            if (_motions == null)
            {
                error = "运动卡服务尚未就绪：请在主程序内运行流程";
                return false;
            }

            if (_registry == null)
            {
                error = "轴注册表尚未注入（宿主未完成初始化）：请重启主程序";
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
        /// 取**全部**运行态运动卡（供"停所有卡的全部轴"这类全局命令）。
        ///
        /// 设备集合从注册表里出现过的卡 Id 推出，而不是遍历方案配置：
        /// 注册表里的卡 Id 是"确实解析出轴、能被流程引用"的那些，
        /// 与运行态设备表一一对应，不会出现"配置里有、设备没建起来"的空引用。
        /// </summary>
        public static bool TryGetAllDevices(out IReadOnlyList<IMotionDevice> devices, out string error)
        {
            devices = Array.Empty<IMotionDevice>();

            if (_motions == null || _registry == null)
            {
                error = "运动卡服务尚未就绪：请在主程序内运行流程";
                return false;
            }

            var result = new List<IMotionDevice>();
            var seen = new HashSet<Guid>();

            foreach (var binding in _registry.Snapshot())
            {
                if (!seen.Add(binding.CardId)) continue;

                if (_motions.TryGetDevice(binding.CardId, out var device) && device != null)
                    result.Add(device);
            }

            if (result.Count == 0)
            {
                error = "当前没有任何可用的运动卡：请到「运动卡设置」配置并连接至少一张卡";
                return false;
            }

            devices = result;
            error = string.Empty;
            return true;
        }
    }
}
