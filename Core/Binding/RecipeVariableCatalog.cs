using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using VisionMaster.Models;

namespace VisionMaster.Binding
{
    /// <summary>
    /// 配方运行态快照：SCADA 只读显示所需的全部字段（一次取齐，避免多次读服务）。
    ///
    /// 放在 Core 而不是直接让目录引用 RecipeService：Core 不依赖 VisionMaster，
    /// 目录只认这个窄接口；实现（<c>RecipeVariableSource</c>）留在宿主层。
    /// </summary>
    public readonly record struct RecipeVariableSnapshot(
        bool HasActiveRecipe,
        string Name,
        string Code,
        int Version,
        int Deviation,
        int EntryCount,
        string Summary,
        string Status);

    /// <summary>
    /// 配方快照来源（宿主实现）：状态变化时触发 <see cref="Changed"/>。
    /// 与 <see cref="ISystemVariableProvider"/> 的分工：本接口只描述"配方是什么"，
    /// 目录负责把它投影成只读系统变量。
    /// </summary>
    public interface IRecipeVariableSource
    {
        event EventHandler? Changed;

        RecipeVariableSnapshot GetRecipeVariableSnapshot();
    }

    /// <summary>
    /// 配方只读系统变量目录：把当前生效配方的名称/编码/版本/偏离/参数摘要
    /// 投影成一组 <c>System.Recipe.*</c> 变量，供 SCADA 文本图元绑定显示。
    ///
    /// 三条口径
    /// ---------
    /// ① **只读**：用 <see cref="SystemVariableModel"/>（不实现 <c>IWritableVariable</c>），
    ///    与运动/通讯系统变量同一语义——写路径会如实拒绝，界面双向绑定误写则静默忽略。
    /// ② **身份确定**：变量 Id = <c>SHA1("recipe-var|{字段}")</c> 前 16 字节，
    ///    同一字段在任何机器、任何次启动都得到同一个 Id，画面绑定重启后仍命中。
    /// ③ **实例复用**：8 个模型在构造时建一次，之后只更新镜像值。
    ///    重建实例会让已绑定的画面静默失联（与运动目录同一条理由）。
    ///
    /// 刷新时机：宿主在配方状态变化（应用/清除/编辑/换方案）时调 <see cref="Publish"/>。
    /// 不做定时轮询——配方值本身不会自己变；偏离数在参数被外部改动后要等下一次
    /// 配方状态变化才更新，这是 v1 的已知边界。
    /// </summary>
    public sealed class RecipeVariableCatalog : ISystemVariableProvider, IDisposable
    {
        /// <summary>提供方名字（SystemVariableHost 诊断用）</summary>
        public const string OwnerName = "Recipe";

        /// <summary>变量名前缀（用户变量新建/改名据此拒绝，避免遮住系统变量）</summary>
        public const string VariableNamePrefix = "System.Recipe.";

        private const string IdKeyPrefix = "recipe-var";

        private readonly IRecipeVariableSource _source;

        private readonly SystemVariableModel _active;
        private readonly SystemVariableModel _name;
        private readonly SystemVariableModel _code;
        private readonly SystemVariableModel _version;
        private readonly SystemVariableModel _deviation;
        private readonly SystemVariableModel _entryCount;
        private readonly SystemVariableModel _summary;
        private readonly SystemVariableModel _status;
        private readonly IReadOnlyList<IVariable> _variables;

        public RecipeVariableCatalog(IRecipeVariableSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));

            _active = Create("Active", typeof(bool), "当前是否有生效配方");
            _name = Create("Name", typeof(string), "当前生效配方名");
            _code = Create("Code", typeof(string), "当前生效配方的产品编码");
            _version = Create("Version", typeof(int), "当前生效配方的内容版本");
            _deviation = Create("Deviation", typeof(int), "当前参数与配方不一致的项数");
            _entryCount = Create("EntryCount", typeof(int), "当前生效配方的参数条数");
            _summary = Create("Summary", typeof(string), "当前生效配方的参数摘要（每行一项）");
            _status = Create("Status", typeof(string), "当前配方状态文本（已生效/偏离/未应用/悬空）");

            _variables = new IVariable[]
            {
                _active, _name, _code, _version, _deviation, _entryCount, _summary, _status,
            };

            _source.Changed += OnSourceChanged;
            Publish();
        }

        /// <inheritdoc />
        public string Name => OwnerName;

        /// <inheritdoc />
        public event EventHandler? Changed;

        /// <inheritdoc />
        public IReadOnlyList<IVariable> Snapshot() => _variables;

        /// <summary>从来源取一份快照并写入 8 个模型（同值自动短路）。</summary>
        public void Publish()
        {
            RecipeVariableSnapshot snapshot;
            try
            {
                snapshot = _source.GetRecipeVariableSnapshot();
            }
            catch
            {
                // 来源读失败不该让系统变量集合消失：保留上一拍的值，只把状态说明改掉。
                snapshot = new RecipeVariableSnapshot(
                    false, string.Empty, string.Empty, 0, 0, 0, string.Empty, "配方状态不可读");
            }

            _active.UpdateMirrorValue(snapshot.HasActiveRecipe);
            _name.UpdateMirrorValue(snapshot.Name ?? string.Empty);
            _code.UpdateMirrorValue(snapshot.Code ?? string.Empty);
            _version.UpdateMirrorValue(snapshot.Version);
            _deviation.UpdateMirrorValue(snapshot.Deviation);
            _entryCount.UpdateMirrorValue(snapshot.EntryCount);
            _summary.UpdateMirrorValue(snapshot.Summary ?? string.Empty);
            _status.UpdateMirrorValue(snapshot.Status ?? string.Empty);

            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void OnSourceChanged(object? sender, EventArgs e) => Publish();

        /// <inheritdoc />
        public void Dispose() => _source.Changed -= OnSourceChanged;

        private static SystemVariableModel Create(string field, Type dataType, string label)
            => new(
                ComputeVariableId(field),
                VariableNamePrefix + field,
                dataType,
                label + "（只读系统变量）",
                owner: OwnerName);

        /// <summary>确定性身份：同一字段在任何机器/任何次启动都相同。</summary>
        private static Guid ComputeVariableId(string field)
        {
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes($"{IdKeyPrefix}|{field}"));
            Span<byte> bytes = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(bytes);
            return new Guid(bytes);
        }
    }
}
