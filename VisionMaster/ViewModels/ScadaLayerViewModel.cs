using System;
using System.Collections.Generic;
using System.ComponentModel;
using Prism.Commands;
using Prism.Mvvm;
using UI.CustomControl;
using VisionMaster.Scada;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 图层列表里的一行：一份<b>不可变快照</b>。
    ///
    /// 为什么不直接把 <see cref="ScadaLayer"/> 绑到行上：
    /// ① 行上要显示的东西比图层自己多——"本层有几个图元""能不能删""能不能上移""是不是当前选中图元所在的层"
    ///    都是<b>画面级</b>的算出来的量，图层对象上根本没有，硬塞给它就是把编辑器知识写进领域模型；
    /// ② 快照能被断言直接读（<c>Items[0].CanRemove</c> 就是一句布尔判断），
    ///    而"控件模板里的眼睛图标到底翻没翻过来"只能靠离屏渲染去数——那是最后一道兜底，不该是唯一一道。
    ///
    /// 快照的刷新时机由 <see cref="ScadaLayerViewModel"/> 统一负责（见那里的 <c>Version</c> 订阅），
    /// 本类型自己不订阅任何东西，也就没有"行还活着、模型已经换了"这类劈叉。
    ///
    /// 图标字形放在这里，理由与 <see cref="ScadaMenuItem.Icon"/> 完全一致：
    /// "这一行长什么样"是视图模型层的唯一一份知识，宿主只负责按字体把字形渲染出来。
    /// </summary>
    public sealed class ScadaLayerItem
    {
        /// <summary>Font Awesome 6 Pro Solid：眼睛（本层可见）</summary>
        public const string EyeGlyph = "\uF06E";

        /// <summary>眼睛加斜杠（本层已隐藏）</summary>
        public const string EyeSlashGlyph = "\uF070";

        /// <summary>闭锁（本层已锁定）</summary>
        public const string LockGlyph = "\uF023";

        /// <summary>开锁（本层可编辑）</summary>
        public const string UnlockGlyph = "\uF3C1";

        public ScadaLayerItem(
            ScadaLayer layer,
            int elementCount,
            bool canRemove,
            bool canMoveUp,
            bool canMoveDown,
            bool containsSelection)
        {
            Layer = layer;
            ElementCount = elementCount;
            CanRemove = canRemove;
            CanMoveUp = canMoveUp;
            CanMoveDown = canMoveDown;
            ContainsSelection = containsSelection;
        }

        /// <summary>这一行代表的图层（命令的落点；改名/显隐/锁定都会反映到它身上）</summary>
        public ScadaLayer Layer { get; }

        public string Name => Layer.Name;

        public bool IsVisible => Layer.IsVisible;

        public bool IsLocked => Layer.IsLocked;

        /// <summary>本层图元数（<see cref="ScadaPage.CountElements"/>）</summary>
        public int ElementCount { get; }

        /// <summary>能不能删：空层且画面还剩别的层（<see cref="ScadaPage.CanRemoveLayer"/>）</summary>
        public bool CanRemove { get; }

        public bool CanMoveUp { get; }

        public bool CanMoveDown { get; }

        /// <summary>当前选中的图元是不是落在本层（行上做高亮用，省得用户自己找）</summary>
        public bool ContainsSelection { get; }

        /// <summary>眼睛图标：可见是睁眼、隐藏是斜杠眼</summary>
        public string VisibilityIcon => IsVisible ? EyeGlyph : EyeSlashGlyph;

        public string VisibilityTip => IsVisible ? $"隐藏图层 [{Name}]" : $"显示图层 [{Name}]";

        /// <summary>锁图标：锁定是闭锁、可编辑是开锁</summary>
        public string LockIcon => IsLocked ? LockGlyph : UnlockGlyph;

        public string LockTip => IsLocked ? $"解锁图层 [{Name}]" : $"锁定图层 [{Name}]";

        /// <summary>数量文案：0 个图元直接写"空"——它是"能删"的唯一前提，值得一个显眼的说法</summary>
        public string ElementCountText => ElementCount == 0 ? "空" : $"{ElementCount} 个图元";
    }

    /// <summary>
    /// 图层面板：当前画面的图层列表 + 显隐/锁定 + 上移/下移 + 新建/重命名/删除。
    ///
    /// <b>它管的是"图层自己"，不是"图元归到哪一层"</b>——后者是画布右键菜单里那一栏
    ///（见 <see cref="ScadaEditorViewModel.BuildElementContextMenu"/>）。两处分工刻意不重叠：
    /// 右键菜单回答"选中的东西在哪一层"，本面板回答"这张画面一共有哪几层、它们各自开没开"。
    ///
    /// 三条口径：
    /// 1. <b>写入口只有 <see cref="ScadaPage.Try*"/> 家族与 <see cref="ScadaLayer.BeginEdit"/></b>。
    ///    本类不自己动 <c>Layers</c> 集合、也不裸写 <c>IsVisible</c>——前者会绕过校验与中文错误文案，
    ///    后者会绕过撤销记录。显隐/锁定没有 <c>TrySet*</c> 是<b>设计如此</b>：图层对象上那两个属性
    ///    就是唯一的写入口，外面套一层 <see cref="ScadaLayer.BeginEdit"/> 即可（一次点按 = 一条撤销位）。
    /// 2. <b>刷新靠 <see cref="ScadaPage.Version"/> 一条订阅</b>。它的递增规则已经把"图层增删改名/显隐/锁定"
    ///    与"图元增删/改归属"全覆盖了（见 <c>ScadaPage.Version</c> 注释），
    ///    比分别盯两个集合 + 每个对象少一大截漏挂漏摘的机会。
    /// 3. <b>快照值不变就不刷 UI</b>（<see cref="ItemsEqual"/>）。拖动图元会逐帧抬 Version，
    ///    不挡一下就是每秒几十次重建列表 → ItemsControl 反复重建行容器 → 拖动肉眼可见地发涩。
    ///    注意"值没变"只挡 <c>Items</c> 的通知，命令的可用性仍然每次都重算（见 <see cref="RaiseAllCanExecute"/>）。
    /// </summary>
    public class ScadaLayerViewModel : BindableBase
    {
        private readonly ScadaEditorViewModel _editor;

        private bool _subscribed;
        private ScadaPage? _page;
        private IReadOnlyList<ScadaLayerItem> _items = Array.Empty<ScadaLayerItem>();
        private int _unassignedCount;

        public ScadaLayerViewModel(ScadaEditorViewModel editor)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));

            // 命令参数一律取行快照（XAML 里就是 CommandParameter="{Binding}"），
            // 落点从 item.Layer 取——快照只负责"带哪个图层进来"，判断与写入都回模型现读，
            // 避免"行是上一秒的快照、命令却按它执行"这类只有并发才复现的错。
            AddLayerCommand = new DelegateCommand(OnAddLayer, () => _page != null);

            ToggleVisibleCommand = new DelegateCommand<ScadaLayerItem>(OnToggleVisible, item => item?.Layer != null);
            ToggleLockCommand = new DelegateCommand<ScadaLayerItem>(OnToggleLock, item => item?.Layer != null);

            MoveLayerUpCommand = new DelegateCommand<ScadaLayerItem>(
                item => MoveLayer(item?.Layer, -1), item => CanMove(item?.Layer, -1));
            MoveLayerDownCommand = new DelegateCommand<ScadaLayerItem>(
                item => MoveLayer(item?.Layer, 1), item => CanMove(item?.Layer, 1));

            RenameLayerCommand = new DelegateCommand<ScadaLayerItem>(OnRenameLayer, item => item?.Layer != null);

            // 删除的可用性直接问模型：空层 + 至少还剩一层，判据只有 ScadaPage.CanRemoveLayer 一份
            RemoveLayerCommand = new DelegateCommand<ScadaLayerItem>(
                OnRemoveLayer, item => _page?.CanRemoveLayer(item?.Layer) == true);

            Activate();
        }

        /// <summary>当前画面的图层行（无画面时是空表）</summary>
        public IReadOnlyList<ScadaLayerItem> Items => _items;

        /// <summary>有没有画面可管（决定空态提示）</summary>
        public bool HasPage => _page != null;

        /// <summary>标题："图层 · 画面名"——面板和工具箱共用一列，标题里点名是哪张画面才不会看串</summary>
        public string HeaderText => _page is { } page ? $"图层 · {page.Name}" : "图层";

        /// <summary>右上角计数</summary>
        public string LayerCountText => $"共 {_page?.Layers.Count ?? 0} 层";

        /// <summary>
        /// 空态文案。分两种说法：<b>"没有画面"和"有画面但没图层"是两回事</b>——
        /// 前者要去开方案/选画面，后者只要点一下下面的"新建图层"。
        /// 用同一句话兜住两种情形，用户会照着做错的那一步去试。
        /// </summary>
        public string EmptyHint => _page == null
            ? "打开方案并选中画面后，这里会显示该画面的图层。"
            : "当前画面还没有图层。点下方「新建图层」即可添加。";

        /// <summary>未分层（含归属指向已删图层）的图元数</summary>
        public int UnassignedCount => _unassignedCount;

        public bool HasUnassigned => _unassignedCount > 0;

        public string UnassignedHint => $"另有 {_unassignedCount} 个图元未分层";

        /// <summary>
        /// 面板底部那句"防误判"文案的完整版（挂在悬停提示上）。
        ///
        /// 行内只放得下一行短句（"图层顺序不改变图元叠放"），但用户真正需要的是
        /// <b>"那我该怎么调前后"</b>——那句话在这里补上，指到画布右键菜单去。
        ///
        /// 为什么非要解释：<b>图层次序不参与叠放计算</b>（见 <see cref="ScadaLayer"/> 类头第 ③ 条），
        /// 谁盖住谁只由 <c>ScadaElement.ZIndex</c> 决定。不写出来的话，用户点几次"上移"
        /// 发现画面前后关系没变，会判定成"这个按钮坏了"——解释成本最低、收益最高的一句文案。
        /// </summary>
        public string OrderHintDetail =>
            "图层次序只决定列表排列，不改变图元前后叠放。\n要调整图元谁盖住谁，请选中图元后用画布右键菜单的「叠放次序」。";

        public DelegateCommand AddLayerCommand { get; }

        public DelegateCommand<ScadaLayerItem> ToggleVisibleCommand { get; }

        public DelegateCommand<ScadaLayerItem> ToggleLockCommand { get; }

        public DelegateCommand<ScadaLayerItem> MoveLayerUpCommand { get; }

        public DelegateCommand<ScadaLayerItem> MoveLayerDownCommand { get; }

        public DelegateCommand<ScadaLayerItem> RenameLayerCommand { get; }

        public DelegateCommand<ScadaLayerItem> RemoveLayerCommand { get; }

        /// <summary>挂接编辑器通知并对齐当前画面（幂等）</summary>
        public void Activate()
        {
            if (_subscribed) return;
            _subscribed = true;

            _editor.PropertyChanged += OnEditorPropertyChanged;
            Rebuild();
        }

        /// <summary>摘干净（幂等）；面板离树时调用，让旧画面不被本面板钉住</summary>
        public void Deactivate()
        {
            if (!_subscribed) return;
            _subscribed = false;

            _editor.PropertyChanged -= OnEditorPropertyChanged;
            BindPage(null);
        }

        private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // SelectedElement 也要接：行上的"当前选中图元在本层"高亮跟着它变。
            // Pages/Document 一并接住：换方案时编辑器会先把 SelectedPage 置空，
            // 但那之后可能直接赋新值而不经过 null，少接一条就会管到已经不在屏幕上的画面。
            if (e.PropertyName is null
                or nameof(ScadaEditorViewModel.SelectedPage)
                or nameof(ScadaEditorViewModel.SelectedElement)
                or nameof(ScadaEditorViewModel.Pages)
                or nameof(ScadaEditorViewModel.Document))
            {
                Rebuild();
            }
        }

        private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 只认 Version：图层增删改名/显隐锁定、图元增删改归属都会抬它（见 ScadaPage.Version 的递增规则）。
            if (e.PropertyName is null or nameof(ScadaPage.Version))
                Rebuild();
        }

        /// <summary>换画面订阅（幂等：还是同一张就不动，避免反复摘挂漏一层）</summary>
        private void BindPage(ScadaPage? page)
        {
            if (ReferenceEquals(_page, page)) return;

            if (_page != null)
                _page.PropertyChanged -= OnPagePropertyChanged;

            _page = page;

            if (_page != null)
                _page.PropertyChanged += OnPagePropertyChanged;
        }

        /// <summary>重算整份列表（值没变就不惊动 UI）</summary>
        private void Rebuild()
        {
            BindPage(_editor.SelectedPage);

            var page = _page;
            var items = page == null ? Array.Empty<ScadaLayerItem>() : BuildItems(page);

            if (!ItemsEqual(_items, items))
            {
                _items = items;
                RaisePropertyChanged(nameof(Items));
            }

            int unassigned = page == null ? 0 : CountUnassigned(page);

            if (_unassignedCount != unassigned)
            {
                _unassignedCount = unassigned;
                RaisePropertyChanged(nameof(UnassignedCount));
                RaisePropertyChanged(nameof(HasUnassigned));
                RaisePropertyChanged(nameof(UnassignedHint));
            }

            RaisePropertyChanged(nameof(HasPage));
            RaisePropertyChanged(nameof(HeaderText));
            RaisePropertyChanged(nameof(LayerCountText));
            RaisePropertyChanged(nameof(EmptyHint));

            RaiseAllCanExecute();
        }

        private IReadOnlyList<ScadaLayerItem> BuildItems(ScadaPage page)
        {
            var layers = page.Layers;

            // 选中图元的归属层：Guid.Empty（未分层）与"指向已删图层"都不该高亮任何一行
            Guid? selectedLayerId = _editor.SelectedElement?.LayerId;

            var items = new ScadaLayerItem[layers.Count];

            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];

                items[i] = new ScadaLayerItem(
                    layer,
                    page.CountElements(layer),
                    page.CanRemoveLayer(layer),
                    canMoveUp: i > 0,
                    canMoveDown: i < layers.Count - 1,
                    containsSelection: selectedLayerId is { } id && id != Guid.Empty && id == layer.LayerId);
            }

            return items;
        }

        /// <summary>
        /// 逐项比"看得见的东西"。比的是值不是引用：两次重建拿到的必然是两批新对象，
        /// 引用比较永远为假，等于没挡。
        /// </summary>
        private static bool ItemsEqual(IReadOnlyList<ScadaLayerItem> left, IReadOnlyList<ScadaLayerItem> right)
        {
            if (left.Count != right.Count) return false;

            for (int i = 0; i < left.Count; i++)
            {
                var a = left[i];
                var b = right[i];

                if (!ReferenceEquals(a.Layer, b.Layer)
                    || a.Name != b.Name
                    || a.IsVisible != b.IsVisible
                    || a.IsLocked != b.IsLocked
                    || a.ElementCount != b.ElementCount
                    || a.CanRemove != b.CanRemove
                    || a.CanMoveUp != b.CanMoveUp
                    || a.CanMoveDown != b.CanMoveDown
                    || a.ContainsSelection != b.ContainsSelection)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>未分层图元数：<see cref="ScadaPage.ResolveLayer"/> 为 null 的（含归属指向已删图层）</summary>
        private static int CountUnassigned(ScadaPage page)
        {
            int count = 0;

            foreach (var element in page.Elements)
            {
                if (page.ResolveLayer(element) == null)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// DelegateCommand 不跟随 CommandManager 自动重查，每个命令的可用性都必须在它依赖的状态变化处
        /// 显式 raise 一次。本面板的所有可用性都由"当前画面 + 层数 + 层内图元数"决定，
        /// 而它们全部汇进 <see cref="Rebuild"/>，所以刷新点就这一个。
        /// </summary>
        private void RaiseAllCanExecute()
        {
            AddLayerCommand.RaiseCanExecuteChanged();
            ToggleVisibleCommand.RaiseCanExecuteChanged();
            ToggleLockCommand.RaiseCanExecuteChanged();
            MoveLayerUpCommand.RaiseCanExecuteChanged();
            MoveLayerDownCommand.RaiseCanExecuteChanged();
            RenameLayerCommand.RaiseCanExecuteChanged();
            RemoveLayerCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 新建一层：单击直接落地，名字自动取"图层_N"。
        /// 刻意不弹输入框——一次点击就该是一条撤销位，弹框会把"新建"变成两步；
        /// 想改成有意义的名字，双击行或点笔形图标即可（模型侧会校验重名并给中文原因）。
        /// </summary>
        private void OnAddLayer() => _page?.AddLayer();

        private void OnToggleVisible(ScadaLayerItem? item)
        {
            if (item?.Layer is not { } layer) return;

            bool next = !layer.IsVisible;

            // 显隐没有 TrySet*：ScadaLayer.BeginEdit 就是它唯一的写入口（设计如此，见 ScadaLayer 类注释）
            using (layer.BeginEdit($"{(next ? "显示" : "隐藏")}图层 [{layer.Name}]"))
            {
                layer.IsVisible = next;
            }
        }

        private void OnToggleLock(ScadaLayerItem? item)
        {
            if (item?.Layer is not { } layer) return;

            bool next = !layer.IsLocked;

            using (layer.BeginEdit($"{(next ? "锁定" : "解锁")}图层 [{layer.Name}]"))
            {
                layer.IsLocked = next;
            }
        }

        /// <summary>这一层还能不能往 <paramref name="delta"/> 方向挪（越界即不能，端点项据此判灰）</summary>
        private bool CanMove(ScadaLayer? layer, int delta)
        {
            if (_page is not { } page || layer == null) return false;

            int index = page.Layers.IndexOf(layer);
            if (index < 0) return false;

            int target = index + delta;
            return target >= 0 && target < page.Layers.Count;
        }

        private void MoveLayer(ScadaLayer? layer, int delta)
        {
            if (_page is not { } page || layer == null) return;

            int index = page.Layers.IndexOf(layer);
            if (index < 0) return;

            // 越界由 TryMoveLayer 自己 clamp；这里不重复判，避免两处判据分叉
            page.TryMoveLayer(layer, index + delta, out _);
        }

        private void OnRenameLayer(ScadaLayerItem? item)
        {
            if (_page is not { } page || item?.Layer is not { } layer) return;

            // 预填当前名：多数改名是"改一两个字"，比空白框重新打一遍顺手得多
            var (confirmed, name) = EasyDialog.ShowTextInputSync($"重命名图层 [{layer.Name}]", layer.Name);
            if (!confirmed) return;

            // 失败原因（空名/重名）由模型给中文文案，这里只负责转达——
            // 面板自己判一遍就会出现"两处规则不一致"的经典毛病。
            if (!page.TryRenameLayer(layer, name, out string error))
                EasyDialog.ShowSync("无法重命名", error);
        }

        private void OnRemoveLayer(ScadaLayerItem? item)
        {
            if (_page is not { } page || item?.Layer is not { } layer) return;

            // 能走到这里的层一定是空的（按钮可用性就是 CanRemoveLayer），所以文案不必吓唬人；
            // 但仍然确认一次：垃圾桶图标很小，误点代价是"图层没了"。
            if (!EasyDialog.ShowSync("删除确认", $"确定要删除图层 [{layer.Name}] 吗？\n删除后可用 Ctrl+Z 撤销。"))
                return;

            if (!page.TryRemoveLayer(layer, out string error))
                EasyDialog.ShowSync("无法删除", error);
        }
    }
}
