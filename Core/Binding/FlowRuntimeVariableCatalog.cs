using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Binding
{
    /// <summary>
    /// 一条流程的运行统计快照（宿主产、目录消费）。
    ///
    /// 为什么是 record struct：目录每 200ms 比一次"有没有变化"，
    /// 值语义相等可以直接 <c>SequenceEqual</c>，不必为每个字段手写比较。
    /// </summary>
    public readonly record struct FlowRuntimeSnapshot(
        Guid FlowId,
        string FlowName,
        string Role,
        string State,
        bool IsRunning,
        bool IsDebug,
        bool HasRound,
        double CycleTimeMs,
        double AverageCycleTimeMs,
        long RoundCount,
        long OkCount,
        long NgCount,
        long UnknownCount,
        string CurrentStep,
        int CurrentStepIndex,
        int StepCount,
        string LastRoundAt,
        string LastReason,
        string VerdictSource);

    /// <summary>
    /// 流程运行统计来源（宿主实现）：状态变化时触发 <see cref="Changed"/>。
    /// 与 <see cref="ISystemVariableProvider"/> 的分工：本接口只描述"统计是什么"，
    /// 目录负责把它投影成只读系统变量。
    /// </summary>
    public interface IFlowRuntimeSource
    {
        event EventHandler? Changed;

        IReadOnlyList<FlowRuntimeSnapshot> Snapshot();
    }

    /// <summary>
    /// 流程运行统计系统变量目录：把每条流程投影成
    /// <c>System.Flow.&lt;流程名&gt;.*</c>（父变量 + 16 个字段），供底栏 / SCADA / 脚本只读绑定。
    ///
    /// 三条口径（与运动轴 / 配方目录一致）
    /// ---------
    /// ① <b>只读派生、不落盘</b>：用 <see cref="SystemVariableModel"/>（不实现 IWritableVariable），
    ///    不进 SolutionModel / VariableSnapshots，重启清零。
    /// ② <b>身份确定</b>：变量 Id = SHA1("flow-var|{FlowId:N}|{字段}") 前 16 字节；
    ///    流程改名、重启、换机器都不变，按 Id 绑定的画面不会断。
    /// ③ <b>实例复用</b>：同一流程同一字段复用已建好的模型，只改名/改值；
    ///    重建实例会让订阅了 ValueChanged 的数据泵/画面静默失联。
    ///
    /// 与 <see cref="SystemVariableCatalog"/> 的差别：本目录的实体集合（流程列表）由来源快照决定，
    /// 所以没有单独的 Rebuild；<see cref="Publish"/> 每次按快照增删/复用。
    /// 只有"集合/顺序/名字"变了才触发 <see cref="Changed"/>（宿主据此改集合）；
    /// 单纯值变化由 <see cref="SystemVariableModel.UpdateMirrorValue"/> 自己通知，不刷整表。
    /// </summary>
    public sealed class FlowRuntimeVariableCatalog : ISystemVariableProvider, IDisposable
    {
        /// <summary>系统变量顶层保留前缀（用户变量新建/改名据此拒绝）</summary>
        public const string VariableNamePrefix = "System.Flow.";

        /// <summary>本目录写入的模型都带这个 Owner（宿主"是否已最新"只数自己那一份）</summary>
        public const string OwnerName = "Flow";

        private const string IdKeyPrefix = "flow-var";
        private const string ParentIdKeyPrefix = "flow-dev";
        private const string ParentFieldKey = "$flow";

        private readonly record struct FieldDef(string Key, Type DataType, string Label);

        private static readonly FieldDef[] Fields =
        {
            new("State", typeof(string), "运行状态（运行/暂停/停止/故障）"),
            new("IsRunning", typeof(bool), "是否在运行"),
            new("Role", typeof(string), "流程角色（主任务/工位/回原点/收尾）"),
            new("HasRound", typeof(bool), "是否至少完成过一轮"),
            new("CycleTimeMs", typeof(double), "上一轮节拍（毫秒）"),
            new("AverageCycleTimeMs", typeof(double), "最近若干轮平均节拍（毫秒）"),
            new("RoundCount", typeof(long), "完成轮次"),
            new("OkCount", typeof(long), "OK 数"),
            new("NgCount", typeof(long), "NG 数"),
            new("UnknownCount", typeof(long), "未判定轮次"),
            new("CurrentStep", typeof(string), "当前执行节点（主线程焦点）"),
            new("CurrentStepIndex", typeof(int), "当前节点序号（1 起，0=未知）"),
            new("StepCount", typeof(int), "节点总数"),
            new("LastRoundAt", typeof(string), "最近一轮完成时间"),
            new("LastReason", typeof(string), "最近一轮判定原因"),
            new("VerdictSource", typeof(string), "判定来源（结果输出步骤/结果变量/未配置）"),
        };

        private readonly object _gate = new();
        private readonly IFlowRuntimeSource _source;
        private readonly IVariableRegistry? _registry;

        private Dictionary<(Guid FlowId, string Field), SystemVariableModel> _models = new();
        private Dictionary<Guid, SystemVariableModel> _byId = new();
        private Dictionary<string, SystemVariableModel> _byName = new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<IVariable> _ordered = Array.Empty<IVariable>();

        public FlowRuntimeVariableCatalog(IFlowRuntimeSource source, IVariableRegistry? registry = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _registry = registry;
            _source.Changed += OnSourceChanged;
            Publish();
        }

        /// <inheritdoc />
        public string Name => OwnerName;

        /// <inheritdoc />
        public event EventHandler? Changed;

        /// <inheritdoc />
        public IReadOnlyList<IVariable> Snapshot()
        {
            lock (_gate) return _ordered;
        }

        /// <summary>从来源取一份快照并投影；同值/同结构时只更新值，不刷集合。</summary>
        public void Publish()
        {
            IReadOnlyList<FlowRuntimeSnapshot> snapshots;
            try
            {
                snapshots = _source.Snapshot() ?? Array.Empty<FlowRuntimeSnapshot>();
            }
            catch
            {
                // 来源读失败不该让系统变量集合消失：保留上一拍的值。
                return;
            }

            bool topologyChanged;
            bool renamed = false;

            lock (_gate)
            {
                var desired = snapshots
                    .Where(s => s.FlowId != Guid.Empty)
                    .GroupBy(s => s.FlowId)
                    .Select(g => g.First())
                    .OrderBy(s => s.FlowName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int perFlow = Fields.Length + 1;
                var models = new Dictionary<(Guid, string), SystemVariableModel>(desired.Count * perFlow);
                var byId = new Dictionary<Guid, SystemVariableModel>(desired.Count * perFlow);
                var byName = new Dictionary<string, SystemVariableModel>(desired.Count * perFlow, StringComparer.OrdinalIgnoreCase);
                var ordered = new List<IVariable>(desired.Count * perFlow);

                foreach (var snapshot in desired)
                {
                    string flowName = string.IsNullOrWhiteSpace(snapshot.FlowName)
                        ? snapshot.FlowId.ToString("N")
                        : snapshot.FlowName.Trim();
                    string parentName = VariableNamePrefix + flowName;

                    var fieldModels = new List<IVariable>(Fields.Length);
                    foreach (var field in Fields)
                    {
                        var key = (snapshot.FlowId, field.Key);
                        string name = parentName + "." + field.Key;
                        var model = GetOrCreate(key, name, field, snapshot.FlowId, flowName, ref renamed);
                        models[key] = model;
                        byId[model.VariableId] = model;
                        byName[name] = model;
                        fieldModels.Add(model);
                        SetValue(model, field.Key, snapshot);
                    }

                    var parentKey = (snapshot.FlowId, ParentFieldKey);
                    var parent = GetOrCreateParent(parentKey, parentName, snapshot.FlowId, flowName, fieldModels, ref renamed);
                    models[parentKey] = parent;
                    byId[parent.VariableId] = parent;
                    byName[parentName] = parent;

                    // 顺序：父变量在前，字段按 Fields 顺序
                    ordered.Add(parent);
                    ordered.AddRange(fieldModels);
                }

                topologyChanged = _ordered.Count != ordered.Count
                    || !_models.Keys.ToHashSet().SetEquals(models.Keys);

                _models = models;
                _byId = byId;
                _byName = byName;
                _ordered = ordered;
            }

            if (renamed)
                RefreshRegistryNames();

            // 只有拓扑/名字变化才让宿主重排集合；值变化由模型自己的 INPC 通知。
            if (topologyChanged || renamed)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        private SystemVariableModel GetOrCreate(
            (Guid FlowId, string Field) key,
            string name,
            FieldDef field,
            Guid flowId,
            string flowName,
            ref bool renamed)
        {
            if (_models.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
                {
                    existing.UpdateName(name);
                    renamed = true;
                }
                existing.UpdateDescription($"{flowName} · {field.Label}（只读系统变量）");
                return existing;
            }

            return new SystemVariableModel(
                ComputeVariableId(flowId, field.Key),
                name,
                field.DataType,
                $"{flowName} · {field.Label}（只读系统变量）",
                owner: OwnerName);
        }

        private SystemVariableModel GetOrCreateParent(
            (Guid FlowId, string Field) key,
            string name,
            Guid flowId,
            string flowName,
            IReadOnlyList<IVariable> fields,
            ref bool renamed)
        {
            if (_models.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
                {
                    existing.UpdateName(name);
                    renamed = true;
                }
                existing.UpdateDescription($"流程「{flowName}」运行统计（只读）");
                existing.UpdateFields(fields);
                return existing;
            }

            return new SystemVariableModel(
                ComputeParentVariableId(flowId),
                name,
                typeof(FlowRuntimeSnapshot),
                $"流程「{flowName}」运行统计（只读）",
                fields,
                OwnerName);
        }

        private static void SetValue(SystemVariableModel model, string field, FlowRuntimeSnapshot s)
        {
            switch (field)
            {
                case "State": model.UpdateMirrorValue(s.State ?? string.Empty); break;
                case "IsRunning": model.UpdateMirrorValue(s.IsRunning); break;
                case "Role": model.UpdateMirrorValue(s.Role ?? string.Empty); break;
                case "HasRound": model.UpdateMirrorValue(s.HasRound); break;
                case "CycleTimeMs": model.UpdateMirrorValue(s.CycleTimeMs); break;
                case "AverageCycleTimeMs": model.UpdateMirrorValue(s.AverageCycleTimeMs); break;
                case "RoundCount": model.UpdateMirrorValue(s.RoundCount); break;
                case "OkCount": model.UpdateMirrorValue(s.OkCount); break;
                case "NgCount": model.UpdateMirrorValue(s.NgCount); break;
                case "UnknownCount": model.UpdateMirrorValue(s.UnknownCount); break;
                case "CurrentStep": model.UpdateMirrorValue(s.CurrentStep ?? string.Empty); break;
                case "CurrentStepIndex": model.UpdateMirrorValue(s.CurrentStepIndex); break;
                case "StepCount": model.UpdateMirrorValue(s.StepCount); break;
                case "LastRoundAt": model.UpdateMirrorValue(s.LastRoundAt ?? string.Empty); break;
                case "LastReason": model.UpdateMirrorValue(s.LastReason ?? string.Empty); break;
                case "VerdictSource": model.UpdateMirrorValue(s.VerdictSource ?? string.Empty); break;
            }
        }

        /// <summary>按确定性 Id 取变量（测试/诊断用）。</summary>
        public bool TryGet(Guid variableId, out SystemVariableModel variable)
        {
            lock (_gate) return _byId.TryGetValue(variableId, out variable!);
        }

        /// <summary>按派生名取变量（测试/诊断用）。</summary>
        public bool TryGet(string name, out SystemVariableModel variable)
        {
            lock (_gate) return _byName.TryGetValue(name ?? string.Empty, out variable!);
        }

        private void OnSourceChanged(object? sender, EventArgs e) => Publish();

        public void Dispose() => _source.Changed -= OnSourceChanged;

        private void RefreshRegistryNames()
        {
            try
            {
                _registry?.RefreshSystemNames();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[FlowRuntimeVariableCatalog] 流程改名后刷新系统变量名索引失败：{ex.Message}");
            }
        }

        private static Guid ComputeVariableId(Guid flowId, string field)
        {
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes($"{IdKeyPrefix}|{flowId:N}|{field}"));
            Span<byte> bytes = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(bytes);
            return new Guid(bytes);
        }

        private static Guid ComputeParentVariableId(Guid flowId)
        {
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes($"{ParentIdKeyPrefix}|{flowId:N}"));
            Span<byte> bytes = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(bytes);
            return new Guid(bytes);
        }
    }
}
