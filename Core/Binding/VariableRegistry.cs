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
    ///    为什么不做成"一张合并表"：合并后必须让"后加的用户变量反压已有的系统变量"，
    ///    两套优先级规则会在一张表里打架；分成两张表后，合并规则只剩查找时的一句
    ///    "先用户、再系统"，不会误改既有语义。
    /// 5) **名字表是多值的**：同名变量全部进列表（<b>不再"先出现者赢"</b>），
    ///    <see cref="FindByName"/> 只在唯一命中时返回，歧义交给 <see cref="FindAllByName"/>
    ///    暴露给调用方——多设备同名点位若被静默选一个，会把值读到/写进另一台设备且不报错。
    /// </summary>
    public sealed class VariableRegistry : IVariableRegistry
    {
        private readonly object _gate = new();

        private ObservableCollection<IVariable>? _source;
        private ObservableCollection<IVariable>? _systemSource;
        private Dictionary<Guid, IVariable> _byId = new();

        /// <summary>
        /// 名字 → 同名变量列表（**多值**）。
        /// 为什么不是单值：多设备场景下不同连接允许有同名点位，索引必须能容纳并**暴露**歧义，
        /// 而不是让"先出现者"静默胜出——那会把值读到/写进另一台设备，且不报错。
        /// 正常情况下每个键只有一个元素；超过一个即歧义，<see cref="FindByName"/> 返回 null。
        /// </summary>
        private Dictionary<string, List<IVariable>> _byName = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>系统变量索引（第二来源）。与用户表分开存，见类注释 4)</summary>
        private Dictionary<Guid, IVariable> _systemById = new();

        /// <summary>系统变量名 → 同名变量列表（多值，理由同 <see cref="_byName"/>)</summary>
        private Dictionary<string, List<IVariable>> _systemByName = new(StringComparer.OrdinalIgnoreCase);

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
                if (_byName.TryGetValue(name!, out var users))
                    return users.Count == 1 ? users[0] : null;   // >1 = 歧义，不猜

                if (_systemByName.TryGetValue(name!, out var systems))
                    return systems.Count == 1 ? systems[0] : null;

                return null;
            }
        }

        public IReadOnlyList<IVariable> FindAllByName(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return Array.Empty<IVariable>();

            lock (_gate)
            {
                // 与 FindByName 同一套"用户优先"口径：用户表有同名就只给用户的，
                // 系统表里的同名项此时本来就被遮住，混进来会让提示自相矛盾。
                if (_byName.TryGetValue(name!, out var users) && users.Count > 0)
                    return users.ToArray();

                if (_systemByName.TryGetValue(name!, out var systems) && systems.Count > 0)
                    return systems.ToArray();

                return Array.Empty<IVariable>();
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

            // 保留前缀：`System.` 归系统变量（运行态派生的轴/IO 状态投影）。
            // 放行会造出"用户变量遮住系统变量"的同名冲突，而遮住之后的表现是
            // "轴在动、画面上的数值不变"——现场几乎不可能自己定位到重名。
            // 判据放在"能不能改名"之后：系统变量自己改名时应先报"不允许改名"，
            // 而不是报一个与它的只读语义无关的前缀问题。
            // 与新建变量共用 VariableNameRules 一份规则（两处各写一份就是这次交接里
            // "创建侧漏拦"的成因）。
            if (!VariableNameRules.TryValidateNewName(target, out error))
                return false;

            // 查重与变量管理弹窗同一口径（OrdinalIgnoreCase）：
            // 旧口径允许 Var / var 共存，但索引查找与监视栏匹配都是大小写不敏感的，
            // 两套规则并存必然自相矛盾——改名这里必须堵住。
            // 注意只大小写不同的改名会命中自己，用引用比对放行（"Var" → "var" 是合法操作）
            lock (_gate)
            {
                // 多值索引：列表里只要还有一个"在**同一作用域**内"的实例就算撞名。
                // 2026-10-29：口径从"方案内唯一"改为"同一设备内唯一"——
                // 网络变量只与同连接的网络变量冲突；本地/系统变量仍与任何变量冲突
                //（它们没有设备，按名解析无法区分，见 VariableNameScope 的类注释）。
                if (_byName.TryGetValue(target, out var sameName))
                {
                    foreach (var existing in sameName)
                    {
                        if (ReferenceEquals(existing, variable))
                            continue;
                        if (!VariableNameScope.Conflicts(variable, existing))
                            continue;

                        error = $"已存在同名变量 [{existing.Name}]"
                                + $"（{VariableNameScope.DescribeScope(existing)}），请更换名称";
                        return false;
                    }
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
                // 摘旧名：按引用从多值列表里移除（不能整键删——同名列表里可能还有别的变量，
                // 旧实现"验明该键确实指向被改名对象才删"的意图由引用比对保留）
                if (!string.IsNullOrEmpty(oldName))
                    RemoveNameIndexEntry(_byName, oldName!, variable);

                // 改名不改 Id，正常情况这里是空操作；用 TryAdd 是为与其余两处保持同一套
                // "先出现者赢"规则（重复 Id 的脏数据场景下不因一次改名就换掉解析结果）
                if (variable.VariableId != Guid.Empty && !_byId.TryAdd(variable.VariableId, variable))
                    ReportDuplicateId(variable.VariableId, variable);

                if (!string.IsNullOrEmpty(variable.Name))
                    AddNameIndexEntry(_byName, variable.Name, variable);
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
            var byName = new Dictionary<string, List<IVariable>>(StringComparer.OrdinalIgnoreCase);

            // 先建新字典再整体换引用，让读方永远看到"完整的一份"而不是"清空重建的中途"
            if (source != null)
            {
                foreach (var variable in source)
                {
                    if (variable == null)
                        continue;

                    if (variable.VariableId != Guid.Empty && !byId.TryAdd(variable.VariableId, variable))
                        ReportDuplicateId(variable.VariableId, variable);

                    // 同名**全部**进列表（不再"先出现者赢"）：名字重复时由 FindByName 返回 null
                    // 暴露歧义、由 FindAllByName 给出全部候选。新增变量时的重名拦截是大小写不敏感的，
                    // 走到这里还重名只可能来自旧方案数据或手工编辑过的 .vms
                    if (!string.IsNullOrEmpty(variable.Name))
                        AddNameIndexEntry(byName, variable.Name, variable);
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
            var systemByName = new Dictionary<string, List<IVariable>>(StringComparer.OrdinalIgnoreCase);

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

                            if (!string.IsNullOrEmpty(variable.Name))
                                AddNameIndexEntry(systemByName, variable.Name, variable);
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
                    lock (_gate)
                    {
                        if (e.NewItems != null)
                            foreach (IVariable added in e.NewItems)
                                AddToIndex(added);
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    lock (_gate)
                    {
                        if (e.OldItems != null)
                            foreach (IVariable old in e.OldItems)
                                RemoveFromIndex(old);
                    }

                    // 索引更新完、锁外再释放：释放会广播 ValueChanged，订阅方可能反查注册表。
                    // 变量被移出集合 = 再没有人能按名/Id 找到它，它自己拷贝的图像副本应当当场释放
                    // （一张 500 万彩图 ≈ 15MB，靠 GC 收等于长期占着）。
                    if (e.OldItems != null)
                        foreach (IVariable removed in e.OldItems)
                            VariableValueOwner.Release(removed);
                    break;

                case NotifyCollectionChangedAction.Reset:
                    // Clear() 也走这里：集合已空。旧索引里的用户变量没人再引用，先释放它们的副本再重建。
                    // 注意：集合实例被整体替换（GlobalVariables setter → Attach → Rebuild）不走这里——
                    // 那条路上旧变量可能被搬到新集合里继续用，不能释放。
                    ReleaseIndexedUserVariables();
                    Rebuild();
                    break;

                default:
                    // Replace / Move 等罕见动作：不写"部分更新"的半正确逻辑，直接全量重建
                    Rebuild();
                    break;
            }
        }

        /// <summary>
        /// 释放旧索引里**用户变量**持有的值（Clear 前调用；系统变量是派生只读量，另源、不碰）。
        /// 快照后逐个释放：<see cref="VariableValueOwner.Release"/> 会派发 ValueChanged，
        /// 订阅方在回调里反查本注册表时不能正持锁，也不能看到"释放了一半"的表。
        /// </summary>
        private void ReleaseIndexedUserVariables()
        {
            List<IVariable> previous;
            lock (_gate)
                previous = new List<IVariable>(_byId.Values);

            foreach (var variable in previous)
                VariableValueOwner.Release(variable);
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
        /// 名字进多值列表，必须与 <see cref="Rebuild"/> 完全一致：
        /// 否则同一份集合在"增量灌入"与"重建索引"两条路径下会解析出不同结果——
        /// 那种不一致比解析失败更难查（两个入口各给一个答案，都不报错）。
        /// </summary>
        private void AddToIndex(IVariable variable)
        {
            if (variable == null)
                return;

            if (variable.VariableId != Guid.Empty && !_byId.TryAdd(variable.VariableId, variable))
                ReportDuplicateId(variable.VariableId, variable);

            if (!string.IsNullOrEmpty(variable.Name))
                AddNameIndexEntry(_byName, variable.Name, variable);
        }

        /// <summary>
        /// 名字索引写入（须在锁内调用）：同名全部进列表，**不**去重、**不**覆盖。
        ///
        /// 四条写入路径（<see cref="Rebuild"/>、<see cref="AddToIndex"/>、<see cref="NotifyRenamed"/>、
        /// <see cref="AddToSystemIndex"/>）共用本方法，保证"增量灌入"与"全量重建"给出同一份索引。
        /// 按引用去重只防"同一实例重复写入"（改名时先摘后加不会重复，但幂等重入不该造出两份）。
        /// </summary>
        private static void AddNameIndexEntry(
            Dictionary<string, List<IVariable>> index, string name, IVariable variable)
        {
            if (!index.TryGetValue(name, out var list))
            {
                list = new List<IVariable>(1);
                index[name] = list;
            }

            foreach (var existing in list)
                if (ReferenceEquals(existing, variable))
                    return;

            list.Add(variable);
        }

        /// <summary>
        /// 名字索引摘除（须在锁内调用）。按引用比对，避免误删同键的其它对象；
        /// 列表空了才删键，否则同名列表里别的变量会被一起抹掉。
        /// </summary>
        private static void RemoveNameIndexEntry(
            Dictionary<string, List<IVariable>> index, string name, IVariable variable)
        {
            if (!index.TryGetValue(name, out var list))
                return;

            for (var i = 0; i < list.Count; i++)
            {
                if (!ReferenceEquals(list[i], variable))
                    continue;

                list.RemoveAt(i);
                break;
            }

            if (list.Count == 0)
                index.Remove(name);
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

            if (!string.IsNullOrEmpty(variable.Name))
                RemoveNameIndexEntry(_byName, variable.Name, variable);

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

            AddNameIndexEntry(_systemByName, variable.Name, variable);

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

            if (!string.IsNullOrEmpty(variable.Name))
                RemoveNameIndexEntry(_systemByName, variable.Name, variable);
        }

        /// <inheritdoc />
        public void RefreshSystemNames()
        {
            // 系统变量的名字是**派生量**（轴名变了名字就变），而模型对象是原地复用、不重建的
            // （见 SystemVariableCatalog.Rebuild 的实例复用）。集合没有增删时一个 CollectionChanged
            // 都不会发，增量维护那条路走不到——名字索引就会永久停在旧名上：
            // 新名查不到、旧名一直命中，而且下一拍"名字已一致"还会让重建被短路，不会自愈。
            // 所以"轴改名"那条路必须主动喊一声（调用方见 SystemVariableCatalog.RebuildAxisRenames）。
            RebuildSystem();
        }
    }
}
