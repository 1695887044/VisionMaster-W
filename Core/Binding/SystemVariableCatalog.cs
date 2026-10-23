using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.Binding
{
    /// <summary>
    /// 系统变量目录：把**运行态派生量**投影成一组只读的 <see cref="IVariable"/>（本批是运动轴状态）。
    ///
    /// 三条口径（与用户变量刻意不同，越界就会出事）
    /// ---------
    /// ① <b>派生索引、不落盘</b>：真身是运动卡的轮询快照（<see cref="ScadaAxisSample"/>）。
    ///    本目录不参与 <c>SolutionModel</c> / <c>VariableSnapshots</c> / <c>.vms</c> 的任何一环，
    ///    也不进「变量管理」的用户清单——它每次启动都从轴表重建，没有"保存"这回事。
    ///    一旦让它落盘，方案里就会存下一份"上次退出时轴在哪"的幽灵数据，
    ///    下次打开看起来"变量有值"，其实是几小时前的位置。
    /// ② <b>身份确定性</b>：变量 Id = <c>SHA1("motion-var|{轴Id:N}|{字段}")</c> 的前 16 字节。
    ///    同一根轴的同一个字段在任何机器、任何次启动都得到同一个 Id，于是按 Id 寻址的
    ///    连线/画面绑定在重启后仍然命中——不需要任何"登记/迁移"动作。
    ///    手法与 <c>Core.Interfaces.MotionAxisId</c> 一致（同样只求"同输入同输出"，
    ///    不承担安全职责，几十根轴这个量级上截断碰撞可以忽略）。
    /// ③ <b>实例必须复用</b>：<see cref="Rebuild"/> 对"同一轴 + 同一字段"复用已建好的模型实例，
    ///    只增删/改名。重建实例会让所有订阅了 <c>ValueChanged</c> 的消费方（数据泵、监视项、
    ///    画面绑定）静默失联——它们手里还攥着旧对象，值永远不会再变。
    ///
    /// 线程
    /// ---------
    /// <see cref="Rebuild"/> / <see cref="Publish"/> 由 <c>MotionStatePublisher</c> 的定时器线程调用
    /// （100ms 一拍），<see cref="TryGet(Guid, out SystemVariableModel)"/> 由 UI 线程调用。
    /// 内部用"整表构造 + 引用交换"：读方拿到的永远是一份完整、此后不再被改动的表，
    /// 只有 <see cref="Variables"/> 集合本身在拓扑变化时才被改写（不是每拍）。
    /// </summary>
    public sealed class SystemVariableCatalog : ISystemVariableProvider
    {
        /// <summary>系统变量顶层保留前缀。用户变量的新建/改名据此拒绝，避免与派生量撞名。</summary>
        public const string NamePrefix = "System.";

        /// <summary>确定性身份的命名空间前缀（把"系统变量"与将来其它确定性 Guid 隔开）</summary>
        private const string IdKeyPrefix = "motion-var";

        /// <summary>父变量（整根轴）的身份前缀：与字段前缀分开，避免与任何字段撞键</summary>
        private const string DeviceIdKeyPrefix = "motion-dev";

        /// <summary>父变量在内部字典里的"字段键"（真实字段名不会以 $ 开头，不会撞）</summary>
        private const string DeviceFieldKey = "$device";

        /// <summary>本目录写入的模型都带这个 Owner（"是否已最新"只数自己那一份）</summary>
        private const string OwnerName = "Motion";

        /// <summary>字段定义：键（进名字与身份哈希）、声明类型、给操作员看的中文名</summary>
        private readonly record struct FieldDef(string Key, Type DataType, string Label);

        /// <summary>
        /// 轴级字段表。顺序即清单顺序（位置、速度在前——现场最常看的就是这两个）。
        /// 增删字段只影响系统变量目录，不动任何既有契约。
        /// </summary>
        private static readonly FieldDef[] Fields =
        {
            new("Position", typeof(double), "位置 (mm)"),
            new("Velocity", typeof(double), "速度 (mm/s)"),
            new("Moving", typeof(bool), "运动中"),
            new("InPosition", typeof(bool), "已到位"),
            new("Enabled", typeof(bool), "已使能"),
            new("Alarm", typeof(bool), "报警中"),
            new("AlarmMessage", typeof(string), "报警信息"),
            new("PositiveLimit", typeof(bool), "正限位"),
            new("NegativeLimit", typeof(bool), "负限位"),
            new("Origin", typeof(bool), "原点信号"),
            new("EmergencyStop", typeof(bool), "急停信号"),
            new("StatusText", typeof(string), "状态文本"),
        };

        private const string FieldPosition = "Position";
        private const string FieldVelocity = "Velocity";
        private const string FieldMoving = "Moving";
        private const string FieldInPosition = "InPosition";
        private const string FieldEnabled = "Enabled";
        private const string FieldAlarm = "Alarm";
        private const string FieldAlarmMessage = "AlarmMessage";
        private const string FieldPositiveLimit = "PositiveLimit";
        private const string FieldNegativeLimit = "NegativeLimit";
        private const string FieldOrigin = "Origin";
        private const string FieldEmergencyStop = "EmergencyStop";
        private const string FieldStatusText = "StatusText";

        private readonly object _gate = new();

        /// <summary>三张只读表：整表交换，交换后不再改动（读方拿到引用即可安全使用）</summary>
        private Dictionary<(Guid AxisId, string Field), SystemVariableModel> _models = new();
        private Dictionary<Guid, SystemVariableModel> _byId = new();
        private Dictionary<string, SystemVariableModel> _byName = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<Guid, string> _axisNames = new();
        private Dictionary<Guid, string> _axisCards = new();

        /// <summary>当前投影的**不可变快照**（换表时整份替换，之后不再改动）</summary>
        private IReadOnlyList<IVariable> _ordered = Array.Empty<IVariable>();

        /// <summary>
        /// 构造目录。
        /// </summary>
        /// <param name="workspace">
        /// 工作区：投影结果直接写进它的 <c>SystemVariables</c>（变量注册表挂的正是那一份）。
        ///
        /// 为什么收工作区而不是收一个集合：容器按构造函数解析，若签名是"任意集合"，
        /// 未注册时 Unity 会自己 new 一个空的 ObservableCollection 塞进来 ——
        /// 投影全部写进一个没人读的孤儿集合，而且**不报任何错**（表现是"系统变量一个都查不到"）。
        /// 收工作区就没有这个可能：它一定解析得到，且一定是注册表挂的那一份。
        /// </param>
        /// <param name="registry">
        /// 变量解析索引（可选）。给了它，轴改名之后才会去修名字索引——
        /// 系统变量的名字是派生量，改名时集合不增不减，光靠 <c>CollectionChanged</c>
        /// 永远走不到（详见 <see cref="RefreshRegistryNames"/>）。
        /// 不给也能用，只是改名后要等下一次增删轴才会重扫。
        /// </param>
        public SystemVariableCatalog(IWorkspaceManager workspace, IVariableRegistry? registry = null)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            Variables = workspace.SystemVariables;
            _registry = registry;
        }

        private SystemVariableHost? _host;

        /// <summary>接到统一宿主：接上之后本目录不再直接写集合，只产快照并通知 Host 刷新。</summary>
        public void AttachToHost(SystemVariableHost host)
        {
            _host = host;
            if (host == null) return;
            host.Register(this);
        }

        /// <summary>
        /// 变量解析索引（可为 null）。
        ///
        /// 为什么目录要攥着注册表：名字索引在注册表里（<c>_systemByName</c>），
        /// 而"轴改名"这个事实只有目录知道（它比对得上一次的名字）。
        /// 让注册表反过来订阅每个模型的名字变更，等于给几百个模型各挂一个处理器；
        /// 由目录在"确实改了名"那一刻喊一声，代价只有一次全表重扫。
        /// </summary>
        private readonly IVariableRegistry? _registry;

        /// <summary>
        /// 系统变量集合（与 <c>IWorkspaceManager.SystemVariables</c> 是同一个实例）。
        ///
        /// ⚠ <b>只许"拍快照"用，不要直接绑到常驻 UI 列表</b>：它的改写发生在发布器线程上
        /// （<see cref="Rebuild"/> 里 Clear+Add），WPF 集合绑定要求 UI 线程。
        /// 选择弹窗的做法是每次打开时复制一份（<c>OnDialogOpened</c>），不在打开期间持有它。
        /// 值本身不靠这个集合通知——每个 <see cref="SystemVariableModel"/> 自己实现了 INPC，
        /// 常驻界面请绑模型（<see cref="TryGet(Guid, out SystemVariableModel)"/> 取到的那一个）。
        /// </summary>
        public ObservableCollection<IVariable> Variables { get; }

        /// <summary>提供方名字（SystemVariableHost 诊断用）</summary>
        public string Name => "Motion";

        /// <summary>提供方快照：运动轴状态投影（不可变，换表时整份替换）。</summary>

        /// <summary>
        /// 按轴表重建投影（幂等、便宜：没变化时一次比较就返回）。
        ///
        /// 每拍都可调用：轴的新增/删除/改名由它自动跟进；同一根轴同一字段的模型实例**复用**，
        /// 只有真的增删了轴才会改动 <see cref="Variables"/> 集合。
        /// </summary>
        public void Rebuild(IReadOnlyList<MotionAxisBinding> bindings)
        {
            if (bindings == null)
                return;

            // 期望集合：空身份的轴跳过（它的确定性 Id 会与所有空身份轴撞在一起），
            // 同一轴重复出现时先到者胜（与 MotionAxisRegistry 的口径一致）。
            var desired = new List<(Guid AxisId, MotionAxisBinding Binding)>(bindings.Count);
            var seen = new HashSet<Guid>();

            foreach (var binding in bindings)
            {
                if (binding == null) continue;

                var axisId = binding.AxisId;
                if (axisId == Guid.Empty) continue;
                if (!seen.Add(axisId)) continue;

                desired.Add((axisId, binding));
            }

            // 按轴名排序，让清单顺序稳定（不随注册表内部遍历顺序抖动）
            desired.Sort((a, b) => string.Compare(a.Binding.Name, b.Binding.Name, StringComparison.OrdinalIgnoreCase));

            Dictionary<(Guid, string), SystemVariableModel> previous;
            Dictionary<Guid, string> previousAxisNames;
            lock (_gate)
            {
                if (IsUpToDateLocked(desired))
                    return;

                previous = _models;
                previousAxisNames = _axisNames;
            }

            int perAxis = Fields.Length + 1;   // 父变量（整根轴）+ 12 个字段

            var models = new Dictionary<(Guid, string), SystemVariableModel>(desired.Count * perAxis);
            var byId = new Dictionary<Guid, SystemVariableModel>(desired.Count * perAxis);
            var byName = new Dictionary<string, SystemVariableModel>(desired.Count * perAxis, StringComparer.OrdinalIgnoreCase);
            var axisNames = new Dictionary<Guid, string>(desired.Count);
            var axisCards = new Dictionary<Guid, string>(desired.Count);
            var ordered = new List<IVariable>(desired.Count * perAxis);

            // ★ 有没有**改名**（不是增删）：实例复用只改模型上的 Name，集合不增不减 →
            //   一个 CollectionChanged 都不会发。变量注册表的名字索引就会永久停在旧名上
            //   （新名查不到、旧名一直命中，且"名字已一致"会让后续重建被短路，不会自愈）。
            //   所以这里必须单独记账，重建完主动让注册表重扫一次系统名表。
            bool renamed = false;

            foreach (var (axisId, binding) in desired)
            {
                var axisName = NormalizeAxisName(binding.Name);
                var cardName = binding.CardCaption ?? string.Empty;

                if (previousAxisNames.TryGetValue(axisId, out var previousName)
                    && !string.Equals(previousName, axisName, StringComparison.Ordinal))
                {
                    renamed = true;
                }

                axisNames[axisId] = axisName;
                axisCards[axisId] = cardName;

                // ① 先建字段：沿用既有名字与确定性 Id（老方案里按 Id/名字绑好的连线零迁移）
                var fieldModels = new List<IVariable>(Fields.Length);
                foreach (var field in Fields)
                {
                    var key = (axisId, field.Key);
                    var variableId = ComputeVariableId(axisId, field.Key);
                    var newName = BuildName(axisName, field.Key);

                    // ★ 实例复用：同一轴同一字段的模型必须沿用原对象，否则订阅方全部失联
                    if (previous.TryGetValue(key, out var model))
                    {
                        // 轴改名 → 变量名与说明跟着变（Id 不变）
                        if (!string.Equals(model.Name, newName, StringComparison.Ordinal))
                            renamed = true;

                        model.UpdateName(newName);
                        model.UpdateDescription(BuildDescription(axisName, field.Label));
                    }
                    else
                    {
                        model = new SystemVariableModel(
                            variableId,
                            newName,
                            field.DataType,
                            BuildDescription(axisName, field.Label),
                            owner: OwnerName);
                    }

                    models[key] = model;
                    byId[model.VariableId] = model;
                    byName[model.Name] = model;
                    fieldModels.Add(model);
                }

                // ② 再建/更新父变量（整根轴，类型 MotionAxis）
                //    Fields 只在构造时给：同一轴的字段集合恒定，复用父实例时它手里那 12 个
                //    子实例也来自 previous（同一批复用对象），不需要替换列表。
                var deviceKey = (axisId, DeviceFieldKey);
                var deviceName = BuildDeviceName(axisName);
                var deviceDescription = BuildDeviceDescription(axisName, cardName);

                if (previous.TryGetValue(deviceKey, out var deviceModel))
                {
                    if (!string.Equals(deviceModel.Name, deviceName, StringComparison.Ordinal))
                        renamed = true;

                    deviceModel.UpdateName(deviceName);
                    deviceModel.UpdateDescription(deviceDescription);

                    // 字段表将来若增删，复用父实例必须换一份新列表（否则父行还指着旧字段实例）
                    deviceModel.UpdateFields(fieldModels);
                }
                else
                {
                    deviceModel = new SystemVariableModel(
                        ComputeVariableId(DeviceIdKeyPrefix, axisId, null),
                        deviceName,
                        typeof(MotionAxis),
                        deviceDescription,
                        fieldModels,
                        OwnerName);
                }

                models[deviceKey] = deviceModel;
                byId[deviceModel.VariableId] = deviceModel;
                byName[deviceModel.Name] = deviceModel;

                // 顺序即清单顺序：父在前、字段在后（界面按这个顺序展开）
                ordered.Add(deviceModel);
                ordered.AddRange(fieldModels);
            }

            // 整表交换：先让读方看到完整的新表
            lock (_gate)
            {
                _models = models;
                _byId = byId;
                _byName = byName;
                _axisNames = axisNames;
                _axisCards = axisCards;
                _ordered = ordered;
            }

            // 集合改写放在锁外：它会同步回调订阅方（变量注册表按增量维护索引），
            // 持着本类的锁执行等于把两把锁串成一条链。
            if (_host != null)
            {
                // 托管模式：集合由 SystemVariableHost 统一合并（运动 + IO + 未来来源）。
                // 目录自己不再碰集合，只通知宿主刷新——否则会与 IO 目录抢同一个集合。
                RaiseChanged();
            }
            else
            {
                ApplyToCollection(ordered);
            }

            // 改名之后要修名字索引。顺序在 ApplyToCollection 之后：那一步可能触发
            // Add/Remove 增量，增量按**当前** Name 建键（已经是新名），再重扫一次是幂等的。
            if (renamed)
                RefreshRegistryNames();
        }

        /// <summary>
        /// 轴改名后修一次变量注册表的名字索引（没接注册表时是空操作）。
        ///
        /// 与 <see cref="Variables"/> 的集合事件分工：集合事件负责"谁在库里"（增删），
        /// 这里负责"叫什么名字"（派生名变化）。两者都不能少：
        /// 少了前者，新轴的变量永远查不到；少了后者，改名的轴永远只能用旧名查。
        /// </summary>
        private void RefreshRegistryNames()
        {
            try
            {
                _registry?.RefreshSystemNames();
            }
            catch (Exception ex)
            {
                // 名字索引修不好不该让发布器线程挂掉：值还在写、按 Id 寻址照常，
                // 只有"按新名查找"这一条路暂时失效。记一行，下一拍改名判定会再次触发重试。
                System.Diagnostics.Debug.WriteLine(
                    $"[SystemVariableCatalog] 轴改名后刷新系统变量名索引失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 把投影结果同步进 <see cref="Variables"/>：只做增删移，<b>不清空重建</b>。
        ///
        /// 为什么不用 Clear + Add：Clear 会发一次 Reset，变量注册表据此把系统索引整个清空，
        /// 在随后逐条 Add 补齐之前存在一个"系统变量全查不到"的窗口；界面若正绑着这个集合，
        /// 还会看到列表整体闪一下。增删移没有这个窗口，也只动真正变了的项。
        /// </summary>
        private void ApplyToCollection(IReadOnlyList<IVariable> ordered)
        {
            var target = new HashSet<IVariable>(ordered);

            // ① 只摘掉"本目录"里不在新投影中的；其它来源（通讯/相机）的变量由各自目录管理，
            //    这里不能碰（否则改一次运动卡设置就会把别家的变量全删光）。
            for (var i = Variables.Count - 1; i >= 0; i--)
            {
                if (Variables[i] is SystemVariableModel { Owner: OwnerName } && !target.Contains(Variables[i]))
                    Variables.RemoveAt(i);
            }

            // ② 按目标顺序就位：缺的插入、错位的移动
            for (var i = 0; i < ordered.Count; i++)
            {
                var current = Variables.IndexOf(ordered[i]);
                if (current < 0)
                    Variables.Insert(Math.Min(i, Variables.Count), ordered[i]);
                else if (current != i)
                    Variables.Move(current, i);
            }
        }

        /// <summary>
        /// 把一根轴的最新快照写进对应的 12 个模型（只写有变化的字段）。
        ///
        /// <see cref="ScadaAxisSample.IsValid"/> 为 false 时**只写"为什么不知道"**两格
        /// （<c>AlarmMessage</c> / <c>StatusText</c>），其余字段保留上一拍的已知值：
        /// 清 0 会把"不知道"伪装成"停在原点"，置 false 会把"不知道"伪装成"没在动/没报警"——
        /// 这两类假信息都会让现场按错误前提做判断（与 <c>ScadaAxisSample.Invalid</c> 同一条口径）。
        /// </summary>
        public void Publish(Guid axisId, ScadaAxisSample sample)
        {
            if (axisId == Guid.Empty)
                return;

            Dictionary<(Guid, string), SystemVariableModel> models;
            Dictionary<Guid, string> axisNames;
            Dictionary<Guid, string> axisCards;
            lock (_gate)
            {
                models = _models;
                axisNames = _axisNames;
                axisCards = _axisCards;
            }

            // 该轴不在目录里（已被删除 / 未登记）：静默忽略，不补建——
            // 补建会让"删掉的轴"因为一次迟到的快照又长回来
            if (!models.ContainsKey((axisId, FieldPosition)))
                return;

            // 父变量（整根轴）与 12 个字段写的是同一份快照，顺序不影响结果；
            // 先写父变量，让"整根轴"的消费者与字段消费者在同一拍内看到同一份数据。
            WriteDeviceSnapshot(models, axisId,
                axisNames.TryGetValue(axisId, out var axisName) ? axisName : "未命名轴",
                axisCards.TryGetValue(axisId, out var cardName) ? cardName : string.Empty,
                sample);

            if (!sample.IsValid)
            {
                var reason = string.IsNullOrWhiteSpace(sample.Error) ? "状态未知（运动卡未连接）" : sample.Error!;
                Write(models, axisId, FieldAlarmMessage, reason);
                Write(models, axisId, FieldStatusText, reason);
                return;
            }

            Write(models, axisId, FieldPosition, sample.PositionMm);
            Write(models, axisId, FieldVelocity, sample.VelocityMmPerS);
            Write(models, axisId, FieldMoving, sample.Moving);
            Write(models, axisId, FieldInPosition, sample.InPosition);
            Write(models, axisId, FieldEnabled, sample.Enabled);
            Write(models, axisId, FieldAlarm, sample.Alarm);
            // 报警文案：ScadaAxisSample 没有独立字段，只能在**确实报警**时借用状态描述；
            // 常态下写空串（不写 "就绪"），否则报警栏永远挂着一条非报警内容，
            // 操作员分不清"这是报警"还是"这是状态"。
            Write(models, axisId, FieldAlarmMessage, sample.Alarm ? sample.StatusText : string.Empty);
            Write(models, axisId, FieldPositiveLimit, sample.PositiveLimit);
            Write(models, axisId, FieldNegativeLimit, sample.NegativeLimit);
            Write(models, axisId, FieldOrigin, sample.Origin);
            Write(models, axisId, FieldEmergencyStop, sample.EmergencyStop);
            Write(models, axisId, FieldStatusText, sample.StatusText);
        }

        /// <summary>按确定性身份取模型（变量注册表、监视项、画面绑定走这条）</summary>
        public bool TryGet(Guid variableId, out SystemVariableModel variable)
        {
            variable = null!;
            if (variableId == Guid.Empty) return false;

            Dictionary<Guid, SystemVariableModel> byId;
            lock (_gate) byId = _byId;

            return byId.TryGetValue(variableId, out variable!);
        }

        /// <summary>按名字取模型（兼容旧数据/用户手输的兜底寻址，大小写不敏感）</summary>
        public bool TryGet(string name, out SystemVariableModel variable)
        {
            variable = null!;
            if (string.IsNullOrWhiteSpace(name)) return false;

            Dictionary<string, SystemVariableModel> byName;
            lock (_gate) byName = _byName;

            return byName.TryGetValue(name.Trim(), out variable!);
        }

        /// <summary>
        /// 该名字是否落在系统变量保留前缀里（<c>System.</c>，大小写不敏感）。
        ///
        /// 用途：用户变量的新建/改名校验。放行会造出"用户变量遮住系统变量"的同名冲突，
        /// 而遮住之后表现是"轴在动、画面上的数值不变"，现场几乎不可能自己定位到重名。
        /// </summary>
        public static bool IsReservedName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return name.Trim().StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 确定性身份：<c>SHA1("motion-var|{轴Id:N}|{字段}")</c> 取前 16 字节（字段），
        /// 父变量是 <c>SHA1("motion-dev|{轴Id:N}")</c>。
        /// 与 <c>Core.Interfaces.MotionAxisId</c> 同一手法（含"不重排字节序"的理由）。
        /// </summary>
        private static Guid ComputeVariableId(string prefix, Guid ownerId, string? field)
        {
            string key = string.IsNullOrEmpty(field)
                ? $"{prefix}|{ownerId:N}"
                : $"{prefix}|{ownerId:N}|{field}";

            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));

            Span<byte> bytes = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(bytes);
            return new Guid(bytes);
        }

        /// <summary>字段身份：保持历史键格式不变（老方案按 Id 寻址的连线零迁移）</summary>
        private static Guid ComputeVariableId(Guid axisId, string field)
            => ComputeVariableId(IdKeyPrefix, axisId, field);

        /// <summary>变量名：<c>Motion.{轴名}.{字段}</c>（轴名里的点原样保留——本目录从不按点拆名字）</summary>
        private static string BuildName(string axisName, string field) => $"{NamePrefix}Motion.{axisName}.{field}";

        /// <summary>父变量名：<c>System.Motion.{轴名}</c>（整根轴）</summary>
        private static string BuildDeviceName(string axisName) => $"{NamePrefix}Motion.{axisName}";

        private static string BuildDescription(string axisName, string label)
            => $"运动轴 {axisName} 的{label}（只读系统变量）";

        private static string BuildDeviceDescription(string axisName, string cardName)
            => string.IsNullOrWhiteSpace(cardName)
                ? $"运动轴 {axisName} 的只读状态快照（含 {Fields.Length} 个字段）"
                : $"运动轴 {axisName}（{cardName}）的只读状态快照（含 {Fields.Length} 个字段）";

        /// <summary>轴名去掉首尾空白；空名给一个可读兜底（注册表不允许空名，这里只防御脏数据）</summary>
        private static string NormalizeAxisName(string? axisName)
            => string.IsNullOrWhiteSpace(axisName) ? "未命名轴" : axisName!.Trim();

        /// <summary>
        /// 供诊断/断言读取的投影快照（不可变，换表时整份替换）。
        /// 消费方需要"一次拿到完整清单"时用它，而不是遍历 <see cref="Variables"/>
        /// （那个集合由发布器线程改写）。
        /// </summary>
        public IReadOnlyList<IVariable> Snapshot()
        {
            lock (_gate)
                return _ordered;
        }

        /// <summary>事件：投影变化（新增/删除/改名）。SystemVariableHost 订阅后统一刷新集合。</summary>
        public event EventHandler? Changed;

        private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// 拓扑与名字都没变 → 直接返回（100ms 一拍，绝不能每拍都清空重建集合）。
        /// 判据是"实例、顺序、数量"三者全同：只比名字会漏掉"轴被删了又加回来"这类换了对象的场景。
        /// 调用方必须已持 <see cref="_gate"/>。
        /// </summary>
        private bool IsUpToDateLocked(List<(Guid AxisId, MotionAxisBinding Binding)> desired)
        {
            if (_axisNames.Count != desired.Count) return false;
            if (_ordered.Count != desired.Count * (Fields.Length + 1)) return false;

            // 集合被外部动过（测试里 Clear、或另一份目录实例改写）时也必须重建。
            // **只在非托管模式检查**：托管时集合由宿主独占改写，本目录在发布器线程上枚举它
            // 会与通讯/相机的定时器撞车（"Collection was modified"）；而且名字冲突导致宿主
            // 少收一条时，按数量比会每拍判"被外部动过"→ 100ms 全量重建一次（白烧 CPU）。
            if (_host == null
                && Variables.OfType<SystemVariableModel>().Count(m => m.Owner == OwnerName) != _ordered.Count)
                return false;

            int index = 0;
            foreach (var (axisId, binding) in desired)
            {
                if (!_axisNames.TryGetValue(axisId, out var currentName)) return false;
                if (!string.Equals(currentName, NormalizeAxisName(binding.Name), StringComparison.Ordinal)) return false;

                // 卡名进了父变量的说明文案：改卡名也要重建一次，否则说明永远停在旧卡名
                if (!_axisCards.TryGetValue(axisId, out var currentCard)) return false;
                if (!string.Equals(currentCard, binding.CardCaption ?? string.Empty, StringComparison.Ordinal)) return false;

                // 顺序是"父 → 12 字段"，与 Rebuild 的入表顺序必须逐项一致
                if (!_models.TryGetValue((axisId, DeviceFieldKey), out var device)) return false;
                if (!ReferenceEquals(_ordered[index], device)) return false;
                index++;

                foreach (var field in Fields)
                {
                    if (!_models.TryGetValue((axisId, field.Key), out var model)) return false;
                    if (!ReferenceEquals(_ordered[index], model)) return false;
                    index++;
                }
            }

            return true;
        }

        /// <summary>
        /// 把一拍快照写进**父变量**（整根轴，类型 <see cref="MotionAxis"/>）。
        ///
        /// 离线时保留上一拍的已知量值，只改"为什么不知道"——与 12 个字段同一口径
        /// （清 0 会把"不知道"伪装成"停在原点"）。首拍就离线时没有"上一拍"可保留，
        /// 只能给一份全 0 的快照，但 <c>IsValid=false</c> 与 <c>Error</c> 会明确说明它不可信。
        /// </summary>
        private static void WriteDeviceSnapshot(
            Dictionary<(Guid AxisId, string Field), SystemVariableModel> models,
            Guid axisId,
            string axisName,
            string cardName,
            ScadaAxisSample sample)
        {
            if (!models.TryGetValue((axisId, DeviceFieldKey), out var model))
                return;

            if (!sample.IsValid)
            {
                var reason = string.IsNullOrWhiteSpace(sample.Error) ? "状态未知（运动卡未连接）" : sample.Error!;

                if (model.Value is MotionAxis previous)
                {
                    model.UpdateMirrorValue(previous with
                    {
                        // 身份字段也要跟着刷新：离线期间轴改名/换卡，父变量的 Value 不能停在旧名
                        AxisName = axisName,
                        CardName = cardName,
                        IsValid = false,
                        Error = reason,
                        AlarmMessage = reason,
                        StatusText = reason,
                        TimestampUtc = sample.TimestampUtc
                    });
                }
                else
                {
                    model.UpdateMirrorValue(new MotionAxis(
                        axisId, axisName, cardName, false, reason,
                        0, 0, false, false, false, false, reason,
                        false, false, false, false, reason, sample.TimestampUtc));
                }

                return;
            }

            model.UpdateMirrorValue(new MotionAxis(
                axisId,
                axisName,
                cardName,
                true,
                null,
                sample.PositionMm,
                sample.VelocityMmPerS,
                sample.Moving,
                sample.InPosition,
                sample.Enabled,
                sample.Alarm,
                sample.Alarm ? sample.StatusText : string.Empty,
                sample.PositiveLimit,
                sample.NegativeLimit,
                sample.Origin,
                sample.EmergencyStop,
                sample.StatusText,
                sample.TimestampUtc));
        }

        /// <summary>
        /// 写一个字段。模型不存在时静默跳过（轴刚被删、快照迟到一拍）；
        /// 模型值同值会被 <c>SetProperty</c> 短路，所以"只写有变化的字段"是自动成立的。
        /// </summary>
        private static void Write(
            Dictionary<(Guid AxisId, string Field), SystemVariableModel> models,
            Guid axisId,
            string field,
            object? value)
        {
            if (models.TryGetValue((axisId, field), out var model))
                model.UpdateMirrorValue(value);
        }
    }
}
