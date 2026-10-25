using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Newtonsoft.Json;

namespace VisionMaster.Scada
{
    /// <summary>
    /// SCADA 画面文档：整个组态部分的根对象，随方案文件（.vms）一起落盘。
    ///
    /// 为什么要有这一层"根"，而不是让 SolutionModel 直接持有一个 <c>ObservableCollection&lt;ScadaPage&gt;</c>：
    /// 组态部分必然会继续长大——报警配置、趋势曲线定义、用户权限、画面跳转关系……
    /// 有了根对象，这些内容都是往 <see cref="ScadaDocument"/> 上加字段，
    /// 不用再去改 <c>SolutionModel</c> 这个被全工程引用的类型；
    /// <see cref="SchemaVersion"/> 也才有地方落，将来做旧文件升级时不至于抓瞎。
    ///
    /// 本类刻意<b>不</b>做变更计数/变更事件：画面内容变化的信号由
    /// <see cref="ScadaPage.Version"/> 表达，页面增删由 <see cref="Pages"/> 自带的
    /// <c>CollectionChanged</c> 表达。根对象再汇总一遍等于把两层订阅又抄一次，
    /// 而复制出来的订阅逻辑一旦漏摘就是内存泄漏。
    /// </summary>
    public class ScadaDocument : ScadaModelBase
    {
        /// <summary>当前代码写出的结构版本号（读旧文件时用它判断要不要迁移）</summary>
        /// <remarks>
        /// 图层（<see cref="ScadaPage.Layers"/> 与 <see cref="ScadaElement.LayerId"/>）引入时<b>没有</b>升这个号：
        /// 新增字段全部能被"反序列化默认值 + <see cref="EnsureIdentity"/> 补齐"消化掉，
        /// 旧文件照常打开、并且打开之后就拥有可编辑的图层列表。
        /// 升版本号换不来任何好处，反而要为此写一段"比较版本号再决定要不要迁移"的分支——
        /// 版本号只在"旧结构不补齐就会读错/读崩"时才该动。
        /// </remarks>
        public const int CurrentSchemaVersion = 1;

        private int _schemaVersion = CurrentSchemaVersion;
        private Guid _startupPageId;
        private ObservableCollection<ScadaPage> _pages = new();
        private ObservableCollection<ScadaAlarmDefinition> _alarms = new();
        private ObservableCollection<ScadaVariableEvent> _variableEvents = new();
        private ObservableCollection<ScadaStateList> _stateLists = new();

        /// <summary>
        /// 结构版本号（落盘）。加载到小于 <see cref="CurrentSchemaVersion"/> 的文件时，
        /// 提示存在旧结构并按需迁移；比当前版本大则说明文件来自更新的软件版本。
        /// </summary>
        public int SchemaVersion
        {
            get => _schemaVersion;
            set => SetProperty(ref _schemaVersion, value);
        }

        /// <summary>
        /// 画面集合（落盘）。
        /// <c>ObjectCreationHandling.Replace</c>：反序列化时整体替换而不是往默认实例里追加。
        /// 本集合<b>不需要</b>子项订阅保活——没有谁订阅画面的变更（画面自身的变更由
        /// <see cref="ScadaPage.Version"/> 表达）。
        /// 但需要给集合自身挂一层 <c>CollectionChanged</c>：删画面是编辑器里最"贵"的一次操作，
        /// 它必须能撤销（见 <see cref="OnPagesChanged"/>）。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaPage> Pages
        {
            get => _pages;
            set
            {
                var old = _pages;
                if (old != null)
                    old.CollectionChanged -= OnPagesChanged;

                _pages = value ?? new ObservableCollection<ScadaPage>();
                _pages.CollectionChanged += OnPagesChanged;
            }
        }

        /// <summary>初始化文档（为初始集合挂上变更订阅，理由见 <see cref="Pages"/> 与 <see cref="Alarms"/>）</summary>
        [JsonConstructor]
        public ScadaDocument()
        {
            _pages.CollectionChanged += OnPagesChanged;
            _alarms.CollectionChanged += OnAlarmsChanged;
            _variableEvents.CollectionChanged += OnVariableEventsChanged;
            _stateLists.CollectionChanged += OnStateListsChanged;

            // 基座集合同样要在<b>字段初始化的那份实例</b>上挂订阅——只在属性 setter 里挂的话，
            // 新建文档（不走反序列化）就永远收不到 CollectionChanged：
            // 注册/摘除基座不进撤销栈（撤销只回退引用字段、集合停在改过之后），而且没任何报错。
            _basePages.CollectionChanged += OnBasePagesChanged;
        }

        /// <summary>
        /// 画面增删/排序 → 记一条变更（进撤销栈的原料）。
        /// 不挂子项订阅：画面自己的变更由 <see cref="ScadaPage.Version"/> 表达，根对象再抄一层
        /// 等于把订阅逻辑复制一遍（见类注释）。
        /// </summary>
        private void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => ScadaCollectionRecorder.Record(_pages, e);

        /// <summary>
        /// 报警定义集合（落盘）。
        ///
        /// 与 <see cref="Pages"/> 同一套范式：<c>ObjectCreationHandling.Replace</c>（反序列化整体替换）
        /// + setter 摘旧挂新 + <see cref="OnAlarmsChanged"/> 记变更进撤销栈。
        ///
        /// 报警定义<b>不</b>需要逐项订阅保活：它是纯配置，没有"值"可等，
        /// 谁想知道它变了就自己订阅它（<see cref="ScadaAlarmEngine"/> 就是这么做的——
        /// 它订阅集合与逐条定义，运行中改阈值能自动重挂）。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaAlarmDefinition> Alarms
        {
            get => _alarms;
            set
            {
                var old = _alarms;
                if (old != null)
                    old.CollectionChanged -= OnAlarmsChanged;

                _alarms = value ?? new ObservableCollection<ScadaAlarmDefinition>();
                _alarms.CollectionChanged += OnAlarmsChanged;
            }
        }

        /// <summary>报警增删 → 记一条变更。删报警同样要能撤销，理由与删画面相同</summary>
        private void OnAlarmsChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => ScadaCollectionRecorder.Record(_alarms, e);

