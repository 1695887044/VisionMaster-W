using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Mvvm;
using VisionMaster.Scada.Controls;

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
    /// 图元工具箱：把 <see cref="ElementRegistry"/> 里的图元按分类列出来，供拖到画布上。
    ///
    /// 这个视图模型<b>只读注册表、不认识画面</b>——它不知道当前是哪一页、不知道选中了谁，
    /// 也不负责往画面里放东西（放东西是画布那头的 <c>Drop</c> 干的）。
    /// 所以它没有工作区依赖、没有事件订阅、不需要 Activate/Deactivate 那套可逆挂摘，
    /// 面板随 AvalonDock 装卸几次都不会漏摘，也不会有"换方案后工具箱还挂着旧状态"的问题。
    ///
    /// 新增一类图元时这里一行不改：图元是注册出来的，不是在这里列出来的。
    /// 这条是工具箱最重要的性质——它决定了"厂商扩展图元库"和"我们自己做扩展"走同一条路。
    /// </summary>
    public class ScadaToolboxViewModel : BindableBase
    {
        /// <summary>启动时快照的全量图元（注册表在程序生命周期内不再新增，故只取一次）</summary>
        private readonly IReadOnlyList<ElementDescriptor> _all;

        private string _searchText = string.Empty;

        public ScadaToolboxViewModel()
        {
            _all = ElementRegistry.All;
            Groups = BuildGroups(_all);
            TotalCount = _all.Count;
        }

        /// <summary>按分类分组的图元（搜索框为空时是全量）</summary>
        public IReadOnlyList<ScadaToolboxGroup> Groups { get; private set; }

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
