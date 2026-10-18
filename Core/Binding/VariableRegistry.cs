using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Binding
{
    /// <summary>
    /// <see cref="IVariableRegistry"/> 的默认实现：给 <see cref="ObservableCollection{T}"/> 变量集合
    /// 加一层 Id / Name 双路索引，并跟着集合变化增量维护。
    ///
    /// 设计取舍：
    /// 1) **索引是派生数据，不持有真理**：变量集合（Workspace.GlobalVariables）仍是唯一数据源，
    ///    本类只是它的镜像。因此集合实例被整体替换时（InitializeCommonVariables、方案重载），
    ///    必须经 <see cref="Attach"/> 重挂，否则会"看得见幽灵变量、看不见新变量"。
    /// 2) **读写都加锁**：索引落在 Dictionary 上，读写并发时 Dictionary 内部扩容会直接损坏结构。
    ///    旧实现是每次遍历 ObservableCollection 线性查找——它同样不是线程安全的，只是"坏得慢"。
    ///    锁只保护索引本身，绝不在锁内抛事件（见 <see cref="NotifyRenamed"/>）。
    /// 3) **增量 vs 全量**：Add/Remove 走增量（万级变量逐个灌入时不至于 O(n²)），
    ///    Clear/整体替换走全量重建（这两种动作下增量维护没有收益且容易漏键）。
    /// 4) **两个来源、两张表**：用户变量（<see cref="Attach"/>，会落盘）与系统变量
    ///    （<see cref="AttachSystem"/>，运行态派生量）各占一张 Id/Name 表，查找时**用户优先**。
    ///    为什么不做成"一张合并表"：用户集合的"先出现者赢"是既有且被断言钉住的行为，
    ///    合并后必须让"后加的用户变量反压已有的系统变量"，两套优先级规则会在一张表里打架；
    ///    分成两张表后，合并规则只剩查找时的一句"先用户、再系统"，不会误改既有语义。
    /// </summary>
    public sealed class VariableRegistry : IVariableRegistry
    {
        private readonly object _gate = new();

        private ObservableCollection<IVariable>? _source;
        private ObservableCollection<IVariable>? _systemSource;
        private Dictionary<Guid, IVariable> _byId = new();
        private Dictionary<string, IVariable> _byName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>系统变量索引（第二来源）。与用户表分开存，见类注释 4)</summary>
        private Dictionary<Guid, IVariable> _systemById = new();
        private Dictionary<string, IVariable> _systemByName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>最近一次 Rebuild 发现的问题（只读快照，供宿主做加载期体检）</summary>
        private IReadOnlyList<string> _lastProblems = Array.Empty<string>();

        /// <summary>绑定到变量集合（构造即完成一次全量索引）</summary>
        public VariableRegistry(ObservableCollection<IVariable> source)
        {
            Attach(source);
        }

        public int Count
        {
            get
            {
                lock (_gate)
                    return _byId.Count + _systemById.Count;
            }
        }

        public event EventHandler<VariableRenamedEventArgs>? VariableRenamed;

        /// <summary>
        /// 重挂到另一个变量集合实例（退订旧集合、订阅新集合并全量重建）。
        /// 集合实例不变时是空操作，可安全重复调用。
        /// </summary>
        public void Attach(ObservableCollection<IVariable> source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            lock (_gate)
            {
                if (ReferenceEquals(_source, source))
                    return;

                if (_source != null)
                    _source.CollectionChanged -= OnSourceChanged;

                _source = source;
                _source.CollectionChanged += OnSourceChanged;
            }

            // 重建放在锁外：它自己会加锁，且不需要与"换集合"这一步构成原子操作
            Rebuild();
        }

        /// <inheritdoc />
        public void AttachSystem(ObservableCollection<IVariable>? source)
        {
            lock (_gate)
            {
                // 幂等：同一个实例重复挂接是空操作；传 null 表示摘掉系统源
                if (ReferenceEquals(_systemSource, source))
                    return;

                if (_systemSource != null)
                    _systemSource.CollectionChanged -= OnSystemSourceChanged;

                _systemSource = source;

                if (_systemSource != null)
                    _systemSource.CollectionChanged += OnSystemSourceChanged;
            }

            // 只重建系统表：用户表与它无关（换系统源不该重扫用户集合，
            // 那会在"UI 线程正在改用户集合"时把一次无害的挂接变成枚举异常）
            RebuildSystem();
        }

        public IVariable? FindById(Guid variableId)
        {
            // Guid.Empty 是"无身份"的哨兵（旧工程数据、常量引用），索引里不存在该键
            if (variableId == Guid.Empty)
                return null;

            lock (_gate)
            {
                // 用户变量优先：同名同 Id 的脏数据场景下，行为必须与 AttachSystem 的合并口径一致
                if (_byId.TryGetValue(variableId, out var variable))
                    return variable;

                return _systemById.TryGetValue(variableId, out var system) ? system : null;
            }
        }

        public IVariable? FindByName(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            lock (_gate)
            {
                // 用户变量优先（见类注释 4)）。冲突时系统变量被遮住，
                // 但那种情况在 Rebuild 里已经记过一条诊断，不是静默行为
                if (_byName.TryGetValue(name!, out var variable))
                    return variable;

                return _systemByName.TryGetValue(name!, out var system) ? system : null;
            }
        }

        public IVariable? Resolve(Guid preferredId, string? fallbackName)
            => FindById(preferredId) ?? FindByName(fallbackName);

        public IVariable? ResolveGlobalLink(LinkReference? link)
        {
            if (link == null)
                return null;

            var variable = Resolve(link.TargetVariableId, link.TargetPortName);
            if (variable == null)
                return null;

            // 旧工程自愈：TargetVariableId 为空说明这条连线是"按名"存下来的。
            // 此刻既然按名命中了，就把稳定身份补齐 —— 幂等的一次性动作：
            // 补上之后引用改为按 Id 寻址，此后变量再改名也不会断链（下次保存即落盘）。
            if (link.TargetVariableId != variable.VariableId)
                link.TargetVariableId = variable.VariableId;

            return variable;
        }

        public bool TryRename(IVariable variable, string? newName, out string error)
        {
            error = string.Empty;

            if (variable == null)
            {
                error = "变量不存在，无法改名";
                return false;
            }

            var target = (newName ?? string.Empty).Trim();
            if (target.Length == 0)
            {
                error = "变量名不能为空";
                return false;
            }

            // 幂等：与旧名完全一致（含大小写），什么都不做也算成功
            if (string.Equals(variable.Name, target, StringComparison.Ordinal))
                return true;

            // 能写名才谈得上改名。这里不 switch 具体模型类型：认得接口就够，
            // 将来新增变量源只需实现 IRenameableVariable，本方法无需改动
            if (variable is not IRenameableVariable renameable)
            {
                error = $"变量类型 {variable.GetType().Name} 不允许改名";
                return false;
            }

            // 保留前缀：`Motion.` 归系统变量（运行态派生的轴状态投影）。
            // 放行会造出"用户变量遮住系统变量"的同名冲突，而遮住之后的表现是
            // "轴在动、画面上的数值不变"——现场几乎不可能自己定位到重名。
            // 判据放在"能不能改名"之后：系统变量自己改名时应先报"不允许改名"，
            // 而不是报一个与它的只读语义无关的前缀问题。
            if (SystemVariableCatalog.IsReservedName(target))
            {
                error = $"「{SystemVariableCatalog.NamePrefix}」前缀是系统变量保留前缀，请更换名称";
                return false;
            }

            // 查重与变量管理弹窗同一口径（OrdinalIgnoreCase）：
            // 旧口径允许 Var / var 共存，但索引查找与监视栏匹配都是大小写不敏感的，
            // 两套规则并存必然自相矛盾——改名这里必须堵住。
            // 注意只大小写不同的改名会命中自己，用引用比对放行（"Var" → "var" 是合法操作）
            lock (_gate)
            {
                if (_byName.TryGetValue(target, out var existing) && !ReferenceEquals(existing, variable))
                {
                    error = $"已存在同名变量 [{existing.Name}]，请更换名称";
                    return false;
                }
            }

            var oldName = variable.Name;

            // ① 写模型（Id 恒定不变，所以引用侧按 Id 寻址的连线/监视项不受影响）
            renameable.Name = target;

            // ② 修索引 + ③ 广播（事件由 NotifyRenamed 在锁外抛出）
            NotifyRenamed(variable, oldName);
            return true;
        }

        public void NotifyRenamed(IVariable variable, string? oldName)
        {
            if (variable == null)
                return;

            lock (_gate)
            {
                // 摘掉旧名键：必须验明"这个键确实指向被改名的对象"，
                // 否则会在同名冲突场景下把别人的键误删
                if (
                    !string.IsNullOrEmpty(oldName)
                    && _byName.TryGetValue(oldName!, out var stale)
                    && ReferenceEquals(stale, variable)
                )
                {
                    _byName.Remove(oldName!);
                }

                // 改名不改 Id，正常情况这里是空操作；用 TryAdd 是为与其余两处保持同一套
                // "先出现者赢"规则（重复 Id 的脏数据场景下不因一次改名就换掉解析结果）
                if (variable.VariableId != Guid.Empty && !_byId.TryAdd(variable.VariableId, variable))
                    ReportDuplicateId(variable.VariableId, variable);

                if (!string.IsNullOrEmpty(variable.Name))
                    _byName[variable.Name] = variable;
            }

            // 事件必须在锁外抛出：订阅方做的是改名级联与画面绑定刷新，
            // 会遍历流程模型、可能触达 UI 线程，握着索引锁执行等于把 UI 钉死
            VariableRenamed?.Invoke(this, new VariableRenamedEventArgs(variable, oldName));
        }

        public void Rebuild()
        {
            ObservableCollection<IVariable>? source;
            lock (_gate)
                source = _source;

            var byId = new Dictionary<Guid, IVariable>();
            var byName = new Dictionary<string, IVariable>(StringComparer.OrdinalIgnoreCase);

            // 先建新字典再整体换引用，让读方永远看到"完整的一份"而不是"清空重建的中途"
            if (source != null)
            {
                foreach (var variable in source)
                {
                    if (variable == null)
                        continue;

                    if (variable.VariableId != Guid.Empty && !byId.TryAdd(variable.VariableId, variable))
                        ReportDuplicateId(variable.VariableId, variable);

                    // 同名以集合中靠前者为准（TryAdd 不覆盖）：新增变量时的重名拦截是大小写不敏感的，
                    // 走到这里还重名只可能来自旧方案数据，保持"先出现者赢"这一确定行为
                    if (!string.IsNullOrEmpty(variable.Name))
                        byName.TryAdd(variable.Name, variable);
                }
            }

            lock (_gate)
            {
                _byId = byId;
                _byName = byName;

                // 用户表换了，同名冲突要重新算一遍。**只读系统表的键、不枚举系统集合**——
                // 系统集合由发布器在后台线程改写，这里（通常是 UI 线程）去枚举它就是
                // "集合已修改，无法枚举"。系统表本身的增删由 OnSystemSourceChanged 负责。
                var problems = new List<string>();
                foreach (var name in _systemByName.Keys)
                {
                    if (_byName.ContainsKey(name))
                        problems.Add(
                            $"用户变量「{name}」与系统变量同名，解析一律取用户变量（系统变量被遮住）；请把用户变量改名");
                }

                _lastProblems = problems;
            }
        }

        /// <summary>
        /// 只重建**系统变量表**（用户表不动）。
        ///
        /// 为什么系统集合的变化不走全量 <see cref="Rebuild"/>：系统集合由发布器在**后台线程**上
        /// 改写（轴表变化时 Clear+Add），而全量 Rebuild 要遍历用户集合（UI 线程维护）。
        /// 后台线程去遍历 UI 正在改的 ObservableCollection，就是"集合已修改，无法枚举"。
        /// 这里只碰系统集合（改动它的正是当前线程），再用锁内的 <c>_byName</c> 做一次
        /// 只读的同名冲突查询——不遍历用户表，跨线程安全。
        /// </summary>
        private void RebuildSystem()
        {
            ObservableCollection<IVariable>? systemSource;
            lock (_gate)
                systemSource = _systemSource;

            var systemById = new Dictionary<Guid, IVariable>();
            var systemByName = new Dictionary<string, IVariable>(StringComparer.OrdinalIgnoreCase);

            if (systemSource != null)
            {
                // 撞上"发布器正在改集合"时重试；三次都失败就保留上一版系统索引
                // （宁可少一个刚加的轴，也不让"改运动卡设置"把发布线程打断）
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    systemById.Clear();
                    systemByName.Clear();

                    try
                    {
                        foreach (var variable in systemSource)
                        {
                            if (variable == null)
                                continue;

                            if (variable.VariableId != Guid.Empty
                                && !systemById.TryAdd(variable.VariableId, variable))
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"[VariableRegistry] 系统变量身份 {variable.VariableId} 重复（'{variable.Name}'），索引保留先出现者");
                            }

                            if (!string.IsNullOrEmpty(variable.Name)
                                && !systemByName.TryAdd(variable.Name, variable))
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"[VariableRegistry] 系统变量名「{variable.Name}」重复，索引保留先出现者");
                            }
                        }

                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        if (attempt == 2)
                            return;
                    }
                }
            }

            var problems = new List<string>();

            lock (_gate)
            {
                _systemById = systemById;
                _systemByName = systemByName;

                // ★ 用户变量优先：同名时系统变量被遮住（FindByName 先查用户表）。
                // 不抛异常——脏数据不该让方案打不开；但必须留痕，
                // 否则表现是"轴在动、画面上的数值不变"，现场无从定位。
                foreach (var name in systemByName.Keys)
                {
                    if (_byName.ContainsKey(name))
                        problems.Add(
                            $"用户变量「{name}」与系统变量同名，解析一律取用户变量（系统变量被遮住）；请把用户变量改名");
                }

                _lastProblems = problems;
            }

            // 锁外落诊断：Debug.WriteLine 本身不重，但重建可能被高频调用
            foreach (var problem in problems)
                System.Diagnostics.Debug.WriteLine($"[VariableRegistry] {problem}");
        }

        /// <summary>
        /// 最近一次重建发现的**既有**问题（重名/重复身份，用于宿主加载期体检）。
        /// 只记录、不自动修正：自动替用户改名的后果比"某个变量解析不到"更难解释。
        /// </summary>
        public IReadOnlyList<string> LastProblems
        {
            get
            {
                lock (_gate)
                    return _lastProblems;
            }
        }

        private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Remove:
                    lock (_gate)
                    {
                        if (e.OldItems != null)
                            foreach (IVariable old in e.OldItems)
                                RemoveFromIndex(old);

                        if (e.NewItems != null)
                            foreach (IVariable added in e.NewItems)
                                AddToIndex(added);
                    }
                    break;

                case NotifyCollectionChangedAction.Reset:
                    // Clear() 也走这里：集合已空，重建即为清空
                    Rebuild();
                    break;

                default:
                    // Replace / Move 等罕见动作：不写"部分更新"的半正确逻辑，直接全量重建
                    Rebuild();
                    break;
            }
        }

        /// <summary>
        /// 系统变量集合的变化：与用户集合同构（Add/Remove 增量、其余全量）。
        /// 增量只动系统表——用户表的键不会因此变化，改它反而会让"用户优先"的规则出现瞬时空洞。
        /// </summary>
        private void OnSystemSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Remove:
                    lock (_gate)
                    {
                        if (e.OldItems != null)
                            foreach (IVariable old in e.OldItems)
                                RemoveFromSystemIndex(old);

                        if (e.NewItems != null)
                            foreach (IVariable added in e.NewItems)
                                AddToSystemIndex(added);
                    }
                    break;

                case NotifyCollectionChangedAction.Reset:
                default:
                    // 只重建系统表：这条回调由系统集合自己触发，用户集合可能正被 UI 线程改，
                    // 全量 Rebuild 会去遍历它（见 RebuildSystem 的注释）
                    RebuildSystem();
                    break;
            }
        }

        /// <summary>
        /// 索引写入（须在锁内调用）。
        /// Id 与名字两个键都用 TryAdd（先出现者赢），必须与 <see cref="Rebuild"/> 完全一致：
        /// 否则同一份集合在"增量灌入"与"重建索引"两条路径下会解析出不同对象——
        /// 那种不一致比解析失败更难查（两个入口各给一个答案，都不报错）。
        /// </summary>
        private void AddToIndex(IVariable variable)
        {
            if (variable == null)
                return;

            if (variable.VariableId != Guid.Empty && !_byId.TryAdd(variable.VariableId, variable))
                ReportDuplicateId(variable.VariableId, variable);

            if (!string.IsNullOrEmpty(variable.Name))
                _byName.TryAdd(variable.Name, variable);
        }

        /// <summary>
        /// 重复身份留痕（须在锁内调用）。
        /// 不抛异常：脏数据不该让方案打不开；但也不能静默——静默去重会让"某个变量永远解析不到"
        /// 变成无迹可寻的怪事。Id 唯一性由生成端（VariableFactory / 迁移补发）保证，
        /// 走到这里只可能是手改过的 .vms。
        /// </summary>
        private static void ReportDuplicateId(Guid variableId, IVariable variable)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[VariableRegistry] 检测到重复变量身份 {variableId}（'{variable.Name}'）——"
                + "索引保留集合中靠前者，后者将无法被 Id 解析。方案文件疑似被手工修改。");
        }

        /// <summary>索引摘除（须在锁内调用）。按引用比对，避免误删同键的其它对象</summary>
        private void RemoveFromIndex(IVariable variable)
        {
            if (variable == null)
                return;

            if (
                variable.VariableId != Guid.Empty
                && _byId.TryGetValue(variable.VariableId, out var byId)
                && ReferenceEquals(byId, variable)
            )
            {
                _byId.Remove(variable.VariableId);
            }

            if (
                !string.IsNullOrEmpty(variable.Name)
                && _byName.TryGetValue(variable.Name, out var byName)
                && ReferenceEquals(byName, variable)
            )
            {
                _byName.Remove(variable.Name);
            }

            // 用户变量走了、同名的系统变量还在：查找会自动落回系统表（FindByName 先用户后系统），
            // 不需要在这里补任何键——这正是"两张表 + 查找时合并"相对"一张合并表"的好处：
            // 摘除一侧的键不会留下"另一半被永久遮住"的幽灵状态。
        }

        /// <summary>系统索引写入（须在锁内调用）。规则与 <see cref="AddToIndex"/> 一致</summary>
        private void AddToSystemIndex(IVariable variable)
        {
            if (variable == null)
                return;

            if (variable.VariableId != Guid.Empty && !_systemById.TryAdd(variable.VariableId, variable))
                ReportDuplicateId(variable.VariableId, variable);

            if (string.IsNullOrEmpty(variable.Name))
                return;

            _systemByName.TryAdd(variable.Name, variable);

            // 用户优先：用户表里已有同名键时留痕（不抛、不覆盖）
            if (_byName.ContainsKey(variable.Name))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[VariableRegistry] 用户变量「{variable.Name}」与系统变量同名，解析一律取用户变量；请把用户变量改名");
            }
        }

        /// <summary>系统索引摘除（须在锁内调用）。按引用比对，避免误删同键的其它对象</summary>
        private void RemoveFromSystemIndex(IVariable variable)
        {
            if (variable == null)
                return;

            if (
                variable.VariableId != Guid.Empty
                && _systemById.TryGetValue(variable.VariableId, out var byId)
                && ReferenceEquals(byId, variable)
            )
            {
                _systemById.Remove(variable.VariableId);
            }

            if (
                !string.IsNullOrEmpty(variable.Name)
                && _systemByName.TryGetValue(variable.Name, out var byName)
                && ReferenceEquals(byName, variable)
            )
            {
                _systemByName.Remove(variable.Name);
            }
        }
    }
}