        /// <summary>
        /// 变量事件表（落盘）："哪个变量、满足什么条件、就干哪些事"。
        ///
        /// 与 <see cref="Alarms"/> 同一套范式（<c>ObjectCreationHandling.Replace</c> + setter 摘旧挂新
        /// + <see cref="OnVariableEventsChanged"/> 记变更），也同样<b>不</b>需要逐项订阅保活：
        /// 它是纯配置，没有"值"可等，运行态的变量事件引擎自己订阅集合与逐条记录
        /// （照 <see cref="ScadaAlarmEngine"/> 对 <see cref="Alarms"/> 的做法）。
        ///
        /// 为什么另起一张表而不并进 <see cref="Alarms"/>：报警是"要人确认的异常"，
        /// 有严重度、确认状态、历史记录；变量事件是"值一变就干点事"的通用钩子，
        /// 绝大多数条根本不进报警条。两者只在"由值驱动"这一点上重合，
        /// 合并之后报警面板得先滤掉九成的非报警项，权限与历史也跟着混在一起。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaVariableEvent> VariableEvents
        {
            get => _variableEvents;
            set
            {
                var old = _variableEvents;
                if (old != null)
                    old.CollectionChanged -= OnVariableEventsChanged;

                _variableEvents = value ?? new ObservableCollection<ScadaVariableEvent>();
                _variableEvents.CollectionChanged += OnVariableEventsChanged;
            }
        }

        /// <summary>变量事件增删 → 记一条变更（理由与删报警相同：删配置也要能撤销）</summary>
        private void OnVariableEventsChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => ScadaCollectionRecorder.Record(_variableEvents, e);

        /// <summary>
        /// 方案级文本列表（落盘）：ComboBox 等控件按稳定 Id 引用。
        ///
        /// 与 <see cref="Alarms"/> 同一套范式（整体替换 + setter 摘旧挂新 + 变更记录）。
        /// 列表自身的条目改动由 <see cref="ScadaStateList.Version"/> 通知订阅方，
        /// 文档层不需要逐条订阅——那会把“条目改了哪一条”抄成第二份真相。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaStateList> StateLists
        {
            get => _stateLists;
            set
            {
                var old = _stateLists;
                if (old != null)
                    old.CollectionChanged -= OnStateListsChanged;

                _stateLists = value ?? new ObservableCollection<ScadaStateList>();
                _stateLists.CollectionChanged += OnStateListsChanged;
            }
        }

        /// <summary>文本列表增删 → 记一条变更（与报警/变量事件同一口径）</summary>
        private void OnStateListsChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => ScadaCollectionRecorder.Record(_stateLists, e);

        /// <summary>
        /// 打开一次可撤销的编辑（D3 统一写入口），用法与 <see cref="ScadaPage.BeginEdit"/> 一致。
        /// 画面级操作（新建/删除/改名/排序/设启动画面）都自带作用域，调用方不需要再包一层——
        /// 除非要把几件事合并成一次撤销。
        /// </summary>
        public IScadaChangeScope BeginEdit(string label) => ScadaChangeScope.Begin(label);

        /// <summary>
        /// 启动画面的稳定身份（落盘）。<see cref="Guid.Empty"/> 表示"没指定"，运行态回落到
        /// <see cref="Pages"/> 的第一页（见 <see cref="ResolveStartupPage"/>）。
        ///
        /// 为什么存 Id 而不是存画面自己：一存对象引用，这个字段就成了"文档里第二个持有画面的地方"，
        /// 删画面时漏一处清空就是幽灵引用，序列化还会跟着把整页内容重复写一遍。
        /// 为什么存的是"单选一个 Id"而不是"每页一个 bool 标记"：启动画面是方案级的<b>单选</b>事实，
        /// 用六个画面上的六个勾选框表达"其中恰好一个"，就得在每次写入时横着清其余五个，
        /// 而复制画面、手工改文件这些旁路一来，随时能出现两个都勾着的状态——那时"谁是启动画面"
        /// 就得靠集合次序猜。单选事实用单值字段表达，互斥是白送的。
        ///
        /// 写入只走 <see cref="SetStartupPage"/> / <see cref="ClearStartupPage"/>，别直着赋这个属性
        /// （那是"跳过校验的口子"，而且绕开了互斥与判等这两条约定）。
        /// </summary>
        public Guid StartupPageId
        {
            get => _startupPageId;
            set => SetProperty(ref _startupPageId, value);
        }

        /// <summary>
        /// 指定启动画面（属性面板"启动画面"勾选框的落点）。
        ///
        /// 传 null、或传一个不属于本方案的画面 = 取消指定（回落到第一个画面），不报错：
        /// 面板那一行的语义本来就是"要么这一页是启动画面，要么没有"，
        /// 为"取消"造一条失败路径只会让用户在勾选框上收到一个莫名其妙的弹窗。
        /// </summary>
        public void SetStartupPage(ScadaPage? page)
        {
            // 基座不能当启动画面（<see cref="IsNavigable"/> 一并拦掉）：启动画面是"运行先看到哪一页"，
            // 而基座是叠在本页下面的公共层，单独显示它既没有本页内容、也不是任何人的目的地。
            var target = IsNavigable(page) ? page!.PageId : Guid.Empty;
            if (target == _startupPageId)
                return; // 幂等：勾了同一页不该刷脏，也不该占一次撤销位

            using (BeginEdit(target == Guid.Empty ? "取消启动画面" : $"设启动画面 [{page!.Name}]"))
            {
                StartupPageId = target;
            }
        }

        /// <summary>
        /// 取消某个画面的启动指定，<b>且仅当它确实是当前启动画面</b>。
        ///
        /// 为什么不能直接用 <see cref="SetStartupPage"/> 传 null 代替：那样一来在画面 B 上
        /// 取消勾选会把画面 A 的启动指定一起擦掉——面板那一行只看得到自己这一页，
        /// 它无权知道"当前是谁"。判等操作才是"取消勾选"的真实语义。
        /// 删画面（<see cref="TryRemovePage"/>）走的是同一条判断，所以两处共用本方法。
        /// </summary>
        public void ClearStartupPage(ScadaPage? page)
        {
            if (page != null && _startupPageId == page.PageId)
            {
                using (BeginEdit($"取消启动画面 [{page.Name}]"))
                {
                    StartupPageId = Guid.Empty;
                }
            }
        }

        /// <summary>
        /// 运行态该显示哪个画面：指定的启动画面（还在）→ 否则第一页 → 一页都没有则 null。
        ///
        /// "回落"必须发生在这里而不是界面侧：指定过启动画面之后又把它删掉是完全正常的操作，
        /// 运行态不能因此显示空白画面，也不能弹"启动画面不存在"——那个配置项已经被删除动作带走了。
        ///
        /// <b>基座画面不参与</b>（见 <see cref="FindNavigablePage"/>）：基座是公共层、不是终点，
        /// 它也在 <see cref="Pages"/> 里（要落盘、要能进编辑器），所以"回落第一页"必须显式跳过它，
        /// 否则把第一页注册成基座之后，一按运行就先看到一张孤零零的基座。
        /// </summary>
        public ScadaPage? ResolveStartupPage()
            => FindNavigablePage(_startupPageId) ?? _pages.FirstOrDefault(IsNavigable);

