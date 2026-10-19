using System;
using Core.Interfaces;

namespace Plugin.Camera.Hikvision
{
    /// <summary>
    /// <see cref="CameraTriggerMode"/> → GenICam 节点值的纯映射（不碰 SDK）。
    ///
    /// 为什么单独抽一个纯类型：
    /// 1. 驱动与断言共用同一份映射，避免"测试写一套、驱动写另一套"；
    /// 2. 把最容易错的语义钉死——本仓枚举的 0 是"软触发"，GenICam 节点的 0 是"连续采图"，
    ///    两者数值相同、语义相反，禁止按数值直接透传。
    /// </summary>
    internal readonly record struct HikvisionTriggerPlan(
        uint TriggerMode,
        string? TriggerSource,
        string? TriggerActivation,
        float TriggerDelayUs)
    {
        public static HikvisionTriggerPlan From(CameraSettings? settings)
        {
            if (settings == null) return new(0, null, null, 0);

            int line = Math.Clamp(settings.TriggerSourceLine, 0, 3);
            float delay = (float)Math.Max(0, settings.TriggerDelayUs);

            return settings.TriggerMode switch
            {
                CameraTriggerMode.Continuous => new(0, null, null, 0),
                CameraTriggerMode.Software => new(1, "Software", null, delay),
                CameraTriggerMode.RisingEdge => new(1, $"Line{line}", "RisingEdge", delay),
                CameraTriggerMode.FallingEdge => new(1, $"Line{line}", "FallingEdge", delay),
                _ => new(1, "Software", null, delay),
            };
        }
    }
}
