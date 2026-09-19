using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public class ScadaDocument : BindableBase
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
        /// 本集合<b>不需要</b>订阅保活三件套——没有谁订阅画面的变更（画面自身的变更由
        /// <see cref="ScadaPage.Version"/> 表达），所以这里只做 null 兜底，不加子项订阅。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaPage> Pages
        {
            get => _pages;
            set => _pages = value ?? new ObservableCollection<ScadaPage>();
        }

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
            => StartupPageId = page != null && _pages.Contains(page) ? page.PageId : Guid.Empty;

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
                StartupPageId = Guid.Empty;
        }

        /// <summary>
        /// 运行态该显示哪个画面：指定的启动画面（还在）→ 否则第一页 → 一页都没有则 null。
        ///
        /// "回落"必须发生在这里而不是界面侧：指定过启动画面之后又把它删掉是完全正常的操作，
        /// 运行态不能因此显示空白画面，也不能弹"启动画面不存在"——那个配置项已经被删除动作带走了。
        /// </summary>
        public ScadaPage? ResolveStartupPage()
            => FindPage(_startupPageId) ?? (_pages.Count > 0 ? _pages[0] : null);

        /// <summary>按稳定身份找画面（找不到返回 null）</summary>
        public ScadaPage? FindPage(Guid pageId)
            => pageId == Guid.Empty ? null : _pages.FirstOrDefault(p => p.PageId == pageId);

        /// <summary>按名找画面（大小写不敏感；仅用于兼容旧数据与用户手输，优先用 <see cref="FindPage"/>）</summary>
        public ScadaPage? FindPageByName(string? name)
            => string.IsNullOrWhiteSpace(name)
                ? null
                : _pages.FirstOrDefault(p => string.Equals(p.Name, name!.Trim(), StringComparison.OrdinalIgnoreCase));

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
        /// </summary>
        public ScadaPage AddPage(string? name = null)
        {
            var page = new ScadaPage();

            page.Name = string.IsNullOrWhiteSpace(name) ? NextPageName() : name!.Trim();
            page.AddLayer(); // 先建好图层再进集合：让订阅者看到的画面永远是"完整"的

            _pages.Add(page);
            return page;
        }

        #region 画面管理（删除 / 重命名 / 排序）

        /// <summary>
        /// 删除画面。<b>只拒绝"删到没有任何画面"</b>，非空画面照删。
        ///
        /// 这里与 <see cref="ScadaPage.TryRemoveLayer"/> 的口径刻意不同：删图层时用户只想丢掉
        /// 一个分组开关，图元被他当作资产留着，所以模型必须拒绝；删画面则意味着"这一整屏我都不要了"，
        /// 拒绝非空画面等于功能不可用。因此这里只守住最后一条底线，界面侧务必弹确认框
        /// （工程上没有撤销，误删就是真丢了）。
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

            _pages.Remove(page);

            // 删掉的正是启动画面时顺手清空指定：留着那个 Id 是个指向不存在画面的幽灵引用，
            // 虽然 ResolveStartupPage 的回落能兜住显示，但属性面板上会一排全不勾、
            // 而用户并不知道"其实是因为启动画面被删了"。清空之后行为一样（都回落第一页），
            // 差别只在于配置状态是诚实的。
            ClearStartupPage(page);

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

            page.Name = target;
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

            _pages.Move(from, targetIndex);
            return true;
        }

        #endregion

        /// <summary>
        /// 变量改名后的引用刷新：递归到所有画面的所有绑定。返回被改动的绑定条数。
        ///
        /// 这是改名级联在画面侧的入口（与 <c>IReadOnlyWorkspaceContext.OnVariableRenamed</c>
        /// 里刷新流程连线、监视项并列）。按 Id 寻址的绑定只刷新展示名，
        /// 旧数据（没有 Id）在按名命中时顺带把 Id 补回来。
        /// </summary>
        /// <param name="variableId">改名变量的稳定身份</param>
        /// <param name="oldName">改名前的旧名</param>
        /// <param name="newName">改名后的新名</param>
        public int RefreshVariableReferences(Guid variableId, string? oldName, string newName)
        {
            int changed = 0;

            foreach (var page in _pages)
                changed += page.RefreshVariableReferences(variableId, oldName, newName);

            return changed;
        }

        /// <summary>
        /// 补发缺失的稳定身份（画面/图层/图元及其归属，旧数据迁移）。返回补发的处数。
        /// 加载流程在读文件后调用一次即可（见 SolutionService 的加载链路）。
        /// 图元归属的补齐规则见 <see cref="ScadaPage.EnsureIdentity"/>。
        /// </summary>
        public int EnsureIdentity()
        {
            int repaired = 0;

            foreach (var page in _pages)
                repaired += page.EnsureIdentity();

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