        /// <summary>按稳定身份找画面（找不到返回 null）</summary>
        public ScadaPage? FindPage(Guid pageId)
            => pageId == Guid.Empty ? null : _pages.FirstOrDefault(p => p.PageId == pageId);

        /// <summary>按名找画面（大小写不敏感；仅用于兼容旧数据与用户手输，优先用 <see cref="FindPage"/>）</summary>
        public ScadaPage? FindPageByName(string? name)
            => string.IsNullOrWhiteSpace(name)
                ? null
                : _pages.FirstOrDefault(p => string.Equals(p.Name, name!.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 按 Id 找<b>可导航</b>的画面：在 <see cref="Pages"/> 里、且不是已注册的基座。
        ///
        /// <b>为什么不能直接用 <see cref="FindPage"/></b>：基座画面同时也在 <see cref="Pages"/> 里
        /// （要落盘、要能进编辑器、要被引用），所以按 Id 找一定会找到它。但基座是"公共层"，
        /// 不是"终点"——跳过去只会看到一张没有本页内容的画面，用户会以为跳转坏了。
        /// 导航、启动画面、画面选择器三处统一走这一对方法，判据只写一遍。
        /// </summary>
        public ScadaPage? FindNavigablePage(Guid pageId)
            => pageId == Guid.Empty
                ? null
                : _pages.FirstOrDefault(p => p.PageId == pageId && !IsBasePage(p) && !p.IsPopup);

        /// <summary>按名找可导航的画面（口径同 <see cref="FindNavigablePage"/>）</summary>
        public ScadaPage? FindNavigablePageByName(string? name)
            => string.IsNullOrWhiteSpace(name)
                ? null
                : _pages.FirstOrDefault(p =>
                    !IsBasePage(p)
                    && !p.IsPopup
                    && string.Equals(p.Name, name!.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>该画面能不能作为导航目标（在本方案里、不是基座、不是弹窗页）</summary>
        public bool IsNavigable(ScadaPage? page)
            => page != null && _pages.Contains(page) && !IsBasePage(page) && !page.IsPopup;

        /// <summary>
        /// 可作为基座的画面集合（落盘）。
        ///
        /// <b>为什么单独一个集合而不是挂在 <see cref="Pages"/> 里打标记</b>：
        /// 基座与画面在生命周期上是两类东西——基座不许被导航（它不是终点，是公共层）、
        /// 不许被删除时连带删掉引用它的画面（引用方按"无基座"降级，而不是跟着消失）。
        /// 合并进一个集合就要靠"某字段当开关"来分流，那种"同一个类型、两种语义"的写法
        /// 迟早把切换画面、删除画面、遍历画面三处的判据写岔。
        ///
        /// 序列化与 <see cref="Pages"/> 同一个范式（`ObjectCreationHandling.Replace`）。
        /// 旧 .vms 里没有这个集合，反序列化后取默认空集——不需要迁移代码。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaPage> BasePages
        {
            get => _basePages;
            set
            {
                var old = _basePages;
                if (old != null)
                    old.CollectionChanged -= OnBasePagesChanged;

                _basePages = value ?? new ObservableCollection<ScadaPage>();
                _basePages.CollectionChanged += OnBasePagesChanged;
            }
        }

        private ObservableCollection<ScadaPage> _basePages = new();

        private void OnBasePagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 基座集合的变更只影响"谁还能被引用"：被删的基座让引用它的画面按"无基座"降级，
            // 不需要在这里清掉引用字段（与 ScadaPage.BasePageId 注释里"悬空不清理"的口径一致）。
            //
            // 没有版本号（基座的版本由 ScadaPage.Version 表达，与 Pages 同一条纪律），
            // 但**要记账**：注册/摘除基座同样是可撤销的编辑动作（否则"撤销"只回退了引用字段、
            // 基座集合停在改过之后的状态，一次撤销后模型仍是不一致的）。
            ScadaCollectionRecorder.Record(_basePages, e);
        }

        /// <summary>
        /// 按 Id 找基座画面。
        /// 找不到（未注册 / 已被删 / 传入空 Id）返回 null——引用方按"无基座"降级，不丢画面内容。
        /// </summary>
        public ScadaPage? FindBasePage(Guid basePageId)
            => basePageId == Guid.Empty ? null : _basePages.FirstOrDefault(p => p.PageId == basePageId);

        /// <summary>按名找基座（大小写不敏感；用于"选的目标其实是基座"这类诊断文案）</summary>
        public ScadaPage? FindBasePageByName(string? name)
            => string.IsNullOrWhiteSpace(name)
                ? null
                : _basePages.FirstOrDefault(p => string.Equals(p.Name, name!.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 把某画面设为基座（加入 <see cref="BasePages"/>）。
        ///
        /// 三条拒由，每条都对应一个真实会坏掉的输入：
        /// ① 该画面已经在基座集合里——重复注册会让"页面列表里同时出现两个同名基座"；
        /// ② 画面不在本方案的 <see cref="Pages"/> 里——外来的对象不该被注册进本方案；
        /// ③ 该画面自己已经引用了别的基座——那就是"基座再套基座"（本版本只支持一层，
        ///    渲染侧只合成一层：基座压底 + 本页，链上第二层会被静默丢掉，看起来像"基座没生效"）。
        ///
        /// ③ 与 <see cref="TrySetBasePage"/> 里那条是同一个不变量的两个入口，两处都必须拦：
        /// 只在一边拦，另一条路径就能造出"模型里有链、画面上只画一层"的静默故障。
        /// </summary>
        public bool TryAddBasePage(ScadaPage? page, out string error)
        {
            error = string.Empty;

            if (page is null || !_pages.Contains(page))
            {
                error = "画面不在本方案里，无法注册为基座";
                return false;
            }

            if (_basePages.Contains(page))
                return true; // 幂等：已经注册过了

            if (page.IsPopup)
            {
                error = "弹窗页是特殊画面，不能注册为基座";
                return false;
            }

            if (page.BasePageIds.Count != 0)
            {
                error = "该画面已引用别的基座，不能再注册为基座（本版本只支持一层）";
                return false;
            }

            using (BeginEdit($"注册基座 [{page.Name}]"))
            {
                _basePages.Add(page);
            }

            return true;
        }

        /// <summary>
        /// 把某画面从基座集合里摘除（引用它的画面按"无基座"降级）。
        ///
        /// 幂等：本来就不在集合里时返回 true 且不产记录（与 <see cref="ScadaPage.TryUngroupElements"/> 同一口径）。
        /// 摘除不清掉引用字段：那些画面的 <c>BasePageId</c> 会指向一个不存在的 Id，
        /// 渲染侧按"无基座"降级，撤销还能把它找回来。
        /// </summary>
        public bool TryRemoveBasePage(ScadaPage? page, out string error)
        {
            error = string.Empty;

            if (page is null || !_basePages.Contains(page))
                return true; // 本来就不在，目标已达成

            using (BeginEdit($"摘除基座 [{page.Name}]"))
            {
                _basePages.Remove(page);
            }

            return true;
        }

        /// <summary>
        /// 把 <paramref name="page"/> 的基座引用设为 <paramref name="basePage"/>（null / Guid.Empty = 不引用）。
        ///
        /// 四条拒由：
        /// ① 自引用（基座引用自己）；
        /// ② 引用方本身就是基座——"基座再套基座"，本版本只支持一层（渲染侧只合成两层：
        ///    基座压底 + 本页；链上第二层会被静默丢掉）。与 <see cref="TryAddBasePage"/> 的 ③ 同一不变量；
        /// ③ 目标画面不是已注册的基座——引用一个不注册的对象会让它既不能被编辑也不能被统一管理；
        /// ④ 会形成环（防御：正常编辑路径到不了这里，但手改过的 .vms 可能带着链）。
        ///
        /// 只在引用关系<b>变化</b>时动（幂等：同一基座再指一次是空操作）。
        /// </summary>
        public bool TrySetBasePage(ScadaPage? page, ScadaPage? basePage, out string error)
        {
            error = string.Empty;

            if (page is null || !_pages.Contains(page))
            {
                error = "画面不在本方案里";
                return false;
            }

            var newBaseId = basePage?.PageId ?? Guid.Empty;

            if (page.BasePageIds.Count == (basePage is null ? 0 : 1)
                && (basePage is null || page.BasePageIds[0] == newBaseId))
                return true; // 幂等：没变化

            if (basePage is null)
            {
                using (page.BeginEdit($"取消基座引用 [{page.Name}]"))
                {
                    page.SetBasePageIds(Array.Empty<Guid>());
                }

                return true;
            }

            if (ReferenceEquals(page, basePage))
            {
                error = "基座不能引用它自己";
                return false;
            }

            if (IsBasePage(page))
            {
                error = "基座画面不能再引用别的基座（本版本只支持一层）";
                return false;
            }

            if (!IsBasePage(basePage))
            {
                error = $"「{basePage.Name}」不是已注册的基座画面";
                return false;
            }

            if (CreatesCycle(page, basePage))
            {
                error = "会形成循环引用（基座不能再套基座）";
                return false;
            }

            using (page.BeginEdit($"设置基座 [{basePage.Name}]"))
            {
                page.SetBasePageIds(new[] { newBaseId });
            }

            return true;
        }

        /// <summary>
        /// 把一只基座追加到画面的引用列表末尾（渲染顺序 = 列表顺序，后加的盖在上面）。
        ///
        /// 与 <see cref="TrySetBasePage"/> 共用同一套校验：画面在本方案、目标已注册为基座、
        /// 引用方自己不是基座、不成环。重复添加是幂等空操作（不产生撤销记录）。
        /// </summary>
        public bool TryAddBasePageReference(ScadaPage? page, ScadaPage? basePage, out string error)
        {
            error = string.Empty;

            if (page is null || !_pages.Contains(page))
            {
                error = "画面不在本方案里";
                return false;
            }

            if (basePage is null)
            {
                error = "基座画面为空";
                return false;
            }

            if (ReferenceEquals(page, basePage))
            {
                error = "基座不能引用它自己";
                return false;
            }

            if (IsBasePage(page))
            {
                error = "基座画面不能再引用别的基座（本版本只支持一层）";
                return false;
            }

            if (!IsBasePage(basePage))
            {
                error = $"「{basePage.Name}」不是已注册的基座画面";
                return false;
            }

            if (page.BasePageIds.Contains(basePage.PageId))
                return true; // 幂等

            if (CreatesCycle(page, basePage))
            {
                error = "会形成循环引用（基座不能再套基座）";
                return false;
            }

            using (page.BeginEdit($"添加基座 [{basePage.Name}]"))
            {
                page.SetBasePageIds(page.BasePageIds.Concat(new[] { basePage.PageId }));
            }

            return true;
        }

        /// <summary>从画面的引用列表里摘掉一只基座；本来没引用时幂等返回 true。</summary>
        public bool TryRemoveBasePageReference(ScadaPage? page, ScadaPage? basePage, out string error)
        {
            error = string.Empty;

            if (page is null || !_pages.Contains(page))
            {
                error = "画面不在本方案里";
                return false;
            }

            if (basePage is null || !page.BasePageIds.Contains(basePage.PageId))
                return true;

            using (page.BeginEdit($"移除基座 [{basePage.Name}]"))
            {
                page.SetBasePageIds(page.BasePageIds.Where(id => id != basePage.PageId));
            }

            return true;
        }

        /// <summary>
        /// 调整一只基座在引用列表里的位置（0 = 最底层）。用于"先画标题栏还是先画底栏"这类层序调整。
        /// 返回 false 只可能是画面/基座不匹配，正常界面路径不可达。
        /// </summary>
        public bool TryMoveBasePageReference(ScadaPage? page, ScadaPage? basePage, int newIndex, out string error)
        {
            error = string.Empty;

            if (page is null || !_pages.Contains(page) || basePage is null)
            {
                error = "画面或基座为空";
                return false;
            }

            var ids = page.BasePageIds.ToList();
            var oldIndex = ids.IndexOf(basePage.PageId);

            if (oldIndex < 0)
            {
                error = $"「{basePage.Name}」不在本页的基座列表里";
                return false;
            }

            var target = Math.Clamp(newIndex, 0, ids.Count - 1);

            if (target == oldIndex)
                return true;

            ids.RemoveAt(oldIndex);
            ids.Insert(target, basePage.PageId);

            using (page.BeginEdit($"调整基座顺序 [{basePage.Name}]"))
            {
                page.SetBasePageIds(ids);
            }

            return true;
        }

        /// <summary>
        /// 本页当前真正生效的基座列表：按 <see cref="ScadaPage.BasePageIds"/> 顺序，
        /// 跳过悬空 Id、自引用与重复项。渲染侧只认这个结果，不自己再写一遍过滤。
        /// </summary>
        public IReadOnlyList<ScadaPage> ResolveBasePages(ScadaPage? page)
        {
            if (page is null || page.BasePageIds.Count == 0)
                return Array.Empty<ScadaPage>();

            var result = new List<ScadaPage>(page.BasePageIds.Count);

            foreach (var id in page.BasePageIds)
            {
                var basePage = FindBasePage(id);

                if (basePage is null || ReferenceEquals(basePage, page) || result.Contains(basePage))
                    continue;

                result.Add(basePage);
            }

            return result;
        }

        /// <summary>该画面是不是已注册的基座（直接比引用，不查 Id——对象比 Id 更可靠）</summary>
        public bool IsBasePage(ScadaPage? page)
            => page is not null && _basePages.Contains(page);

        /// <summary>
        /// 把 page 设成 basePage 的引用方会不会造成循环（A 引 B 且 B 引 A，或更长环）。
        ///
        /// 正常编辑路径到不了这里（基座本身不允许再引用基座），但手改过的 .vms 可能带着链；
        /// 这里按列表逐层扫，既防直接自引用，也防"畸形数据里的多跳环"。
        /// </summary>
        private bool CreatesCycle(ScadaPage page, ScadaPage basePage)
        {
            var byId = new Dictionary<Guid, ScadaPage>();

            foreach (var p in _pages)
                byId[p.PageId] = p;
            foreach (var p in _basePages)
                byId[p.PageId] = p;

            var stack = new Stack<ScadaPage>();
            var seen = new HashSet<Guid>();
            stack.Push(basePage);

            while (stack.Count > 0)
            {
                var current = stack.Pop();

                if (!seen.Add(current.PageId))
                    return true; // 链上有重复 Id = 环

                if (current.PageId == page.PageId)
                    return true;

                foreach (var id in current.BasePageIds)
                {
                    if (byId.TryGetValue(id, out var next))
                        stack.Push(next);
                }
            }

            return false;
        }


        /// <summary>
        /// 新建一个画面并加入集合（名字留空则按"画面_N"自动命名，N 取当前未被占用的最小序号）。
        ///
        /// 自动命名放在模型侧而不是编辑器侧：名字唯一性只在这里能保证（此处能看到全部画面），
        /// 而且断言/离线脚本建画面时同样需要它。
        ///
        /// 新画面自带一个默认图层（<c>图层_1</c>）。这一步必须在这里做，不能推给
        /// <see cref="EnsureIdentity"/> 或界面层：本方法是编辑器"新建画面"的唯一入口，
        /// 若留空集合，图层面板就得自己处理"一个图层都没有"的畸形状态，而工具箱往画布上
        /// 放图元时也找不到归属层。走反序列化进来的画面由文件自带图层，旧文件（没有这个字段）
        /// 由 <see cref="EnsureIdentity"/> 补齐——两条路都不经过本方法，互不干扰。
        ///
        /// <paramref name="basePageId"/>：新画面默认引用哪个基座。留空 = 不引用。
        /// 编辑器顶栏"新建"默认沿用当前页的基座，正是现场建一批"长得一样"的画面的常态。
        /// </summary>
        public ScadaPage AddPage(string? name = null, Guid basePageId = default)
        {
            var page = new ScadaPage();
            var pageName = string.IsNullOrWhiteSpace(name) ? NextPageName() : name!.Trim();

            using (BeginEdit($"新建画面 [{pageName}]"))
            {
                page.Name = pageName;
                // 这里是"新建画面夹具/迁移"入口：basePageId 由调用方保证来自当前页的引用列表。
                // 用户改引用的路径走 TryAdd/TryRemove/TrySet，那三处才做注册/一层/成环校验。
                if (basePageId != Guid.Empty)
                    page.SetBasePageIds(new[] { basePageId }); // 先定引用再进集合：订阅者看到的画面永远是"完整"的
                page.AddLayer(); // 先建好图层再进集合：让订阅者看到的画面永远是"完整"的
                _pages.Add(page);
            }

            return page;
        }

        #region 画面管理（删除 / 重命名 / 排序）

        /// <summary>
        /// 删除画面。<b>只拒绝"删到没有任何画面"</b>，非空画面照删。
        ///
        /// 这里与 <see cref="ScadaPage.TryRemoveLayer"/> 的口径刻意不同：删图层时用户只想丢掉
        /// 一个分组开关，图元被他当作资产留着，所以模型必须拒绝；删画面则意味着"这一整屏我都不要了"，
        /// 拒绝非空画面等于功能不可用。因此这里只守住最后一条底线。
        /// S9 起删画面<b>可撤销</b>（走 <see cref="BeginEdit"/>），界面侧的确认框仍建议保留——
        /// 撤销是补救，不是让人放心乱删的理由。
        /// </summary>
        public bool TryRemovePage(ScadaPage? page, out string error)
        {
            error = string.Empty;

            if (page == null || !_pages.Contains(page))
            {
                error = "画面不存在，无法删除";
                return false;
            }

            if (_pages.Count <= 1)
            {
                error = "方案至少需要保留一个画面，无法删除最后一个画面";
                return false;
            }

            using (BeginEdit($"删除画面 [{page.Name}]"))
            {
                _pages.Remove(page);

                // 基座集合里也必须摘掉：留着它会让"被删掉的画面"仍能被引用
                // （IsBasePage 查的是 BasePages，不查 Pages），界面上还会在下拉框里留一条幽灵候选。
                // 与 _pages 在同一个作用域里，一次撤销把两边一起恢复。
                _basePages.Remove(page);

                // 删掉的正是启动画面时顺手清空指定：留着那个 Id 是个指向不存在画面的幽灵引用，
                // 虽然 ResolveStartupPage 的回落能兜住显示，但属性面板上会一排全不勾、
                // 而用户并不知道"其实是因为启动画面被删了"。清空之后行为一样（都回落第一页），
                // 差别只在于配置状态是诚实的。
                ClearStartupPage(page);
            }

            return true;
        }

        /// <summary>
        /// 画面改名（方案内不许重名）。校验口径与 <see cref="ScadaPage.TryRenameLayer"/> 逐条同构，
        /// 而<b>不是</b>照抄 <c>FlowListViewModel</c> 那套无重名校验的写法：
        /// <see cref="FindPageByName"/> 与运行态的画面跳转下拉框都按名取画面，重名会解析到谁全看集合次序。
        /// 空名拒、与原值等值幂等放行（不刷任何变更）、查重用 <c>OrdinalIgnoreCase</c>、失败原因中文带回。
        /// </summary>
        public bool TryRenamePage(ScadaPage? page, string? newName, out string error)
        {
            error = string.Empty;

            if (page == null || !_pages.Contains(page))
            {
                error = "画面不存在，无法改名";
                return false;
            }

            var target = (newName ?? string.Empty).Trim();
            if (target.Length == 0)
            {
                error = "画面名不能为空";
                return false;
            }

            if (string.Equals(page.Name, target, StringComparison.Ordinal))
                return true; // 幂等：连值都没变，不该算一次变更

            if (_pages.Any(p => !ReferenceEquals(p, page)
                                && string.Equals(p.Name, target, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"已存在同名画面 [{target}]，请更换名称";
                return false;
            }

            using (BeginEdit($"画面改名 [{target}]"))
            {
                page.Name = target;
            }

            return true;
        }

        /// <summary>
        /// 调整画面在集合中的位置（<paramref name="targetIndex"/> 是移动后应处的下标）。
        /// 次序即画面标签页的排列次序；越界下标按两端夹取（拖拽到列表末尾之外是常见手势，不该报错）。
        /// </summary>
        public bool TryMovePage(ScadaPage? page, int targetIndex, out string error)
        {
            error = string.Empty;

            if (page == null)
            {
                error = "画面不存在，无法调整次序";
                return false;
            }

            int from = _pages.IndexOf(page);
            if (from < 0)
            {
                error = "画面不属于本方案，无法调整次序";
                return false;
            }

            targetIndex = Math.Clamp(targetIndex, 0, _pages.Count - 1);
            if (from == targetIndex)
                return true; // 原地放置是空操作

            using (BeginEdit($"画面排序 [{page.Name}]"))
            {
                _pages.Move(from, targetIndex);
            }

            return true;
        }

        #endregion

        #region 报警管理（新建 / 删除 / 重命名 / 查找）

        /// <summary>
        /// 新建一条报警定义并加入集合（名字留空则按"报警_N"自动命名，N 取当前未被占用的最小序号）。
        ///
        /// 默认严重度只在<b>这里</b>给一次（<see cref="ScadaAlarmDefinition.Kind"/> 的 setter 刻意不联动）：
        /// 用户后面把"高高限"改成"高限"时，他特意调过的严重度不该被静默推翻；
        /// 而新建时给一个跟种类相称的初值，能省掉九成的第一次编辑。
        /// </summary>
        /// <param name="name">报警名；留空自动命名</param>
        /// <param name="kind">条件种类；决定默认严重度</param>
        public ScadaAlarmDefinition AddAlarm(string? name = null, ScadaAlarmKind kind = ScadaAlarmKind.High)
        {
            var alarm = new ScadaAlarmDefinition();
            var alarmName = string.IsNullOrWhiteSpace(name) ? NextAlarmName() : name!.Trim();

            using (BeginEdit($"新建报警 [{alarmName}]"))
            {
                alarm.Name = alarmName;
                alarm.Kind = kind;
                alarm.Severity = kind.DefaultSeverity();
                _alarms.Add(alarm);
            }

            return alarm;
        }

        /// <summary>
        /// 删除报警定义。<b>允许删到一条不剩</b>——与 <see cref="TryRemovePage"/> 的
        /// "至少留一个画面"刻意不同：画面是运行态的载体，一页都没有就没东西可显示；
        /// 报警是附加的监视项，"这个方案不需要报警"是完全正常的组态结果，
        /// 拒绝删最后一条等于逼用户留一条永远关掉的垃圾配置。
        /// </summary>
        public bool TryRemoveAlarm(ScadaAlarmDefinition? alarm, out string error)
        {
            error = string.Empty;

            if (alarm == null || !_alarms.Contains(alarm))
            {
                error = "报警不存在，无法删除";
                return false;
            }

            using (BeginEdit($"删除报警 [{alarm.Name}]"))
            {
                _alarms.Remove(alarm);
            }

            return true;
        }

        /// <summary>
        /// 报警改名（方案内不许重名）。校验口径与 <see cref="TryRenamePage"/> 逐条同构：
        /// 空名拒、与原值等值幂等放行（不刷任何变更）、查重用 <c>OrdinalIgnoreCase</c>、失败原因中文带回。
        ///
        /// 为什么报警也要唯一名：报警列表、历史面板、CSV 导出、报警条上显示的都是这个名字，
        /// 重名会让"到底是哪台设备报的"在事后查询时无法分辨——而事后查询正是报警系统的核心用途。
        /// </summary>
        public bool TryRenameAlarm(ScadaAlarmDefinition? alarm, string? newName, out string error)
        {
            error = string.Empty;

            if (alarm == null || !_alarms.Contains(alarm))
            {
                error = "报警不存在，无法改名";
                return false;
            }

            var target = (newName ?? string.Empty).Trim();
            if (target.Length == 0)
            {
                error = "报警名不能为空";
                return false;
            }

            if (string.Equals(alarm.Name, target, StringComparison.Ordinal))
                return true; // 幂等：连值都没变，不该算一次变更

            if (_alarms.Any(a => !ReferenceEquals(a, alarm)
                                 && string.Equals(a.Name, target, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"已存在同名报警 [{target}]，请更换名称";
                return false;
            }

            using (BeginEdit($"报警改名 [{target}]"))
            {
                alarm.Name = target;
            }

            return true;
        }

        /// <summary>按稳定身份找报警定义（找不到返回 null）</summary>
        public ScadaAlarmDefinition? FindAlarm(Guid alarmId)
            => alarmId == Guid.Empty ? null : _alarms.FirstOrDefault(a => a.AlarmId == alarmId);

        /// <summary>按名找报警定义（大小写不敏感；仅用于兼容旧数据与用户手输，优先用 <see cref="FindAlarm"/>）</summary>
        public ScadaAlarmDefinition? FindAlarmByName(string? name)
            => string.IsNullOrWhiteSpace(name)
                ? null
                : _alarms.FirstOrDefault(a => string.Equals(a.Name, name!.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>取当前未被占用的最小"报警_N"</summary>
        private string NextAlarmName()
        {
            var used = new HashSet<string>(_alarms.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);

            int index = 1;
            while (used.Contains($"报警_{index}"))
                index++;

            return $"报警_{index}";
        }

        #endregion

        #region 变量事件管理（新建 / 删除 / 查找）

        /// <summary>
        /// 取某个变量的变量事件记录，没有就地建一条（变量事件弹窗"选中一个变量"就调这个）。
        ///
        /// 为什么是"取或建"而不是"每次都新建"：本表的形状是<b>一个变量一条记录</b>
        /// （见 <see cref="ScadaVariableEvent"/> 的类注释），同一个变量被选中两次必须落到同一条上，
        /// 否则改名级联与运行态订阅会各自持有一份"半条配置"。
        ///
        /// 按 Id 优先、名字兜底（与 <see cref="ScadaVariableEvent.Matches"/> 同一口径）：
        /// 旧数据没有 Id，只能按名认领；认领到就复用，不会为同一个变量造出第二条。
        /// </summary>
        /// <param name="variableId">变量稳定身份（<see cref="Guid.Empty"/> = 旧数据，只能按名找）</param>
        /// <param name="variableName">变量名（展示串 + 旧数据兜底键）</param>
        public ScadaVariableEvent GetOrAddVariableEvent(Guid variableId, string? variableName)
        {
            var existing = FindVariableEvent(variableId) ?? FindVariableEventByName(variableName);
            if (existing != null)
                return existing;

            var record = new ScadaVariableEvent();

            using (BeginEdit($"新建变量事件 [{variableName}]"))
            {
                record.Bind(variableId, variableName);
                _variableEvents.Add(record);
            }

            return record;
        }

        /// <summary>
        /// 删除一条变量事件记录（连同它下面所有钩子）。<b>允许删到一条不剩</b>，
        /// 理由与 <see cref="TryRemoveAlarm"/> 相同：变量事件是附加的监视项，
        /// "这个方案不需要任何值驱动动作"是完全正常的组态结果。
        /// </summary>
        public bool TryRemoveVariableEvent(ScadaVariableEvent? record, out string error)
        {
            error = string.Empty;

            if (record == null || !_variableEvents.Contains(record))
            {
                error = "变量事件不存在，无法删除";
                return false;
            }

            using (BeginEdit($"删除变量事件 [{record.VariableName}]"))
            {
                _variableEvents.Remove(record);
            }

            return true;
        }

        /// <summary>按变量稳定身份找变量事件记录（找不到返回 null）</summary>
        public ScadaVariableEvent? FindVariableEvent(Guid variableId)
            => variableId == Guid.Empty
                ? null
                : _variableEvents.FirstOrDefault(v => v.VariableId == variableId);

        /// <summary>按变量名找变量事件记录（大小写不敏感；仅用于兼容旧数据，优先用 <see cref="FindVariableEvent"/>）</summary>
        public ScadaVariableEvent? FindVariableEventByName(string? name)
            => string.IsNullOrWhiteSpace(name)
                ? null
                : _variableEvents.FirstOrDefault(v => string.Equals(v.VariableName, name!.Trim(), StringComparison.OrdinalIgnoreCase));

        #endregion

        /// <summary>
        /// 变量改名后的引用刷新：递归到所有画面的所有绑定，并修到报警定义上。返回被改动的引用条数。
        ///
        /// 这是改名级联在画面侧的入口（与 <c>IReadOnlyWorkspaceContext.OnVariableRenamed</c>
        /// 里刷新流程连线、监视项并列）。按 Id 寻址的绑定只刷新展示名，
        /// 旧数据（没有 Id）在按名命中时顺带把 Id 补回来。
        ///
        /// 报警<b>必须</b>一起修：报警条上写着"变量名"是给现场看的，
        /// 漏修之后报警会指着旧名字，而旧名字已经不存在了——
        /// 运行态解析不到变量就静默跳过（见 <see cref="ScadaAlarmEngine"/>），
        /// 结果就是一条无声失效的报警。无声失效的报警比没有报警更危险。
        /// </summary>
        /// <param name="variableId">改名变量的稳定身份</param>
        /// <param name="oldName">改名前的旧名</param>
        /// <param name="newName">改名后的新名</param>
        public int RefreshVariableReferences(Guid variableId, string? oldName, string newName)
        {
            int changed = 0;

            foreach (var page in _pages)
                changed += page.RefreshVariableReferences(variableId, oldName, newName);

            // 报警这一支路与图元支路同一口径（见 ScadaElement.RefreshVariableReferences）：
            // 改名级联是变量面板触发的数据自愈，不是用户在画面上做的编辑——撤销由变量注册表
            // 那一侧的作用域负责，这里不该被写守卫拦。漏掉这一层时，Strict 模式下改名会直接抛，
            // 而同一批里画面上的绑定却已经改成功了：一半改一半没改，最难查的那种半成品状态。
            using (ScadaWriteGuard.Suspend())
            {
                foreach (var alarm in _alarms)
                {
                    if (!alarm.Matches(variableId, oldName))
                        continue;

                    alarm.Bind(variableId, newName, alarm.ArrayIndex);
                    changed++;
                }

                // 变量事件表同批修：它的钩子里同样可以引用别的变量
                //（"这个变量一变就把那个变量置 1"），漏修就是又一条无声失效的动作。
                foreach (var record in _variableEvents)
                {
                    if (record == null)
                        continue;

                    if (record.Matches(variableId, oldName))
                    {
                        record.Bind(variableId, newName);
                        changed++;
                    }

                    changed += record.RefreshVariableReferences(variableId, oldName, newName);
                }
            }

            return changed;
        }

        /// <summary>
        /// 轴改名后的引用刷新：递归到每张画面（见 <see cref="ScadaPage.RefreshAxisReferences"/>）。
        /// 返回被改动的条数，0 = 本方案没有引用这根轴。
        ///
        /// 与 <see cref="RefreshVariableReferences"/> 同一套纪律：改名级联是数据自愈，
        /// 不在撤销栈里，也不该被写守卫拦——否则 Strict 模式下改名会直接抛，
        /// 而画面上的绑定已经改成功了，留下"一半改一半没改"的半成品状态。
        /// </summary>
        public int RefreshAxisReferences(Guid axisId, string? oldName, string newName)
        {
            int changed = 0;

            using (ScadaWriteGuard.Suspend())
            {
                foreach (var page in _pages)
                    changed += page.RefreshAxisReferences(axisId, oldName, newName);
            }

            return changed;
        }

        /// <summary>
        /// 旧轴控件迁移：把"同页只有一块轴面板 + 一张旧点位表"的画面自动合并成轴控台。
        ///
        /// 为什么只在这一个条件下自动合并：旧画面里轴面板与点位表往往摆在不同的位置，
        /// 无条件合并会**删掉用户画面上的一块**、并在原处留一个洞（版面/叠放都会变）。
        /// 当且仅当整页只有这两个图元时，这一页本来就是"一根轴的调试页"，
        /// 合并不会撞到任何别的图元——这是唯一能自动做、又不改用户版面的情形。
        /// 其余页面只产出一条提示，让用户自己决定（保留哪块、删哪块）。
        ///
        /// 合并后的面板尺寸补到至少 320×470（轴控台默认尺寸）：点位区 6 行需要这个高度，
        /// 否则"合并成功"在屏幕上表现为"点位区被裁掉"。宽高只放大不缩小。
        ///
        /// 迁移不是用户编辑：走 <see cref="ScadaWriteGuard.Suspend"/> + <see cref="ScadaChangeScope.SuspendRecording"/>，
        /// 不进撤销栈、也不被写守卫拦（与 <see cref="EnsureIdentity"/> 同一口径）。
        /// </summary>
        /// <param name="messages">每个被处理画面的结果/未合并原因（加载期写诊断日志）</param>
        /// <returns>实际合并的画面数</returns>
        public int MigrateLegacyAxisElements(out IReadOnlyList<string> messages)
        {
            const string LegacyTypeKey = "Hmi.AxisPointTable";

            var notes = new List<string>();
            int merged = 0;

            using (ScadaWriteGuard.Suspend())
            using (ScadaChangeScope.SuspendRecording())
            {
                foreach (var page in _pages)
                {
                    if (page == null)
                        continue;

                    var legacyTables = page.Elements
                        .Where(e => e != null && string.Equals(e.TypeKey, LegacyTypeKey, StringComparison.Ordinal))
                        .ToList();

                    if (legacyTables.Count == 0)
                        continue;

                    var panels = page.Elements
                        .Where(e => e != null && (string.Equals(e.TypeKey, "Hmi.AxisPanel", StringComparison.Ordinal)
                                                  || string.Equals(e.TypeKey, "Hmi.AxisConsole", StringComparison.Ordinal)))
                        .ToList();

                    if (legacyTables.Count != 1 || panels.Count != 1)
                    {
                        notes.Add($"画面「{page.Name}」有 {legacyTables.Count} 张旧点位表、{panels.Count} 块轴面板："
                                  + "未自动合并，请手工保留一块轴面板并删掉旧点位表");
                        continue;
                    }

                    if (page.Elements.Count != 2)
                    {
                        notes.Add($"画面「{page.Name}」除轴面板与旧点位表外还有其它图元："
                                  + "未自动合并（避免改动版面），请手工把点位表并进轴面板");
                        continue;
                    }

                    var panel = panels[0];
                    var table = legacyTables[0];

                    if (table.IsLocked)
                    {
                        notes.Add($"画面「{page.Name}」的旧点位表已锁定：未自动合并，请先解锁再迁移");
                        continue;
                    }

                    // 先取并集再删旧表：删完就读不到旧表的候选轴了。
                    // 顺序：面板在前、旧表独有的在后；按 Id 去重。
                    var options = new List<ScadaAxisRef>();
                    var seen = new HashSet<Guid>();

                    foreach (var source in new[] { panel, table })
                    {
                        foreach (var axis in ScadaAxisOptionsCodec.Parse(source.GetProperty(ScadaAxisOptionsCodec.Key)))
                        {
                            if (axis.AxisId == Guid.Empty || !seen.Add(axis.AxisId))
                                continue;

                            options.Add(axis);
                        }
                    }

                    // 再摘旧表：删除失败时不要留下"面板已经打开点位、旧表还在"的半合并状态。
                    if (!page.TryRemoveElement(table, out var removeError))
                    {
                        notes.Add($"画面「{page.Name}」自动合并失败：{removeError}");
                        continue;
                    }

                    panel.SetProperty(ScadaAxisOptionsCodec.Key, ScadaAxisOptionsCodec.Format(options));
                    panel.SetProperty("ShowPoints", "True");
                    panel.SetProperty("ShowCommands", "True");
                    panel.Width = Math.Max(panel.Width, 320);
                    panel.Height = Math.Max(panel.Height, 470);

                    merged++;
                    notes.Add($"画面「{page.Name}」已自动合并旧点位表到轴面板"
                              + $"（候选轴 {options.Count} 根，面板尺寸已补到至少 320×470）");
                }
            }

            messages = notes;
            return merged;
        }

        /// <summary>
        /// 补发缺失的稳定身份（画面/图层/图元及其归属/报警，旧数据迁移）。返回补发的处数。
        /// 加载流程在读文件后调用一次即可（见 SolutionService 的加载链路）。
        /// 图元归属的补齐规则见 <see cref="ScadaPage.EnsureIdentity"/>。
        /// 报警只补 <see cref="ScadaAlarmDefinition.AlarmId"/>，不补 <c>VariableId</c>——
        /// 后者的 <c>Guid.Empty</c> 是有含义的（"只能按名字找"），靠加载期按名解析成功后自愈。
        /// </summary>
        public int EnsureIdentity()
        {
            int repaired = 0;

            foreach (var page in _pages)
                repaired += page.EnsureIdentity();

            // 迁移不是用户编辑：补齐旧数据的 Id 不该进撤销栈、也不该被写守卫拦
            // （理由与 ScadaPage.EnsureIdentity 完全相同）。
            using (ScadaWriteGuard.Suspend())
            using (ScadaChangeScope.SuspendRecording())
            {
                foreach (var alarm in _alarms)
                {
                    if (alarm.AlarmId != Guid.Empty)
                        continue;

                    alarm.AlarmId = Guid.NewGuid();
                    repaired++;
                }

                // 基座的两条不变量在加载期也要成立——手改过的 .vms、或旧版本留下的数据
                // 可能带着"基座集合里的幽灵条目"和"基座再套基座"的链。
                // 渲染侧只合成两层（基座压底 + 本页），链上第二层会被静默丢掉，
                // 现场看到的是"基座改了没反应"，比加载期直接报错难查得多。
                for (int i = _basePages.Count - 1; i >= 0; i--)
                {
                    if (_pages.Contains(_basePages[i]) && !_basePages[i].IsPopup)
                        continue;

                    _basePages.RemoveAt(i); // 幽灵条目 / 弹窗页误注册：都不该留在基座集合里
                    repaired++;
                }

                foreach (var basePage in _basePages)
                {
                    if (basePage.BasePageIds.Count == 0)
                        continue;

                    basePage.SetBasePageIds(Array.Empty<Guid>()); // 一层限制：基座不引用任何基座
                    repaired++;
                }

                // 手改过的 .vms 可能带自引用/重复 Id：渲染侧会重复画同一层或把本页当基座。
                // 这里只清结构性脏数据，悬空 Id 保留（撤销/恢复基座还要靠它）。
                foreach (var page in _pages)
                {
                    var normalized = page.BasePageIds
                        .Where(id => id != page.PageId)
                        .Distinct()
                        .ToList();

                    if (normalized.SequenceEqual(page.BasePageIds))
                        continue;

                    page.SetBasePageIds(normalized);
                    repaired++;
                }
            }

            return repaired;
        }

        /// <summary>取当前未被占用的最小"画面_N"</summary>
        private string NextPageName()
        {
            var used = new HashSet<string>(_pages.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            int index = 1;
            while (used.Contains($"画面_{index}"))
                index++;

            return $"画面_{index}";
        }
    }
}
