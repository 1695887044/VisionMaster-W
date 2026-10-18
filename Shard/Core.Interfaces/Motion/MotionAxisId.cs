using System;
using System.Security.Cryptography;
using System.Text;

namespace Core.Interfaces
{
    /// <summary>
    /// 轴稳定身份的生成规则。
    ///
    /// 【为什么轴也需要一个 Id】
    /// 轴名（<see cref="AxisMapping.LogicalName"/>）是给人看的标签，用户随时会改；
    /// 而 SCADA 画面绑定、系统变量、将来的外部引用都需要一个"改名不动"的锚点。
    /// 变量侧早就按这个口径做了（<c>IVariable.VariableId</c>），轴侧必须对齐——
    /// 否则轴改名就会把已配好的画面绑定打断，而且**不会报任何错**（静默断链）。
    ///
    /// 【新建轴 vs 历史轴】
    /// · 新建轴：<c>Guid.NewGuid()</c>，随方案落盘；
    /// · 历史轴（旧 .vms 里没有 AxisId）：由 <see cref="ForLegacy"/> 按
    ///   <c>(卡 Id, 物理轴号)</c> 生成**确定性 Guid**。同一份旧方案在任何机器、
    ///   任何次打开都会得到同一个 Id，于是"第一次打开就建好的绑定"在重启后仍然命中。
    ///   注意：历史轴一旦被改到别的物理轴号，Id 会跟着变——这是旧数据没有身份字段时
    ///   唯一可用的稳定依据，属于一次性迁移的代价；迁移后随保存落盘，此后不再变化。
    ///
    /// 【为什么用 SHA1 而不是 <see cref="Guid.NewGuid"/> 的某种变体】
    /// 只需要"同样的输入必得同样的输出"，不承担安全职责；SHA1 在 BCL 里零依赖、
    /// 结果长度正好够取 16 字节。截断碰撞概率对"几十根轴"这个量级可以忽略。
    /// </summary>
    public static class MotionAxisId
    {
        /// <summary>
        /// 为历史轴生成确定性身份。
        /// </summary>
        /// <param name="cardId">轴所属板卡的稳定 Id（<see cref="MotionDescriptor.Id"/>）</param>
        /// <param name="physicalIndex">卡内物理轴号</param>
        public static Guid ForLegacy(Guid cardId, int physicalIndex)
        {
            // 前缀把"轴身份"与将来可能出现的其它确定性 Guid 隔开，避免同一输入在不同语义下撞车。
            string key = $"motion-axis|{cardId:N}|{physicalIndex}";
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));

            // 取前 16 字节。Guid(byte[]) 会按小端解释前三段——这里不追求可读性，
            // 只要求"同输入同输出"，所以不需要重排字节序。
            Span<byte> bytes = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(bytes);
            return new Guid(bytes);
        }
    }
}
