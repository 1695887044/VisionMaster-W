using System;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 当前方案身份的只读快照 + 审计标签。切方案时 <see cref="Epoch"/> 递增，
    /// Web 票据以它为唯一边界；<see cref="SolutionChanged"/> 供 Web/协调器撤销会话。
    /// </summary>
    public sealed class SolutionIdentityHolder : IScadaSolutionContext
    {
        private Guid? _solutionId;
        private string _solutionName = string.Empty;
        private string? _solutionRoot;
        private bool _identityPersisted;
        private long _epoch;

        public Guid? SolutionId => _solutionId;
        public string SolutionName => _solutionName;
        public string? SolutionRoot => _solutionRoot;
        public bool IdentityPersisted => _identityPersisted;
        public long Epoch => _epoch;

        public string AuditTag => ScadaAuditSolutionTag.Build(_solutionId, _identityPersisted, _solutionName);

        public event Action<ScadaSolutionChange>? SolutionChanged;

        public void Set(Guid? id, string? name, string? root, bool identityPersisted)
        {
            var change = new ScadaSolutionChange(_solutionId, _epoch, id, _epoch + 1);
            _solutionId = id;
            _solutionName = name ?? string.Empty;
            _solutionRoot = root;
            _identityPersisted = identityPersisted;
            _epoch++;
            SolutionChanged?.Invoke(change);
        }

        public void UpdateRoot(string? root, bool identityPersisted)
        {
            _solutionRoot = root;
            _identityPersisted = identityPersisted;
        }

        public void Reset() => Set(null, null, null, false);
    }
}
