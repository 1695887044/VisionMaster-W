using Core.Interfaces;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 工具箱 ViewModel：插件工具分组展示 + 关键字过滤 + 收藏 + 列表/网格视图模式。
    ///
    /// 一条硬约束贯穿全类：<see cref="ToolboxGroupViewModel.Children"/> 里的元素必须是
    /// **原始 ToolItemModel 实例**（不是包裹 VM）。工具箱是拖拽源，gong 的默认拖拽处理器
    /// 把条目本身当 DataObject 投放，放置端（FlowCanvasView.FindDraggedTool）硬认
    /// <c>ToolItemModel</c> 类型；包一层 wrapper 就会在放置端拿到 null，拖拽静默失效。
    /// 所以"每项可变状态"（收藏态）只能留在 VM 里、靠转换器回查，不能挂到条目上。
    /// </summary>
    public class ToolViewModel : BindableBase
    {
        private readonly IPluginProvider pluginProvider;

        /// <summary>
        /// 软件级配置（AppConfig.json）。收藏清单从这里读初值、改动写回并立即落盘。
        /// 断言宿主 / 设计器不传：收藏走纯内存、不落盘——AppSettingsService 构造会在
        /// 程序目录写 AppConfig.json，不能让 headless 进程碰它（该服务的注释有案）。
        /// </summary>
        private readonly AppSettingsService? settings;

        /// <summary>
        /// 日志（可选）。只用于"收藏落盘失败"这一条：落盘失败不该弹窗、更不该崩，
        /// 但也不能无声无息——写一条告警，排查时看得见。
        /// </summary>
        private readonly ILogService? log;

        /// <summary>分类分组全量（不含置顶的「收藏」组，它在 ApplyFilter 里现拼）</summary>
        private readonly List<ToolboxGroupViewModel> toolBarSource = new();

        /// <summary>收藏的算子身份（<see cref="ToolItemModel.ModuleTypeName"/>）。顺序由注册顺序决定，见 BuildFavoritesGroup</summary>
        private readonly HashSet<string> _favorites = new(StringComparer.Ordinal);

        /// <summary>置顶「收藏」组的固定名（也是它在展开态字典里的键）</summary>
        private const string FavoritesGroupName = "收藏";

        /// <summary>
        /// 分组的展开态（键 = 分组名）。**只记用户手动切换**——过滤重建期的赋值是程序行为，不写进来
        /// （见 <see cref="_applyingFilter"/>）。分组实例每次过滤都重建，状态必须留在 VM 里才不丢。
        /// </summary>
        private readonly Dictionary<string, bool> _expandedState = new(StringComparer.Ordinal);

        /// <summary>true = 正在重建分组（此时 <see cref="ToolboxGroupViewModel.IsExpanded"/> 的赋值是程序行为）</summary>
        private bool _applyingFilter;

        /// <summary>
        /// 上一次过滤是否处于"有搜索词"状态（null = 还没过滤过）。
        ///
        /// 只在它翻转（空 ↔ 非空）时重新套用分组的展开态：搜索期强制展开、平时按用户折叠偏好。
        /// 中间那些按键不再重复赋值——否则用户搜索时手动折叠某个分组，下一键又被撑开。
        /// </summary>
        private bool? _lastSearchActive;

        /// <summary>
        /// 过滤后的插件分组（SearchText / 收藏 变化时重建）。
        /// 有收藏时 [0] 恒为「收藏」组——注意它**不计入 <see cref="TotalCount"/>**
        /// （总数是"插件个数"，收藏是同一批插件的另一种视角，计进去就成了重复计数）。
        /// </summary>
        public ObservableCollection<ToolboxGroupViewModel> FilteredToolBarSource { get; } = new();

        private int _totalCount;
        /// <summary>工具箱算子总数（标题栏胶囊徽标；搜索时为命中数，收藏组不计）</summary>
        public int TotalCount
        {
            get => _totalCount;
            private set => SetProperty(ref _totalCount, value);
        }

        private string searchText = string.Empty;
        public string SearchText
        {
            get => searchText;
            set
            {
                if (SetProperty(ref searchText, value))
                {
                    ApplyFilter();
                }
            }
        }

        private bool _isGridView;
        /// <summary>true = 网格视图（2 列卡片），false = 列表视图（单列行）</summary>
        public bool IsGridView
        {
            get => _isGridView;
            private set
            {
                if (SetProperty(ref _isGridView, value))
                {
                    RaisePropertyChanged(nameof(IsListView));
                }
            }
        }

        /// <summary>列表视图（= !IsGridView）。给"列表/网格"两个切换按钮的选中态用，
        /// 免得在 XAML 里为一个取反再引入一个转换器。</summary>
        public bool IsListView => !_isGridView;

        private int _favoritesVersion;
        /// <summary>
        /// 收藏集合的版本号。唯一用途是**当刷新信号**：星标的 MultiBinding
        /// （见 <see cref="IsFavoriteConverter"/>）把它当第三路输入，一变就重算。
        /// 不带它的话，切换收藏后界面上那颗星不会重绘。
        /// </summary>
        public int FavoritesVersion
        {
            get => _favoritesVersion;
            private set => SetProperty(ref _favoritesVersion, value);
        }

        /// <summary>当前有没有收藏（空态提示行"悬停条目点亮星标收藏"据此显隐）</summary>
        public bool HasFavorites => _favorites.Count > 0;

        /// <summary>列表/网格互切</summary>
        public ICommand ToggleViewModeCommand { get; }

        /// <summary>切到指定视图模式；参数是 "List" / "Grid"（其它值按"切换"处理）</summary>
        public ICommand SetViewModeCommand { get; }

        /// <summary>收藏/取消收藏；参数是条目 <see cref="ToolItemModel"/></summary>
        public ICommand ToggleFavoriteCommand { get; }

        public ToolViewModel(IPluginProvider pluginProvider, AppSettingsService? settings = null, ILogService? log = null)
        {
            this.pluginProvider = pluginProvider;
            this.settings = settings;
            this.log = log;

            // 两个命令都吃 object?：CommandParameter 在 XAML 里是编译期不检查的表达式
            // （{Binding} / 字面量字符串），写成强类型泛型会在拿到别的类型时抛 InvalidCastException
            // （2026-10-10 真机崩溃先例，见 AGENTS.md R27）。判型一律在处理器首行做。
            ToggleViewModeCommand = new DelegateCommand<object?>(_ => IsGridView = !IsGridView);
            SetViewModeCommand = new DelegateCommand<object?>(OnSetViewMode);
            ToggleFavoriteCommand = new DelegateCommand<object?>(OnToggleFavorite);

            LoadFavorites();
            loadTools();
        }

        /// <summary>该条目是否已收藏（星标转换器的查询入口）</summary>
        public bool IsFavorite(ToolItemModel? item)
            => item != null && !string.IsNullOrEmpty(item.ModuleTypeName) && _favorites.Contains(item.ModuleTypeName);

        private void OnSetViewMode(object? parameter)
        {
            switch (parameter as string)
            {
                case "Grid":
                    IsGridView = true;
                    break;
                case "List":
                    IsGridView = false;
                    break;
                default:
                    IsGridView = !IsGridView;
                    break;
            }
        }

        private void OnToggleFavorite(object? parameter)
        {
            if (parameter is not ToolItemModel item) return;

            var key = item.ModuleTypeName;
            if (string.IsNullOrEmpty(key)) return;

            // 同一把钥匙：已在集合里就是"取消收藏"
            if (!_favorites.Add(key)) _favorites.Remove(key);

            // 落盘失败不能让"点星"崩掉界面（2026-10-14 真机：AppConfig.json 被以
            // "允许读写、不允许删除"方式占用时 File.Replace 抛 1177，栈穿到这里）。
            // 内存态照常生效，只记告警——与"方案清单/系统参数/运行窗口"几个 Save 调用方同一条约定：
            // 它们也是 catch 后提示，不把异常抛进 UI 线程。
            try
            {
                settings?.SyncToolboxFavorites(_favorites);
            }
            catch (Exception ex)
            {
                log?.Warn($"[工具箱] 收藏清单写入 AppConfig.json 失败，本次仅在内存生效（重启后可能丢失）：{ex.Message}");
            }

            FavoritesVersion++;
            RaisePropertyChanged(nameof(HasFavorites));

            // 收藏组要整体重排（置顶 / 消失），所以整份重建而不是只改一行
            ApplyFilter();
        }

        /// <summary>
        /// 从配置读收藏，并**按当前已注册插件做存在性过滤**：插件升级（类型名变）或卸载后，
        /// 老配置里的键会变成拖不动的死条目；这里丢掉它们，等于"配置自愈"。
        /// </summary>
        private void LoadFavorites()
        {
            _favorites.Clear();

            var saved = settings?.Current?.Toolbox?.Favorites;
            if (saved == null || saved.Count == 0) return;

            var known = new HashSet<string>(pluginProvider.ModulePlugins.Keys, StringComparer.Ordinal);
            foreach (var id in saved)
            {
                if (!string.IsNullOrEmpty(id) && known.Contains(id)) _favorites.Add(id);
            }
        }

        /// <summary>
        /// 按关键字过滤：匹配插件名/描述/分类（Category=分组名，命中即整组保留），无匹配项的分组隐藏。
        /// 收藏组一并参与过滤（搜出来的收藏项才出现在收藏组里），但它不计入 TotalCount。
        ///
        /// **原地更新，不整份重建**：先算蓝图（<see cref="BuildPlan"/>，纯计算），再把界面上的分组
        /// 对齐到蓝图——分组还在就复用同一个 VM、只增删它 <see cref="ToolboxGroupViewModel.Children"/>
        /// 里变化的条目；分组没了才移除、新出现才新建。
        ///
        /// 为什么必须这样：旧实现是"清空 + 重建整份集合"，每敲一个字符都把工具箱整棵可视树
        /// 拆掉重来（42 个条目全命中时实测单键 150~200 ms，全在 UI 线程）。而搜索的绝大多数按键
        /// 只是"多打一个字"，命中的那些行本来就该原样留在屏幕上——容器复用之后，
        /// 单键代价从"O(全部条目)"降到"O(增删的条目)"。
        /// </summary>
        private void ApplyFilter()
        {
            var keyword = searchText?.Trim() ?? string.Empty;
            var hasKeyword = keyword.Length > 0;

            var plan = BuildPlan(keyword, hasKeyword);
            var searchModeChanged = _lastSearchActive != hasKeyword;

            // 重建期的 IsExpanded 赋值是程序行为（初值 / 搜索强制展开），不能记进用户的折叠偏好
            _applyingFilter = true;
            try
            {
                // 1) 先摘掉这次不该出现的分组（连同它的条目容器一起离场）
                var wanted = new HashSet<string>(plan.Select(g => g.Name), StringComparer.Ordinal);
                for (int i = FilteredToolBarSource.Count - 1; i >= 0; i--)
                {
                    if (!wanted.Contains(FilteredToolBarSource[i].Name))
                        FilteredToolBarSource.RemoveAt(i);
                }

                // 2) 逐组归位：还在的复用 VM、原地同步条目；缺的新建
                int matchedTotal = 0;
                for (int i = 0; i < plan.Count; i++)
                {
                    var entry = plan[i];

                    var group = FindLiveGroup(entry.Name);
                    if (group == null)
                    {
                        group = CreateGroup(entry);
                        group.IsExpanded = entry.IsExpanded;
                    }
                    else if (searchModeChanged)
                    {
                        // 搜索态翻转才重新套用：中间按键不动它，用户手动折叠的分组保持折叠
                        group.IsExpanded = entry.IsExpanded;
                    }

                    SyncChildren(group.Children, entry.Items);

                    int at = FilteredToolBarSource.IndexOf(group);
                    if (at < 0) FilteredToolBarSource.Insert(i, group);
                    else if (at != i) FilteredToolBarSource.Move(at, i);

                    // 收藏组是同一批插件的另一种视角，计进总数就是重复计数
                    if (!entry.IsFavorites) matchedTotal += entry.Items.Count;
                }

                TotalCount = matchedTotal;
            }
            finally
            {
                _applyingFilter = false;
                _lastSearchActive = hasKeyword;
            }
        }

        /// <summary>分组展开态初值：用户手动切过就按用户的，否则**默认折叠**（「收藏」组默认展开）。</summary>
        private bool ResolveExpanded(string name)
            => _expandedState.TryGetValue(name, out var v) ? v : name == FavoritesGroupName;

        private void OnGroupExpandedChanged(ToolboxGroupViewModel group, bool isExpanded)
        {
            if (_applyingFilter) return;   // 只记用户的手动切换，程序赋值不算
            _expandedState[group.Name] = isExpanded;
        }

        /// <summary>
        /// 算出"这次该显示成什么样"：分类分组（只留命中非空的）+ 置顶的收藏组。
        /// 纯计算、不产生任何通知，所以"算一遍看看结果变没变"可以随便算。
        /// </summary>
        private List<GroupPlan> BuildPlan(string keyword, bool hasKeyword)
        {
            var plan = new List<GroupPlan>();

            foreach (var group in toolBarSource)
            {
                var matched = hasKeyword
                    ? group.Children.Where(t => Matches(t, keyword)).ToList()
                    : group.Children.ToList();

                if (matched.Count == 0) continue;

                plan.Add(new GroupPlan(
                    group.Name,
                    group.Icon,
                    group.AccentBrush,
                    IsFavorites: false,
                    // 默认折叠；搜索时强制展开（否则命中结果全压在折叠头下面，等于搜了个寂寞）
                    IsExpanded: hasKeyword || ResolveExpanded(group.Name),
                    matched));
            }

            // 收藏组置顶：非空才出现
            var favorites = BuildFavoritesPlan(keyword, hasKeyword);
            if (favorites != null) plan.Insert(0, favorites.Value);

            return plan;
        }

        /// <summary>
        /// 拼置顶的「收藏」组：按**当前注册顺序**扫一遍（而不是按加入收藏的先后）——
        /// 收藏键在 HashSet 里本来就没有顺序，而注册顺序是稳定的、每次重建结果一致，
        /// 界面不会因为"上次先点的哪个"而跳动。
        /// 搜索时同样按关键字筛（收藏的是算子，不是搜索条件的豁免）。
        /// </summary>
        private GroupPlan? BuildFavoritesPlan(string keyword, bool hasKeyword)
        {
            if (_favorites.Count == 0) return null;

            var items = new List<ToolItemModel>();
            foreach (var source in toolBarSource)
            {
                foreach (var tool in source.Children)
                {
                    if (!IsFavorite(tool)) continue;
                    if (hasKeyword && !Matches(tool, keyword)) continue;
                    items.Add(tool);
                }
            }

            if (items.Count == 0) return null;

            return new GroupPlan(
                FavoritesGroupName,
                ToolboxCategoryVisuals.FavoriteIcon,
                ToolboxCategoryVisuals.FavoriteBrush,
                IsFavorites: true,
                IsExpanded: ResolveExpanded(FavoritesGroupName),
                items);
        }

        /// <summary>新建一个分组 VM（只在"这个分组这次才出现"时调；已在屏幕上的分组一律复用）</summary>
        private ToolboxGroupViewModel CreateGroup(GroupPlan plan) => new()
        {
            Name = plan.Name,
            Icon = plan.Icon,
            AccentBrush = plan.AccentBrush,
            IsFavorites = plan.IsFavorites,
            ExpandedChanged = OnGroupExpandedChanged,
        };

        /// <summary>当前挂在界面上的同名分组（没有 = 这次要新建）</summary>
        private ToolboxGroupViewModel? FindLiveGroup(string name)
        {
            foreach (var group in FilteredToolBarSource)
            {
                if (string.Equals(group.Name, name, StringComparison.Ordinal)) return group;
            }

            return null;
        }

        /// <summary>
        /// 把分组里的条目对齐到 <paramref name="desired"/>——**原地增删，不换集合实例**。
        ///
        /// 两个集合都是**同一个全量注册顺序**的子序列（过滤只会筛掉、不会重排），所以：
        /// 先倒序删掉不再命中的，再把缺的按位置补上。留下来的条目连容器一起留在屏幕上，
        /// 这正是"只增删变化的条目"这条改造的落点。
        /// </summary>
        private static void SyncChildren(ObservableCollection<ToolItemModel> target, List<ToolItemModel> desired)
        {
            // 按引用比：同一实例在两个分组里各出现一次（收藏组与分类组），值相等不代表同一行
            var wanted = new HashSet<ToolItemModel>(desired, ReferenceEqualityComparer.Instance);

            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!wanted.Contains(target[i])) target.RemoveAt(i);
            }

            for (int i = 0; i < desired.Count; i++)
            {
                if (i < target.Count && ReferenceEquals(target[i], desired[i])) continue;

                int at = -1;
                for (int j = i; j < target.Count; j++)
                {
                    if (ReferenceEquals(target[j], desired[i])) { at = j; break; }
                }

                if (at >= 0) target.Move(at, i);
                else target.Insert(i, desired[i]);
            }
        }

        /// <summary>
        /// 一次过滤的"蓝图"：分组 + 这次要显示的条目 + 展开态。
        /// 与 <see cref="ToolboxGroupViewModel"/> 的区别是它没有 ObservableCollection、不产生通知，
        /// 所以"算一遍看看结果变没变"可以随便算，不用碰可视树。
        /// </summary>
        private readonly record struct GroupPlan(
            string Name,
            string Icon,
            Brush AccentBrush,
            bool IsFavorites,
            bool IsExpanded,
            List<ToolItemModel> Items);


        private static bool Matches(ToolItemModel tool, string keyword)
            => Contains(tool.Name, keyword)
               || Contains(tool.Description, keyword)
               || Contains(tool.Category, keyword);

        private static bool Contains(string? source, string keyword)
        {
            return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void loadTools()
        {
            toolBarSource.Clear();

            // 全量重载：界面上已有的分组全部作废，从空集合重新对齐
            FilteredToolBarSource.Clear();
            _lastSearchActive = null;

            pluginProvider.ModulePlugins.GroupBy(t => t.Value.Category).ToList().ForEach(g =>
            {
                var group = new ToolboxGroupViewModel
                {
                    Name = g.Key,
                    Icon = ToolboxCategoryVisuals.IconFor(g.Key),
                    AccentBrush = ToolboxCategoryVisuals.BrushFor(g.Key),
                    ExpandedChanged = OnGroupExpandedChanged,
                };
                g.ToList().ForEach(p => group.Children.Add(p.Value));
                toolBarSource.Add(group);
            });

            ApplyFilter();
        }
    }
}
