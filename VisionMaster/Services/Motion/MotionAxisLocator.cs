using System;
using Core.Interfaces;

namespace VisionMaster.Services.Motion
{
    /// <summary>
    /// 轴定位器：把"按轴名用轴"变成**一次调用**。
    ///
    /// 【为什么它要和 <see cref="MotionAxisRegistry"/> 分开】
    /// 注册表不认识 <c>IMotionDevice</c>（这样它能被无设备、无 WPF 的测试直接断言）；
    /// 但插件最终要的是"设备 + 轴号"，中间那步"按卡 Id 取设备"必然要碰到 IMotionProvider。
    /// 于是拆两层：
    ///   注册表（纯身份） + 定位器（身份 ⇄ 设备的粘合剂）。
    /// 插件拿到的永远是定位器，永远见不到"选卡"这件事。
    ///
    /// 【插件改造前后】
    ///   改造前：<c>Card</c>（地址）+ <c>Axis</c>（轴名）两个端口，配置文件里还存着卡地址 ——
    ///           换一张卡、改一次 IP，流程就得跟着改；
    ///   改造后：只有一个 <c>Axis</c> 端口，<c>TryLocate("上料轴", …)</c> 一把直取。
    /// </summary>
    public sealed class MotionAxisLocator
    {
        private readonly MotionAxisRegistry _registry;
        private readonly IMotionProvider _motions;

        public MotionAxisLocator(MotionAxisRegistry registry, IMotionProvider motions)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
        }

        /// <summary>底层注册表（少数需要遍历全部轴的场景用，如生成下拉候选）</summary>
        public MotionAxisRegistry Registry => _registry;

        /// <summary>
        /// 按轴名定位到"设备 + 轴号"。这是插件唯一需要的东西。
        /// </summary>
        /// <param name="axisName">轴名（对外唯一标识）</param>
        /// <param name="device">所属板卡的运行态设备</param>
        /// <param name="mapping">该轴的完整配置（脉冲当量 / 软限位 / 是否启用）</param>
        /// <param name="error">失败原因（可直接进失败通知的中文文案）</param>
        public bool TryLocate(string? axisName, out IMotionDevice device, out AxisMapping mapping, out string error)
        {
            device = null!;
            mapping = null!;

            var resolved = _registry.Resolve(axisName);
            if (!resolved.Success || resolved.Binding == null)
            {
                error = resolved.Message;
                return false;
            }

            var binding = resolved.Binding;

            // 按卡 Id 取设备，不走地址：
            // 地址在方案里不保证唯一，重复时"按地址取"只会命中先注册的那张。
            if (!_motions.TryGetDevice(binding.CardId, out device!))
            {
                error = $"找不到运动卡「{binding.CardCaption}」的运行态设备："
                        + "请确认该卡已在「运动卡设置」里配置并保存到方案";
                return false;
            }

            if (!binding.Enabled)
            {
                error = $"轴「{binding.Name}」在卡「{binding.CardCaption}」上处于停用状态："
                        + "请到「运动卡设置」把它启用，或换一个轴名";
                device = null!;
                return false;
            }

            mapping = binding.Mapping;
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 定位 + 检查设备是否处于可下发状态（Online）。
        /// 轴有没有使能、回没回零、忙不忙这些闸门不在这里 —— 那归 <see cref="MotionCommandGate"/>。
        /// </summary>
        public bool TryLocateOnline(string? axisName, out IMotionDevice device, out AxisMapping mapping, out string error)
        {
            if (!TryLocate(axisName, out device, out mapping, out error)) return false;

            if (device.State != MotionCardState.Online)
            {
                error = $"运动卡「{device.Descriptor.Caption}」当前不可运动：{device.StateDetail}";
                device = null!;
                mapping = null!;
                return false;
            }

            return true;
        }
    }
}
