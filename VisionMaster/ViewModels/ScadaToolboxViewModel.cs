using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Commands;
using Prism.Mvvm;
using UI.CustomControl;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 工具箱分组：一个分类（"基础""指示""操作"）和它下面的图元。
    ///
    /// 条目直接放 <see cref="ElementDescriptor"/> 而不包一层 item 视图模型：
    /// 描述符本身是不可变的注册数据，工具箱里也没有任何"这一条的编辑态"要存
    /// （选中态在画布那边，不在这里）。包一层只会多出一份要跟着注册表同步的副本。
    /// </summary>
    public sealed class ScadaToolboxGroup
    {
        public ScadaToolboxGroup(string name, IReadOnlyList<ElementDescriptor> items)
        {
            Name = name;
            Items = items;
        }

        /// <summary>分类名（注册表里的 <c>Category</c>）</summary>
        public string Name { get; }

        /// <summary>该分类下的图元</summary>
        public IReadOnlyList<ElementDescriptor> Items { get; }
    }

    /// <summary>
    /// 图元工具箱：把 <see cref="ElementRegistry"/> 里的图元按分类列出来供拖到画布上，
    /// 另外列出「我的模板」（<see cref="ScadaTemplateStore"/>）。
    ///
    /// 它<b>只读注册表与模板库、不认识画面</b>——不知道当前是哪一页、不知道选中了谁，
    /// 也不负责往画面里放东西：图元与模板的落点都是画布那头的 <c>Drop</c> 算的，
    /// 两边只靠 <c>ScadaDrag</c> 那一个协议传递"要放什么"。
    /// 所以它没有工作区依赖，也就不会有"换方案之后工具箱还挂着旧状态"的问题。
    ///
    /// 唯一一处订阅是模板库的 <see cref="ScadaTemplateStore.Changed"/>，走
    /// <see cref="Attach"/>/<see cref="Detach"/> 这对可逆挂摘（视图入树/离树时调）：
    /// 存模板的入口在画布的右键菜单里，两处互不认识，只能靠这个通知把"存完立刻看得见"接起来。
    /// 成对挂摘是硬要求——<see cref="ScadaTemplateStore.Shared"/> 是静态的，只挂不摘就是泄漏。
    ///
    /// 新增一类图元时这里一行不改：图元是注册出来的，不是在这里列出来的。
    /// 这条是工具箱最重要的性质——它决定了"厂商扩展图元库"和"我们自己做扩展"走同一条路。
    /// </summary>
    public class ScadaToolboxViewModel : BindableBase
    {
        /// <summary>启动时快照的全量图元（注册表在程序生命周期内不再新增，故只取一次）</summary>
        private readonly IReadOnlyList<ElementDescriptor> _all;

        private string _searchText = string.Empty;

        /// <summary>是否已订阅模板库（<see cref="Attach"/> 幂等用：视图的 Loaded 可能来第二次）</summary>
        private bool _attached;

        /// <summary>库里的全量模板（未过滤）</summary>
        private IReadOnlyList<ScadaTemplateInfo> _allTemplates = Array.Empty<ScadaTemplateInfo>();

        /// <summary>当前显示的模板（受搜索框过滤）</summary>
        private IReadOnlyList<ScadaTemplateInfo> _templates = Array.Empty<ScadaTemplateInfo>();

        public ScadaToolboxViewModel()
        {
            _all = ElementRegistry.All;
            Groups = BuildGroups(_all);
            TotalCount = _all.Count;

            RenameTemplateCommand = new DelegateCommand<ScadaTemplateInfo>(OnRenameTemplate, CanEditTemplate);
            DeleteTemplateCommand = new DelegateCommand<ScadaTemplateInfo>(OnDeleteTemplate, CanEditTemplate);
        }

        /// <summary>按分类分组的图元（搜索框为空时是全量）</summary>
        public IReadOnlyList<ScadaToolboxGroup> Groups { get; private set; }

        /// <summary>
        /// 「我的模板」清单（按名字排序，来自 <see cref="ScadaTemplateStore.Shared"/>）。
        ///
        /// 直接绑 <see cref="ScadaTemplateInfo"/> 而不包一层行视图模型：它是 <c>record</c>、
        /// 本身不可变，行上也没有"这一行的编辑态"要存（改名走命令、改完整份重读），
        /// 与图元条目直接绑 <see cref="ElementDescriptor"/> 是同一条理由。
        /// </summary>
        public IReadOnlyList<ScadaTemplateInfo> Templates => _templates;

        /// <summary>模板条数（分组标题上那个数字；搜索时是命中数，与图元分组同一口径）</summary>
        public int TemplateCount => _templates.Count;

        /// <summary>有没有可显示的模板（没有时那一栏整块收起来——一份都没存过、或搜索词一个都没命中）</summary>
        public bool HasTemplates => _templates.Count > 0;

        /// <summary>重命名模板（模板行右键）。重名会被拒绝并给出原因（见 <see cref="ScadaTemplateStore.TryRename"/>）</summary>
        public DelegateCommand<ScadaTemplateInfo> RenameTemplateCommand { get; }

        /// <summary>删除模板（模板行右键）。删之前问一次——模板是用户自己攒的，没有撤销</summary>
        public DelegateCommand<ScadaTemplateInfo> DeleteTemplateCommand { get; }

        /// <summary>
        /// 入树：订阅模板库变更并立刻读一次（幂等）。
        ///
        /// 与 <c>ScadaLayerViewModel.Activate</c> 同款——面板被 AvalonDock 重新挂回可视树时
        /// Loaded 会再来一次，所以必须能重复调。
        /// </summary>
        public void Attach()
        {
            if (_attached) return;
            _attached = true;

            ScadaTemplateStore.Shared.Changed += OnTemplatesChanged;
            RefreshTemplates();
        }

        /// <summary>离树：摘干净（幂等），让本视图模型能被回收</summary>
        public void Detach()
        {
            if (!_attached) return;
            _attached = false;

            ScadaTemplateStore.Shared.Changed -= OnTemplatesChanged;
        }

        private void OnTemplatesChanged(object? sender, EventArgs e) => RefreshTemplates();

        /// <summary>
        /// 重读模板清单。
        ///
        /// 整份重读，而不是"在本地列表上增删改一格"：库里的排序、重名避让之后的真名
        /// 都只有它自己知道，本地跟着改就等于把那份口径抄了第二遍，抄漏一处就会出现
        /// "界面上叫阀门、库里叫阀门_2"。模板是人手攒的量级，重读的代价可以忽略。
        /// </summary>
        public void RefreshTemplates()
        {
            _allTemplates = ScadaTemplateStore.Shared.Templates;
            ApplyTemplateFilter();
        }

        /// <summary>
        /// 按搜索词过滤模板。
        ///
        /// 与图元分组同一口径（命中数进标题、一条都没命中就整块收起来）：
        /// 一个面板里两套过滤规则会让人以为"模板搜不了"——现场攒到几十份模板时，
        /// 想找"三通阀"那份只能一行行扫，而搜索框就在正上方。
        /// 模板身上只有名字可搜（图元数是个数字，搜它没有意义）。
        /// </summary>
        private void ApplyTemplateFilter()
        {
            string keyword = _searchText.Trim();

            _templates = keyword.Length == 0
                ? _allTemplates
                : _allTemplates.Where(t => Contains(t.Name, keyword)).ToArray();

            RaisePropertyChanged(nameof(Templates));
            RaisePropertyChanged(nameof(TemplateCount));
            RaisePropertyChanged(nameof(HasTemplates));
        }

        private static bool CanEditTemplate(ScadaTemplateInfo? template) => template != null;

        private void OnRenameTemplate(ScadaTemplateInfo? template)
        {
            if (template == null) return;

            var (confirmed, value) = EasyDialog.ShowTextInputSync("重命名模板", template.Name);
            if (!confirmed) return;

            // 成功后不在这里刷列表：写入方（库）会发 Changed，由 Attach 那一路统一刷。
            // 刷新点只有一个，"某个入口忘了刷"这种缺陷在结构上就不可能发生。
            if (!ScadaTemplateStore.Shared.TryRename(template.TemplateId, value, out string? error))
                EasyDialog.ShowSync("重命名模板失败", error ?? "重命名失败");
        }

        private void OnDeleteTemplate(ScadaTemplateInfo? template)
        {
            if (template == null) return;

            // 问一次再删：模板是用户自己一个个攒起来的，删掉没有撤销（不进撤销栈，也不进回收站）。
            if (!EasyDialog.ShowSync("删除模板", $"确定删除模板「{template.Name}」吗？此操作不可撤销。"))
                return;

            if (!ScadaTemplateStore.Shared.TryRemove(template.TemplateId, out string? error))
                EasyDialog.ShowSync("删除模板失败", error ?? "删除失败");
        }

        /// <summary>图元总数（标题栏那句"共 N 种图元"）</summary>
        public int TotalCount { get; }

        /// <summary>过滤关键字（匹配显示名、类型键、分类、说明，任一处含即可）</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!SetProperty(ref _searchText, value ?? string.Empty)) return;

                Groups = BuildGroups(string.IsNullOrWhiteSpace(_searchText)
                    ? _all
                    : _all.Where(Matches).ToArray());

                RaisePropertyChanged(nameof(Groups));

                // 模板跟着同一个搜索词走（见 ApplyTemplateFilter）
                ApplyTemplateFilter();
            }
        }

        private bool Matches(ElementDescriptor descriptor)
        {
            string keyword = _searchText.Trim();

            return Contains(descriptor.DisplayName, keyword)
                || Contains(descriptor.TypeKey, keyword)
                || Contains(descriptor.Category, keyword)
                || Contains(descriptor.Description, keyword);
        }

        private static bool Contains(string? source, string keyword)
            => source != null && source.Contains(keyword, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 分组。注册表已经按"分类 → 显示名"排过序，这里靠 <c>GroupBy</c> 的
        /// 稳定顺序（首现次序）落组，不再二次排序——分类的先后是注册表定义的，
        /// 工具箱自己排一遍就会出现"两处顺序不一致"。
        /// </summary>
        private static IReadOnlyList<ScadaToolboxGroup> BuildGroups(IReadOnlyList<ElementDescriptor> descriptors)
            => descriptors
                .GroupBy(d => d.Category, StringComparer.CurrentCulture)
                .Select(g => new ScadaToolboxGroup(g.Key, g.ToArray()))
                .ToArray();
    }
}
