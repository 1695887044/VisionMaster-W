using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Interfaces
{
    /// <summary>
    /// 一条轴绑定关系：轴名 ↔（板卡 + 轴号）。
    ///
    /// 【它在 URI 里的位置】流程、插件、点位表、凸轮表眼里只有 <see cref="Name"/>；
    /// <see cref="CardId"/> 与 <see cref="AxisIndex"/> 是**内部事实**，只在最终下发命令的那一刻被展开。
    ///
    /// 【为什么是不可变快照】读它的可能是**流程线程**（插件解析轴名准备下发），
    /// 写它的是 **UI 线程**（用户改映射表）。线程各持一份不可变快照就不会读到"改到一半"的状态。
    /// 机械参数不走这里 —— <see cref="Mapping"/> 是活引用，读的是方案里那份真实对象。
    /// </summary>
    public sealed class MotionAxisBinding
    {
        public MotionAxisBinding(string name, Guid cardId, string cardCaption, string cardAddress,
                                 int axisIndex, AxisMapping mapping)
        {
            Name = name;
            CardId = cardId;
            CardCaption = cardCaption;
            CardAddress = cardAddress;
            AxisIndex = axisIndex;
            Mapping = mapping;
        }

        /// <summary>轴名（**全局唯一**，对外唯一标识）</summary>
        public string Name { get; }

        /// <summary>所属板卡的内部稳定身份（寻址一律用它，不用地址 —— 见 <see cref="CardAddress"/> 的说明）</summary>
        public Guid CardId { get; }

        /// <summary>板卡显示名（仅用于错误文案与诊断，不作键值）</summary>
        public string CardCaption { get; }

        /// <summary>
        /// 板卡连接地址（仅用于**展示**）。
        ///
        /// 为什么不用它寻址：地址在方案里并不保证唯一，一旦重复，
        /// "按地址取设备"会只命中先注册的那一张 —— 现场表现就是
        /// "命令发给 A 卡、动的是 B 卡"。历史包里流程步骤按地址引用卡，这是已知风险点。
        /// </summary>
        public string CardAddress { get; }

        /// <summary>卡内物理轴号（0 基）</summary>
        public int AxisIndex { get; }

        /// <summary>该轴的完整配置（脉冲当量 / 软限位 / 是否启用）——活引用，读它即得最新值</summary>
        public AxisMapping Mapping { get; }

        /// <summary>该轴是否启用（未接电机的轴被关掉后不应被任何调用方命中）</summary>
        public bool Enabled => Mapping.Enabled;

        /// <summary>「卡名 / 轴名」的可读标签（日志与错误文案用）</summary>
        public string QualifiedName => $"{CardCaption}/{Name}";

        public override string ToString() => $"{Name} → 卡#{CardId:B} 轴{AxisIndex}";
    }

    /// <summary>轴注册表的错误码（每一档都对应一句能让人直接动手修的中文提示）</summary>
    public enum MotionAxisError
    {
        None = 0,
        NameEmpty,
        NameTooLong,
        NameTaken,
        CardNotFound,
        AxisIndexInvalid,
        AxisIndexOccupied,
        AxisNotFound,
        NotInitialized,
    }

    /// <summary>
    /// 注册表操作的结果。返回错误而**不抛异常** —— 见文件末尾的异常处理策略说明。
    /// </summary>
    public readonly struct MotionAxisResult
    {
        private MotionAxisResult(bool success, MotionAxisError code, string message, MotionAxisBinding? binding)
        {
            Success = success;
            Code = code;
            Message = message;
            Binding = binding;
        }

        public bool Success { get; }
        public MotionAxisError Code { get; }
        public string Message { get; }
        public MotionAxisBinding? Binding { get; }

        public static MotionAxisResult Ok(MotionAxisBinding binding, string message = "")
            => new(true, MotionAxisError.None, message, binding);

        public static MotionAxisResult Ok(string message = "")
            => new(true, MotionAxisError.None, message, null);

        public static MotionAxisResult Fail(MotionAxisError code, string message)
            => new(false, code, message, null);
    }

    /// <summary>建索引时发现的**既有**问题（历史数据里的重复/冲突，用于加载期体检，不是操作时报错）</summary>
    public sealed class MotionAxisProblem
    {
        public MotionAxisProblem(MotionAxisError kind, string message)
        {
            Kind = kind;
            Message = message;
        }

        public MotionAxisError Kind { get; }
        public string Message { get; }

        public override string ToString() => Message;
    }

    /// <summary>
    /// 轴注册表：**全局唯一轴名 →（板卡 + 轴号）**的唯一权威。
    ///
    /// ── 职责边界（这一节比接口本身更重要）──
    ///
    /// 【管】只管**身份与其绑定关系**：名字有没有重、名字指向哪张卡的哪个物理轴号。
    ///
    /// 【不管】
    ///   ① 机械参数（脉冲当量 / 软限位 / 回零方式）—— 那些仍归 <see cref="AxisMapping"/>，
    ///      注册表只顺手把 Mapping 递出去。混进来会让"改个轴限位"也要动身份 coordinate。
    ///   ② 设备生命周期与连接 —— 那归 MotionProvider；注册表完全不认识 IMotionDevice，
    ///      这样它能被无设备、无 WPF 的测试直接断言。
    ///   ③ 命令下发 —— 命令要不要发、能不能发归 <c>MotionCommandGate</c>；
    ///      注册表只回答"这个名字是谁"。
    ///   ④ 持久化格式 —— 它是**派生索引**：真身仍是每张卡的 <c>MotionDescriptor.Axes</c>
    ///      （那才跟着 .vms 落盘）。刻意不开第二份存档：两份真身必然在某次保存/加载后分叉，
    ///      而本项目已经吃过这个亏（点位表按轴名做外键、凸轮表按标签做外键，两处都因改名失联）。
    ///
    /// 【线程模型】流程线程会按名字解析轴准备下发，UI 线程在改映射表 ——
    /// 所有读写都过同一把锁，对外一律给出**不可变快照**。
    /// </summary>
    public sealed class MotionAxisRegistry
    {
        private readonly object _gate = new();

        /// <summary>卡片集合的取数口（不直接依赖 SolutionModel：可测、可回收）</summary>
        private readonly Func<IEnumerable<MotionDescriptor>?> _cardsProvider;

        /// <summary>凸轮表的取数口（改名/删轴要级联；可为 null = 不管凸轮）</summary>
        private readonly Func<IEnumerable<MotionCamTable>?>? _camProvider;

        /// <summary>名字（小写）→ 绑定（解析走的唯一索引）</summary>
        private readonly Dictionary<string, MotionAxisBinding> _byName =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>（卡 Id, 轴号）→ 名字（查"这个物理轴被谁占了"）</summary>
        private readonly Dictionary<(Guid CardId, int AxisIndex), string> _bySlot = new();

        public MotionAxisRegistry(
            Func<IEnumerable<MotionDescriptor>?> cardsProvider,
            Func<IEnumerable<MotionCamTable>?>? camProvider = null)
        {
            _cardsProvider = cardsProvider ?? throw new ArgumentNullException(nameof(cardsProvider));
            _camProvider = camProvider;
        }

        #region 索引

        /// <summary>上次建立索引时的方案指纹（精确字符串，不用哈希 —— 哈希碰撞会留下一个陈旧索引）</summary>
        private string _fingerprint = string.Empty;

        private bool _built;

        private List<MotionAxisProblem> _lastProblems = new();

        /// <summary>索引最近一次重建时发现的问题（宿主持久化/日志用它做加载期体检）</summary>
        public IReadOnlyList<MotionAxisProblem> LastProblems
        {
            get { lock (_gate) return _lastProblems.ToList(); }
        }

        /// <summary>
        /// 自刷新：方案一变就重建索引。
        ///
        /// 【为什么必须自动】注册表是**派生索引**（真身在各卡的 <c>MotionDescriptor.Axes</c> 里），
        /// 而那些对象还会被界面**直接改写**（比如在别处改了轴名、换了方案）。
        /// 指望每个调用方"记得先 Reload 一下"，等于把一致性押在所有人都记得上 ——
        /// 忘了的那次就是"解析到一根已经不存在的轴"。
        /// 所以每个公开方法进来都先过这里，调用方不必知道它的存在。
        ///
        /// 【代价】指纹是全表扫描（几十根轴），每个操作一次；锁内完成，不可见。
        /// </summary>
        private void EnsureFresh()
        {
            lock (_gate)
            {
                var fingerprint = ComputeFingerprintLocked();
                if (_built && string.Equals(fingerprint, _fingerprint, StringComparison.Ordinal)) return;

                _fingerprint = fingerprint;
                _lastProblems = RebuildLocked();
                _built = true;
                Version++;
            }
        }

        /// <summary>方案指纹：卡 Id + 每根轴的（逻辑名, 轴号）。名字/轴号任一变化都能被捕捉到</summary>
        private string ComputeFingerprintLocked()
        {
            var builder = new System.Text.StringBuilder();
            foreach (var card in EnumerateCards())
            {
                builder.Append('C').Append(card.Id.ToString("N"));
                foreach (var axis in card.Axes)
                    builder.Append('|').Append(axis.LogicalName).Append('#').Append(axis.PhysicalIndex);
                builder.Append(';');
            }

            return builder.ToString();
        }

        /// <summary>
        /// 强制重建索引。返回发现的**既有问题**（历史里的重名/同卡同号），不抛异常、不静默修正。
        ///
        /// 【为什么重复时不"取最后一个"也不"随机挑"】
        /// 取到哪一个全凭遍历顺序，用户看来就是"今天动的是这根、明天动的是那根"。
        /// 这里是**先到者胜、后来者被影子化**（不进索引、解析不到），并把问题原样报出来 ——
        /// 由宿主在加载方案时提示用户去改，而不是替他悄悄丢掉一行配置。
        /// </summary>
        public IReadOnlyList<MotionAxisProblem> Reload()
        {
            lock (_gate)
            {
                _fingerprint = ComputeFingerprintLocked();
                _lastProblems = RebuildLocked();
                _built = true;
                Version++;
                return _lastProblems.ToList();
            }
        }

        private List<MotionAxisProblem> RebuildLocked()
        {
            var problems = new List<MotionAxisProblem>();

            _byName.Clear();
            _bySlot.Clear();

            foreach (var card in EnumerateCards())
                {
                    foreach (var mapping in card.Axes)
                    {
                        var name = (mapping.LogicalName ?? string.Empty).Trim();

                        if (name.Length == 0)
                        {
                            problems.Add(new MotionAxisProblem(MotionAxisError.NameEmpty,
                                $"卡「{card.Caption}」上有一行轴的逻辑名为空：它不会被任何轴名解析命中，请补一个名字或删掉这一行"));
                            continue;
                        }

                        if (_byName.ContainsKey(name))
                        {
                            var holder = _byName[name];
                            problems.Add(new MotionAxisProblem(MotionAxisError.NameTaken,
                                $"轴名「{name}」重复：卡「{holder.CardCaption}」的轴 {holder.AxisIndex} 与"
                                + $" 卡「{card.Caption}」的轴 {mapping.PhysicalIndex} 同名。"
                                + "当前按前者解析；请改名，否则其中一个永远动不了"));
                            continue;
                        }

                        var slot = (card.Id, mapping.PhysicalIndex);
                        if (_bySlot.TryGetValue(slot, out var occupant))
                        {
                            problems.Add(new MotionAxisProblem(MotionAxisError.AxisIndexOccupied,
                                $"卡「{card.Caption}」的轴号 {mapping.PhysicalIndex} 被「{occupant}」和"
                                + $"「{name}」重复占用：两个逻辑名指向同一个物理轴，运动时互抢"));
                            continue;
                        }

                        if (mapping.PhysicalIndex < 0)
                        {
                            problems.Add(new MotionAxisProblem(MotionAxisError.AxisIndexInvalid,
                                $"卡「{card.Caption}」的轴「{name}」物理轴号是负数（{mapping.PhysicalIndex}）：无法下发命令"));
                            continue;
                        }

                        _byName[name] = new MotionAxisBinding(
                            name, card.Id, card.Caption, card.Address, mapping.PhysicalIndex, mapping);
                        _bySlot[slot] = name;
                    }
                }

            return problems;
        }

        /// <summary>当前索引的版本（每次 <see cref="Reload"/> 或写操作后会变；调用方可据此判断要不要重取快照）</summary>
        public int Version { get; private set; }

        #endregion

        #region 查询

        /// <summary>该名字能否解析到一根轴</summary>
        public bool Contains(string? axisName)
        {
            if (string.IsNullOrWhiteSpace(axisName)) return false;
            EnsureFresh();
            lock (_gate) return _byName.ContainsKey(axisName.Trim());
        }

        /// <summary>
        /// 下一个**全局**可用的轴名（前缀 + 序号）。
        ///
        /// 为什么不能直接用"当前行数"当序号：删过轴、改过名之后行数与占用情况脱钩，
        /// 按行数取名会直接撞名；而"加轴"这种操作一旦撞名就是"这根轴永远不会被流程选中"。
        /// </summary>
        public string NextAvailableName(string prefix = "A")
        {
            EnsureFresh();
            var head = string.IsNullOrWhiteSpace(prefix) ? "A" : prefix;

            lock (_gate)
            {
                for (var i = 0; i < 4096; i++)
                {
                    var candidate = $"{head}{i}";
                    if (!_byName.ContainsKey(candidate)) return candidate;
                }
            }

            return $"{head}{Guid.NewGuid().ToString("N")[..6]}";
        }

        /// <summary>某张卡上下一个可用的物理轴号（0 基）</summary>
        public int NextAvailableAxisIndex(Guid cardId)
        {
            EnsureFresh();

            lock (_gate)
            {
                for (var i = 0; i < 4096; i++)
                {
                    if (!_bySlot.ContainsKey((cardId, i))) return i;
                }
            }

            return 0;
        }

        /// <summary>
        /// 按轴名解析（插件的标准入口）。
        /// 注意：**禁用**的轴也算存在 —— 是否被允许使用由调用方/闸门决定，
        /// 注册表不替调用方做这个决定（否则会出现"明明配了却说没配"的困惑）。
        /// </summary>
        public bool TryResolve(string? axisName, out MotionAxisBinding binding)
        {
            binding = null!;
            if (string.IsNullOrWhiteSpace(axisName)) return false;

            EnsureFresh();
            lock (_gate) return _byName.TryGetValue(axisName.Trim(), out binding!);
        }

        /// <summary>同上，但失败时给出可上界面的中文原因</summary>
        public MotionAxisResult Resolve(string? axisName)
        {
            if (TryResolve(axisName, out var binding)) return MotionAxisResult.Ok(binding);

            return MotionAxisResult.Fail(MotionAxisError.AxisNotFound,
                string.IsNullOrWhiteSpace(axisName)
                    ? "没有指定轴名"
                    : $"没有名为「{axisName.Trim()}」的轴：请到「运动卡设置」确认轴名已配置并启用"
                      + (string.IsNullOrWhiteSpace(HintNames()) ? string.Empty : $"（现有轴：{HintNames()}）"));
        }

        /// <summary>已注册的全部轴（不可变快照，可跨线程安全遍历）</summary>
        public IReadOnlyList<MotionAxisBinding> Snapshot()
        {
            EnsureFresh();
            lock (_gate) return _byName.Values.ToList();
        }

        /// <summary>全部轴名（下拉候选用；已排序，界面顺序稳定）</summary>
        public IReadOnlyList<string> Names()
        {
            EnsureFresh();
            lock (_gate) return _byName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>某张卡上已占用的轴号（"下一个可用轴号"用）</summary>
        public IReadOnlyList<int> AxisIndicesOf(Guid cardId)
        {
            EnsureFresh();
            lock (_gate)
                return _bySlot.Keys.Where(k => k.CardId == cardId).Select(k => k.AxisIndex).ToList();
        }

        /// <summary>名字是否可用（<paramref name="exclude"/> 用于"改自己"时排除自身）</summary>
        public bool IsNameAvailable(string? axisName, string? exclude = null)
        {
            if (string.IsNullOrWhiteSpace(axisName)) return false;
            if (!string.IsNullOrWhiteSpace(exclude)
                && string.Equals(axisName.Trim(), exclude.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;

            EnsureFresh();
            lock (_gate) return !_byName.ContainsKey(axisName.Trim());
        }

        private string HintNames()
        {
            lock (_gate)
                return string.Join("、", _byName.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Take(12));
        }

        #endregion

        #region 注册 / 更新 / 改名 / 移除

        /// <summary>
        /// 注册一根轴（名字 + 卡 + 轴号）。同名、同卡同轴号都被拒绝 —— 默认**绝不抢占**别人的位置。
        /// </summary>
        public MotionAxisResult Register(string? axisName, Guid cardId, int axisIndex)
        {
            var name = (axisName ?? string.Empty).Trim();
            if (name.Length == 0)
                return MotionAxisResult.Fail(MotionAxisError.NameEmpty, "轴名不能为空：它是流程与插件引用轴的唯一标识");

            if (name.Length > MotionAxisValidator.MaxLogicalNameLength)
                return MotionAxisResult.Fail(MotionAxisError.NameTooLong,
                    $"轴名不能超过 {MotionAxisValidator.MaxLogicalNameLength} 个字符");

            EnsureFresh();

            lock (_gate)
            {
                if (_byName.TryGetValue(name, out var taken))
                    return MotionAxisResult.Fail(MotionAxisError.NameTaken,
                        $"轴名「{name}」已存在，属于卡「{taken.CardCaption}」的轴 {taken.AxisIndex}"
                        + " —— 轴名全局唯一，请换一个名字，或先把原来那根改名/删除");

                var card = FindCard(cardId);
                if (card == null)
                    return MotionAxisResult.Fail(MotionAxisError.CardNotFound,
                        $"找不到 ID 为 {cardId:B} 的运动卡：它可能已被删除");

                if (axisIndex < 0)
                    return MotionAxisResult.Fail(MotionAxisError.AxisIndexInvalid,
                        $"物理轴号不能为负（收到 {axisIndex}）");

                if (_bySlot.TryGetValue((cardId, axisIndex), out var occupant))
                    return MotionAxisResult.Fail(MotionAxisError.AxisIndexOccupied,
                        $"卡「{card.Caption}」的轴号 {axisIndex} 已被轴「{occupant}」占用："
                        + "两根逻辑轴指向同一个物理轴会互相打架，请换一个轴号");

                var mapping = new AxisMapping { LogicalName = name, PhysicalIndex = axisIndex };
                card.Axes.Add(mapping);

                Admit(name, card, mapping);
                Version++;

                return MotionAxisResult.Ok(_byName[name], $"轴「{name}」已注册");
            }
        }

        /// <summary>
        /// 更新：把已存在的轴名重新指向另一张卡 / 另一个轴号（换卡换接线走这里）。
        /// 名字不变 —— 换名字走 <see cref="Rename"/>（那里要做级联）。
        /// </summary>
        public MotionAxisResult Update(string? axisName, Guid newCardId, int newAxisIndex)
        {
            var name = (axisName ?? string.Empty).Trim();

            EnsureFresh();

            lock (_gate)
            {
                if (!_byName.TryGetValue(name, out var binding))
                    return MotionAxisResult.Fail(MotionAxisError.AxisNotFound, $"找不到名为「{name}」的轴，无法更新");

                if (newAxisIndex < 0)
                    return MotionAxisResult.Fail(MotionAxisError.AxisIndexInvalid,
                        $"物理轴号不能为负（收到 {newAxisIndex}）");

                var card = FindCard(newCardId);
                if (card == null)
                    return MotionAxisResult.Fail(MotionAxisError.CardNotFound,
                        $"找不到 ID 为 {newCardId:B} 的运动卡：它可能已被删除");

                var slotOwner = _bySlot.TryGetValue((newCardId, newAxisIndex), out var occupant) ? occupant : null;
                if (slotOwner != null && !string.Equals(slotOwner, name, StringComparison.OrdinalIgnoreCase))
                    return MotionAxisResult.Fail(MotionAxisError.AxisIndexOccupied,
                        $"卡「{card.Caption}」的轴号 {newAxisIndex} 已被轴「{slotOwner}」占用");

                // 先从旧卡上摘掉（跨卡移动时不能留一条指向空槽的残Mapping）
                var oldCard = FindCard(binding.CardId);
                if (oldCard != null && !ReferenceEquals(oldCard, card))
                    oldCard.Axes.Remove(binding.Mapping);

                _bySlot.Remove((binding.CardId, binding.AxisIndex));
                ReleaseName(binding.Name);

                binding.Mapping.PhysicalIndex = newAxisIndex;
                if (!ReferenceEquals(card, oldCard) && !card.Axes.Contains(binding.Mapping))
                    card.Axes.Add(binding.Mapping);

                Admit(name, card, binding.Mapping);
                Version++;

                return MotionAxisResult.Ok(_byName[name],
                    $"轴「{name}」已改指向卡「{card.Caption}」的轴 {newAxisIndex}");
            }
        }

        /// <summary>
        /// 改名，并级联三处引用：点位表外键、凸轮表轴标签、自身索引。
        /// 顺序不能改 —— 点位与凸轮都按**旧名**定位，改完了就找不到了。
        /// </summary>
        public MotionAxisResult Rename(string? oldName, string? newName)
        {
            var from = (oldName ?? string.Empty).Trim();
            var to = (newName ?? string.Empty).Trim();

            if (from.Length == 0)
                return MotionAxisResult.Fail(MotionAxisError.NameEmpty, "原轴名为空，无法改名");
            if (to.Length == 0)
                return MotionAxisResult.Fail(MotionAxisError.NameEmpty, "新轴名不能为空");
            if (to.Length > MotionAxisValidator.MaxLogicalNameLength)
                return MotionAxisResult.Fail(MotionAxisError.NameTooLong,
                    $"轴名不能超过 {MotionAxisValidator.MaxLogicalNameLength} 个字符");
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                return MotionAxisResult.Ok($"轴名没有变化（{to}）");

            EnsureFresh();

            lock (_gate)
            {
                if (!_byName.TryGetValue(from, out var binding))
                    return MotionAxisResult.Fail(MotionAxisError.AxisNotFound, $"找不到名为「{from}」的轴，无法改名");

                if (_byName.ContainsKey(to))
                {
                    var taken = _byName[to];
                    return MotionAxisResult.Fail(MotionAxisError.NameTaken,
                        $"轴名「{to}」已存在，属于卡「{taken.CardCaption}」的轴 {taken.AxisIndex} —— 轴名全局唯一");
                }

                var card = FindCard(binding.CardId);
                if (card == null)
                    return MotionAxisResult.Fail(MotionAxisError.CardNotFound,
                        $"找不到 ID 为 {binding.CardId:B} 的运动卡：它可能已被删除");

                // ① 点位表外键（按旧名）
                var movedPoints = card.RenameAxis(binding.Mapping, from, to);

                // ② 凸轮表轴标签（按旧名）
                var movedRefs = MotionCamAxisRefs.RenameAxisLabels(
                    SafeCamTables(),
                    MotionCamAxisRefs.Label(card.Caption, from),
                    MotionCamAxisRefs.Label(card.Caption, to));

                // ③ 自身索引
                ReleaseName(from);
                Admit(to, card, binding.Mapping);
                Version++;

                var detail = movedPoints > 0 || movedRefs > 0
                    ? $"（已同步 {movedPoints} 个点位、{movedRefs} 处凸轮引用）"
                    : string.Empty;

                return MotionAxisResult.Ok(_byName[to], $"轴「{from}」已改名为「{to}」{detail}");
            }
        }

        /// <summary>
        /// 移除一根轴：删映射 + 清它的点位 + 摘掉凸轮表里对它的引用。
        ///
        /// 【为什么必须级联】不级联的话，删掉后再建一根同名轴，
        /// 老的 16 行点位会"复活"在这根新轴上 —— 一条明明不存在配置关系的幽灵数据。
        /// </summary>
        public MotionAxisResult Remove(string? axisName)
        {
            var name = (axisName ?? string.Empty).Trim();

            EnsureFresh();

            lock (_gate)
            {
                if (!_byName.TryGetValue(name, out var binding))
                    return MotionAxisResult.Fail(MotionAxisError.AxisNotFound, $"找不到名为「{name}」的轴，无需删除");

                var card = FindCard(binding.CardId);
                if (card == null)
                {
                    // 卡没了但索引还在：清理干净，别把脏索引留给后面
                    ReleaseName(name);
                    _bySlot.Remove((binding.CardId, binding.AxisIndex));
                    Version++;
                    return MotionAxisResult.Fail(MotionAxisError.CardNotFound,
                        $"轴「{name}」所属的卡已不存在，索引已清理");
                }

                card.Axes.Remove(binding.Mapping);
                card.RemoveAxisPoints(name);

                MotionCamAxisRefs.ClearAxisLabels(
                    SafeCamTables(), MotionCamAxisRefs.Label(card.Caption, name));

                ReleaseName(name);
                _bySlot.Remove((binding.CardId, binding.AxisIndex));
                Version++;

                return MotionAxisResult.Ok($"轴「{name}」及其点位、凸轮引用已移除");
            }
        }

        #endregion

        #region 内部

        private IEnumerable<MotionDescriptor> EnumerateCards()
            => _cardsProvider.Invoke() ?? Enumerable.Empty<MotionDescriptor>();

        private IEnumerable<MotionCamTable> SafeCamTables()
        {
            if (_camProvider == null) return Enumerable.Empty<MotionCamTable>();
            try { return _camProvider.Invoke() ?? Enumerable.Empty<MotionCamTable>(); }
            catch { return Enumerable.Empty<MotionCamTable>(); }
        }

        private MotionDescriptor? FindCard(Guid cardId)
            => EnumerateCards().FirstOrDefault(c => c.Id == cardId);

        private void Admit(string name, MotionDescriptor card, AxisMapping mapping)
        {
            _byName[name] = new MotionAxisBinding(
                name, card.Id, card.Caption, card.Address, mapping.PhysicalIndex, mapping);
            _bySlot[(card.Id, mapping.PhysicalIndex)] = name;
        }

        private void ReleaseName(string name) => _byName.Remove(name);

        #endregion
    }
}
