using System;

namespace VisionMaster.Models
{
    /// <summary>
    /// 数组元素写入能力：把"改数组里的一个元素"从 <see cref="IWritableVariable"/> 里分出来。
    ///
    /// 为什么不直接用 <see cref="IWritableVariable.TryWrite"/> 写整个数组：
    /// 那个入口的语义是"把整组值下发给设备"，网络变量会整段覆盖；用它写单个元素，
    /// 调用方必须自己读镜像 → 改一项 → 整组写回，镜像一旦过期就会覆盖掉别的元素。
    /// 所以元素写是一个独立能力：支持就地写的变量源（本地数组）实现它，
    /// 不支持的变量源（网络数组）明确拒绝，由上层给出可行动的原因。
    /// </summary>
    public interface IArrayElementWritable
    {
        /// <summary>
        /// 写入数组的一个元素。成功返回 true；失败返回 false 并给出可直接展示的中文原因。
        /// </summary>
        /// <param name="index">元素下标（0 基）</param>
        /// <param name="value">目标值（允许文本，由实现按元素类型解析）</param>
        bool TryWriteElement(int index, object? value, out string? error);
    }
}
