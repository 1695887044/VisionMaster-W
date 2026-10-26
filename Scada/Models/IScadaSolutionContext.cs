using System;

namespace VisionMaster.Scada
{
    /// <summary>方案身份切换事件：旧/新 id 与纪元，用于判断"这次切换要不要撤销会话"。</summary>
    public readonly record struct ScadaSolutionChange(
        Guid? OldId, long OldEpoch, Guid? NewId, long NewEpoch);

    /// <summary>
    /// 当前方案的只读身份面。Web 侧只引用 <c>VM.Scada</c>，通过它拿方案 id / 名称 / 根目录 /
    /// 身份是否已落盘 / 审计标签，以及切换事件。
    /// </summary>
    public interface IScadaSolutionContext
    {
        Guid? SolutionId { get; }
        string SolutionName { get; }
        string? SolutionRoot { get; }
        bool IdentityPersisted { get; }
        long Epoch { get; }
        string AuditTag { get; }
        event Action<ScadaSolutionChange>? SolutionChanged;
    }
}
