using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 配方状态 chip 的语义档：模板只按这个键挑色，<b>不认文案</b>。
    ///
    /// 为什么不用 <see cref="System.Windows.Media.Brush"/> 直接算：
    /// 颜色是"设计期语言"（R117/R140），VM 里写死一支 Brush 就等于把配色从令牌里搬了出来；
    /// 而 DataTrigger 只能判等，所以用一个字符串键把"语义"交给模板翻译成令牌。
    /// </summary>
    public enum RecipeChipKind
    {
        Neutral,
        Accent,
        Success,
        Warning,
        Danger,
    }

    /// <summary>配方列表的一行（把"是否生效"这个 UI 态从模型里隔离出来）。</summary>
    public sealed class RecipeListItem : BindableBase
    {
        public RecipeListItem(RecipeDefinition recipe, bool isActive)
        {
            Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            IsActive = isActive;
        }

        public RecipeDefinition Recipe { get; }

        public Guid RecipeId => Recipe.RecipeId;

        public string Name => Recipe.DisplayName;

        public string Version => Recipe.DisplayVersion;

        public int EntryCount => Recipe.EntryCount;

        /// <summary>生效中（列表上的绿点 + 详情头的「生效中」chip 都读它）</summary>
        public bool IsActive { get; }

        /// <summary>第二行元信息：N 条参数 · 产品编码（无编码时只留前半句）</summary>
        public string MetaText
        {
            get
            {
                var code = string.IsNullOrWhiteSpace(Recipe.Code) ? "无编码" : Recipe.Code.Trim();
                return $"{Recipe.EntryCount} 条参数 · {code}";
            }
        }

        /// <summary>搜索匹配：名称 / 编码 / 备注，任一命中即算（大小写不敏感）</summary>
        public bool Matches(string? keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;

            var k = keyword.Trim();
            return Contains(Name, k) || Contains(Recipe.Code, k) || Contains(Recipe.Notes, k);
        }

        private static bool Contains(string? text, string keyword)
            => !string.IsNullOrEmpty(text)
               && text!.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 参数表的一行：<c>当前值 → 配方值</c>（设计基线 §9：阅读方向从"我要变成什么"出发）。
    ///
    /// <see cref="StatusKind"/> 是"语义键"，模板据此挑 chip 样式与差异标线；
    /// <see cref="StatusText"/> 才是给人看的字。
    /// </summary>
    public sealed class RecipeParameterRow : BindableBase
    {
        public RecipeTargetKind Kind { get; init; }
        public Guid TargetId { get; init; }
        public string Target { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;
        public string DeclaredType { get; init; } = string.Empty;

        /// <summary>
        /// 配方里存的值（规范 JSON 原文），就地可编辑。
        ///
        /// 写在这里而不是直接改 RecipeEntry：界面上的编辑要能"改错了不进库"。
        /// 真正的落库在 <see cref="RecipeManagerViewModel.CommitParameterValue"/>，
        /// 由服务层的类型判据决定成败，失败时这个属性会被顶回原值。
        /// </summary>
        private string _recipeValue = string.Empty;

        public string RecipeValue
        {
            get => _recipeValue;
            set => SetProperty(ref _recipeValue, value ?? string.Empty);
        }

        /// <summary>服务端确认过的值：编辑失败时按它顶回去，也是"有没有改动"的比较基准。</summary>
        public string CommittedRecipeValue { get; set; } = string.Empty;

        /// <summary>这条参数的值能不能改（目标已删除/无权限/运行中都不行，由 VM 每次重算时写入）</summary>
        private bool _canEditValue;

        public bool CanEditValue
        {
            get => _canEditValue;
            set => SetProperty(ref _canEditValue, value);
        }

        /// <summary>不能改时悬停要说话，别让用户对着灰框猜。</summary>
        private string _valueEditBlockedReason = string.Empty;

        public string ValueEditBlockedReason
        {
            get => _valueEditBlockedReason;
            set => SetProperty(ref _valueEditBlockedReason, value ?? string.Empty);
        }
        public string CurrentValue { get; init; } = string.Empty;
        public string StatusText { get; init; } = string.Empty;
        public RecipeChipKind StatusKind { get; init; } = RecipeChipKind.Neutral;
        public bool IsDifferent { get; init; }

        /// <summary>目标已不存在（行左侧画红色标线，整行目标列也改红）</summary>
        public bool IsMissing => StatusKind == RecipeChipKind.Danger;

        /// <summary>勾选状态（工具栏「删除选中」读它）</summary>
        private bool _isChecked;

        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        /// <summary>行的唯一键：删除参数时按 (Kind, TargetId, Key) 定位，不用行对象身份</summary>
        public (RecipeTargetKind Kind, Guid TargetId, string Key) Identity => (Kind, TargetId, Key);
    }

    /// <summary>
    /// v2「新建配方」弹层候选表的一行。
    ///
    /// 与 <see cref="RecipeCandidate"/> 的关系：后者是服务层的只读 DTO（GetCandidates 直接产出），
    /// 这里是它的<b>可勾选包装</b>。分开两个类型是因为勾选态属于"这一次操作"，
    /// 不该混进服务返回的候选数据里（服务层不关心 UI 勾了什么）。
    /// </summary>
    public sealed class RecipeCaptureCandidate : BindableBase
    {
        public RecipeTargetKind Kind { get; init; }
        public Guid TargetId { get; init; }
        public string Key { get; init; } = string.Empty;
        public string Target { get; init; } = string.Empty;
        public string DeclaredType { get; init; } = string.Empty;
        public string CurrentValue { get; init; } = string.Empty;

        /// <summary>
        /// 这一条现在不能进配方（已由上游连线提供、或已被规则判掉）。
        /// 不可勾选但<b>仍然显示</b>——直接不列出来，人会以为"方案里没这个参数"。
        /// </summary>
        public bool IsDisabled { get; init; }

        /// <summary>不可勾选的原因（显示在行尾，替代勾选框的交互）</summary>
        public string DisabledReason { get; init; } = string.Empty;

        private bool _isChecked;

        /// <summary>
        /// 勾选态。<b>默认 false</b>——2026-10-24 用户拍板「默认全不勾、必须自己点」，
        /// 不要在这里预勾差异项或任何"聪明"的默认。
        /// </summary>
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (IsDisabled) return;  // 不可勾选的行，点也不翻
                SetProperty(ref _isChecked, value);
            }
        }

        /// <summary>分组键：按目标（步骤名 / 变量所在面板）分行，界面上画成分组行</summary>
        public string GroupName => Target;

        /// <summary>
        /// 这一行是不是"某个目标的第一条"——用来决定在它上面画不画分组行。
        /// 绑定填、筛选后由 <c>RefreshCaptureGroupFlags</c> 重算：
        /// 搜索/筛选会打散分组（比如"只看已勾"剩下两条属于不同步骤），
        /// 按"完整列表里的相邻关系"画分组行就会出现只有一个空标题的分组。
        /// </summary>
        private bool _isGroupStart;
        public bool IsGroupStart
        {
            get => _isGroupStart;
            set => SetProperty(ref _isGroupStart, value);
        }

        /// <summary>
        /// 参数列文案。本地变量的 <see cref="Key"/> 是空串（变量本身就是"那一条"），
        /// 直接绑 Key 会得到一整列空白——补成变量名，与 <see cref="RecipeCandidate.DisplayTitle"/> 同口径。
        /// </summary>
        public string KeyText => string.IsNullOrEmpty(Key) ? Target : Key;

        /// <summary>类型列文案（去掉命名空间，只留短名）</summary>
        public string TypeText => ShortTypeName(DeclaredType);

        /// <summary>
        /// 目标列文案：<b>永远留空</b>——目标名已经由分组标题表达，
        /// 每条参数再重复一遍只是把一列有效横向空间浪费在同一个词上
        /// （截图核对时一眼能看出"匹配 / 匹配 / 匹配"三连）。
        /// 保留这个属性而不是在 XAML 里写死空串：将来若要做"固定分组头"会把目标名挪到这里。
        /// </summary>
        public string TargetText => string.Empty;

        /// <summary>勾选框是否可点（与 <see cref="IsDisabled"/> 反相，便于 XAML 直接绑 IsEnabled）</summary>
        public bool IsCheckedEnabled => !IsDisabled;

        public (RecipeTargetKind Kind, Guid TargetId, string Key) Identity => (Kind, TargetId, Key);

        /// <summary>'System.Double' → 'Double'：类型列只有 9% 宽，带命名空间必然截断</summary>
        private static string ShortTypeName(string declaredType)
        {
            if (string.IsNullOrEmpty(declaredType)) return string.Empty;
            int cut = declaredType.LastIndexOf('.');
            return cut >= 0 && cut < declaredType.Length - 1 ? declaredType[(cut + 1)..] : declaredType;
        }
    }

    /// <summary>应用确认表的一行（与参数表分开：确认表列序不同，且不需要勾选）。</summary>
    public sealed class RecipePreviewRow
    {        public string Target { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;
        public string Current { get; init; } = string.Empty;
        public string Recipe { get; init; } = string.Empty;
        public string StatusText { get; init; } = string.Empty;
        public RecipeChipKind StatusKind { get; init; } = RecipeChipKind.Neutral;
        public bool IsDifferent { get; init; }
    }

    /// <summary>
    /// 配方管理器视图模型（按 <c>docs/UI设计基线/recipe-manager</c> 复刻）。
    ///
    /// <b>与旧版的四处结构性变化</b>
    /// ---------
    /// ① <b>权限与运行锁在按下前判</b>（R136①）：<see cref="CanEdit"/>/<see cref="CanApply"/> 进命令谓词，
    ///    按钮先灰掉并在 ToolTip 里说清原因，而不是点完弹一条橙色错误。
    /// ② <b>八个业务态</b>：空 / 未选中 / 无参数 / 有差异 / 只读 / 运行中 / 预检失败 / 悬空。
    ///    模板按这些布尔位整块切换，未选中时<b>不渲染</b>编辑器（旧版会摆出 6 个不可用控件）。
    /// ③ <b>多选删除</b>：参数行带 <see cref="RecipeParameterRow.IsChecked"/>，工具栏一个「删除选中 (N)」。
    /// ④ <b>重命名就地编辑</b>：详情头点铅笔进入编辑，Enter 提交 / Esc 取消，不再是常驻裸输入框。
    ///
    /// 所有写操作仍只走 <see cref="RecipeService"/>；本类不直接改步骤参数。
    /// </summary>
    public sealed class RecipeManagerViewModel : BindableBase, IDialogAware
    {
        private readonly RecipeService _service;
        private readonly RecipeSaveCoordinator _saveCoordinator;
        private readonly RecipeFileService _fileService;

        private RecipeListItem? _selectedItem;
        private string _searchText = string.Empty;
        private string _parameterFilter = string.Empty;
        private bool _onlyDifferences;
        private string _newRecipeName = string.Empty;
        private string _newRecipeCode = string.Empty;
        private string _newRecipeNotes = string.Empty;
        private bool _isCapturing;
        private bool _isEditingName;
        private string _editNameText = string.Empty;
        private string _statusText = string.Empty;
        private bool _statusIsError;
        private bool _isBusy;
        private bool _isConfirmingApply;
        private bool _previewCanApply;
        private bool _confirmSaveAfter;
        private int _previewErrors;
        private int _previewWarnings;
        private Action? _requestLogin;

        // v2 新建弹层的状态
        private bool _isCreatingRecipe;
        private string _captureSearchText = string.Empty;
        private int _captureFilterIndex;
        private RecipeCandidate? _selectedCandidate;

        public RecipeManagerViewModel(
            RecipeService service,
            RecipeSaveCoordinator saveCoordinator,
            RecipeFileService fileService)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _saveCoordinator = saveCoordinator ?? throw new ArgumentNullException(nameof(saveCoordinator));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));

            AllRecipes = new ObservableCollection<RecipeListItem>();
            RecipeItems = new ObservableCollection<RecipeListItem>();
            Parameters = new ObservableCollection<RecipeParameterRow>();
            ParametersView = CollectionViewSource.GetDefaultView(Parameters);
            ParametersView.Filter = FilterParameter;
            Candidates = new ObservableCollection<RecipeCandidate>();
            PreviewItems = new ObservableCollection<RecipePreviewRow>();
            CaptureCandidates = new ObservableCollection<RecipeCaptureCandidate>();
            CaptureCandidatesView = CollectionViewSource.GetDefaultView(CaptureCandidates);
            CaptureCandidatesView.Filter = FilterCaptureCandidate;

            CaptureCommand = new DelegateCommand(Capture, CanCapture)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => NewRecipeName)
                .ObservesProperty(() => CanEdit);
            ApplyCommand = new DelegateCommand(() => BeginPreview(saveAfter: false), CanApply)
                .ObservesProperty(() => SelectedItem)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => IsConfirmingApply)
                .ObservesProperty(() => CanApplyNow)
                .ObservesProperty(() => CanOperateNow);
            ApplyAndSaveCommand = new DelegateCommand(() => BeginPreview(saveAfter: true), CanApply)
                .ObservesProperty(() => SelectedItem)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => IsConfirmingApply)
                .ObservesProperty(() => CanApplyNow)
                .ObservesProperty(() => CanOperateNow);
            ConfirmApplyCommand = new DelegateCommand(ConfirmApply, () => IsConfirmingApply && PreviewCanApply)
                .ObservesProperty(() => IsConfirmingApply)
                .ObservesProperty(() => PreviewCanApply)
                .ObservesProperty(() => IsBusy);
            CancelPreviewCommand = new DelegateCommand(CancelPreview, () => IsConfirmingApply)
                .ObservesProperty(() => IsConfirmingApply);
            AddCandidateCommand = new DelegateCommand(AddCandidate, () => !IsBusy && CanEdit && SelectedCandidate != null)
                .ObservesProperty(() => SelectedCandidate)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => CanEdit);
            RemoveParameterCommand = new DelegateCommand(RemoveCheckedParameters, () => !IsBusy && CanEdit && CheckedCount > 0)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => CanEdit)
                .ObservesProperty(() => CheckedCount);
            CheckAllCommand = new DelegateCommand(ToggleCheckAll, () => Parameters.Count > 0)
                .ObservesProperty(() => Parameters.Count);
            DeleteCommand = new DelegateCommand(Delete, () => !IsBusy && CanEdit && SelectedRecipe != null)
                .ObservesProperty(() => SelectedItem)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => CanEdit);
            BeginRenameCommand = new DelegateCommand(BeginRename, () => !IsBusy && CanEdit && SelectedRecipe != null)
                .ObservesProperty(() => SelectedItem)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => CanEdit);
            CommitRenameCommand = new DelegateCommand(CommitRename, () => IsEditingName && !string.IsNullOrWhiteSpace(EditNameText))
                .ObservesProperty(() => IsEditingName)
                .ObservesProperty(() => EditNameText);
            CancelRenameCommand = new DelegateCommand(() => IsEditingName = false, () => IsEditingName)
                .ObservesProperty(() => IsEditingName);
            ClearActiveCommand = new DelegateCommand(ClearActive, () => !IsBusy && CanEdit)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => CanEdit);
            ExportCommand = new DelegateCommand(Export, () => !IsBusy && CanEdit && SelectedRecipe != null)
                .ObservesProperty(() => SelectedItem)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => CanEdit);
            ImportCommand = new DelegateCommand(Import, () => !IsBusy && CanEdit)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => CanEdit);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.OK));
            // 新建入口**不按权限禁用**：未登录 / 非工程师时按钮照样能按，按下去先弹登录，
            // 登录成功后自己接着把新建流程走完（详见 OpenCapture）。
            // 旧实现是 `() => !IsBusy && CanEdit`，未登录时按钮直接置灰 ——
            // 用户看到的是"点了没有任何反应"，而且置灰的按钮连理由都不显示。
            OpenCaptureCommand = new DelegateCommand(OpenCapture, () => !IsBusy)
                .ObservesProperty(() => IsBusy);
            CancelCaptureCommand = new DelegateCommand(() => IsCapturing = false);
            // 走 ShowLoginByHost：宿主弹窗关掉后这里要再 Refresh 一次，权限条才会跟着消失。
            LoginCommand = new DelegateCommand(ShowLoginByHost);

            // v2 新建弹层的三条命令
            CheckAllCaptureCommand = new DelegateCommand(CheckAllCaptureCandidates)
                .ObservesProperty(() => IsBusy);
            ClearCaptureChecksCommand = new DelegateCommand(ClearCaptureSelection)
                .ObservesProperty(() => IsBusy);
            CreateRecipeCommand = new DelegateCommand(CreateRecipe, CanCreateRecipe)
                .ObservesProperty(() => IsBusy);
            // 只关弹层，不清已填的名称/编码/备注——用户误点关闭再点开不该丢掉刚敲的字，
            // 真正要丢弃时走 Capture 成功后的 ResetCaptureForm（与 v1 内联卡片同一口径）。
            CancelCreateCommand = new DelegateCommand(() => IsCreatingRecipe = false);

            // 权限与运行锁都可能被"别处"改掉（登录弹窗、流程启动），
            // 这里挂一次订阅，把它们变成按钮可用性的实时变化（README F3）。
            _service.StateChanged += OnServiceStateChanged;

            Refresh();
        }

        /// <summary>
        /// 「登录」按钮的出口：由宿主决定弹哪个登录窗（本 VM 不碰 IDialogService）。
        /// 为 null 时按钮不出现（<see cref="ShowLoginEntry"/>）——而不是点了没反应。
        /// 宿主经 <see cref="RequestLoginParameter"/> 把这个出口注册进来。
        /// </summary>
        public Action? RequestLogin
        {
            get => _requestLogin;
            set
            {
                if (_requestLogin == value) return;
                _requestLogin = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ShowLoginEntry));
            }
        }

        public string Title => "配方管理";

        public DialogCloseListener RequestClose { get; set; }

        /// <summary>全部配方（搜索过滤前）</summary>
        public ObservableCollection<RecipeListItem> AllRecipes { get; }

        /// <summary>左栏可见列表（搜索过滤后）</summary>
        public ObservableCollection<RecipeListItem> RecipeItems { get; }

        public ObservableCollection<RecipeParameterRow> Parameters { get; }

        /// <summary>参数表的视图（承载「仅显示差异」与参数搜索两个过滤）</summary>
        public ICollectionView ParametersView { get; }

        public ObservableCollection<RecipeCandidate> Candidates { get; }

        public ObservableCollection<RecipePreviewRow> PreviewItems { get; }

        /// <summary>
        /// v2「新建配方」弹层里的候选行（勾选表的数据源）。
        /// <para>
        /// 与右栏的 <see cref="Candidates"/> 是两套：那一套服务于"给<b>已有</b>配方追加单条参数"，
        /// 只在选中配方时才填（<c>GetCandidates(recipeId)</c>，会排除已加入的）；这一套服务于"新建"，
        /// 取 <c>GetCandidates(null)</c>，不做已加入排除。
        /// </para>
        /// </summary>
        public ObservableCollection<RecipeCaptureCandidate> CaptureCandidates { get; }

        /// <summary>候选表的视图（承载搜索 + 三档筛选）</summary>
        public ICollectionView CaptureCandidatesView { get; }

        public DelegateCommand CheckAllCaptureCommand { get; }
        public DelegateCommand ClearCaptureChecksCommand { get; }
        public DelegateCommand CreateRecipeCommand { get; }
        public DelegateCommand CancelCreateCommand { get; }

        public DelegateCommand CaptureCommand { get; }
        public DelegateCommand ApplyCommand { get; }
        public DelegateCommand ApplyAndSaveCommand { get; }
        public DelegateCommand ConfirmApplyCommand { get; }
        public DelegateCommand CancelPreviewCommand { get; }
        public DelegateCommand AddCandidateCommand { get; }
        public DelegateCommand RemoveParameterCommand { get; }
        public DelegateCommand CheckAllCommand { get; }
        public DelegateCommand DeleteCommand { get; }
        public DelegateCommand BeginRenameCommand { get; }
        public DelegateCommand CommitRenameCommand { get; }
        public DelegateCommand CancelRenameCommand { get; }
        public DelegateCommand ClearActiveCommand { get; }
        public DelegateCommand ExportCommand { get; }
        public DelegateCommand ImportCommand { get; }
        public DelegateCommand CloseCommand { get; }
        public DelegateCommand OpenCaptureCommand { get; }
        public DelegateCommand CancelCaptureCommand { get; }
        public DelegateCommand LoginCommand { get; }

        // ────────────────────────────────────────────────────────────────
        //  权限与运行锁（全部现算，不缓存）
        // ────────────────────────────────────────────────────────────────

        /// <summary>能不能编辑（工程师及以上）。每次读都问一次服务。</summary>
        public bool CanEdit => _service.CanEdit(out _);

        /// <summary>能不能切换配方（操作员及以上）</summary>
        public bool CanOperateNow => _service.IsLoggedIn;

        /// <summary>流程是否在运行（含暂停）</summary>
        public bool IsRunLocked => _service.IsAnyFlowRunning();

        /// <summary>应用是否此刻可用（权限 + 运行锁 + 有选中 + 不忙 + 不在确认层）</summary>
        public bool CanApplyNow => CanOperateNow && !IsRunLocked;

        public bool IsLoggedIn => _service.IsLoggedIn;

        public string ActorName => _service.ActorName;

        /// <summary>
        /// 权限条是否出现（未登录才出现）。
        /// <para>
        /// 2026-10-24 起<b>不再控制整条的可见性</b>：那条横带改成恒显 38px，未登录是黄底警告、
        /// 已登录是中性的「切换用户」。留这个属性只为兼容可能存在的旧绑定，别再拿它包 Visibility。
        /// </para>
        /// </summary>
        public bool ShowPermissionBanner => !IsLoggedIn;

        /// <summary>
        /// 权限条里的按钮是否可用（`RequestLogin` 会包住它）。
        /// <para>
        /// 旧语义是「未登录 **且** 宿主给了出口」，已登录时整条连按钮一起消失 ——
        /// 那样已登录的人就没有任何入口去换用户/登出了。现在按钮恒在，只在宿主<b>没给出口</b>时禁用
        /// （禁用会带 <see cref="LoginButtonToolTip"/> 说明原因，仍然不留"点了没反应"的死键）。
        /// </para>
        /// </summary>
        public bool ShowLoginEntry => true;

        /// <summary>权限条按钮的文案：未登录是「登录」，已登录是「admin（工程师）▾」</summary>
        public string LoginButtonText => IsLoggedIn ? $"{ActorName}（{RoleLabel}）▾" : "登录";

        /// <summary>权限条按钮的提示语</summary>
        public string LoginButtonToolTip => RequestLogin == null
            ? "没有可用的登录出口（宿主未注入），无法在这里登录或切换用户"
            : IsLoggedIn ? "点击可切换用户或登出" : "登录后可以切换、新建、编辑配方";

        /// <summary>角色中文名（工程师 / 操作员 / 未登录）</summary>
        private string RoleLabel => _service.ActorRoleName;

        /// <summary>标题栏的角色 chip 文案</summary>
        public string RoleText => IsLoggedIn ? ActorName : "未登录";

        /// <summary>标题栏角色 chip 的语义档</summary>
        public RecipeChipKind RoleKind => IsLoggedIn ? RecipeChipKind.Neutral : RecipeChipKind.Warning;

        /// <summary>标题栏运行态 chip 文案</summary>
        public string RunStateText => IsRunLocked ? "运行中 · 应用锁定" : "停止 · 可应用";

        /// <summary>标题栏运行态 chip 的语义档</summary>
        public RecipeChipKind RunStateKind => IsRunLocked ? RecipeChipKind.Danger : RecipeChipKind.Success;

        /// <summary>底栏提示：运行中换成琥珀警告，否则说明"应用只写内存"</summary>
        public string FooterHint => IsRunLocked
            ? "流程运行中（含暂停）：应用被禁用，请先停止流程。"
            : "应用只写入内存；「应用并保存」会同时保存方案文件。";

        public bool FooterHintIsWarning => IsRunLocked;

        /// <summary>编辑类按钮的禁用理由（ToolTip 用；可用时为空串）</summary>
        public string EditBlockedReason
        {
            get
            {
                if (IsBusy) return "正在处理上一项操作…";
                if (!_service.CanEdit(out var error)) return error;
                return string.Empty;
            }
        }

        /// <summary>应用类按钮的禁用理由（ToolTip 用；可用时为空串）</summary>
        public string ApplyBlockedReason
        {
            get
            {
                if (IsBusy) return "正在处理上一项操作…";
                if (!IsLoggedIn) return "请先登录（操作员及以上）再切换配方";
                if (IsRunLocked) return "流程运行中（含暂停）：请先停止流程";
                if (SelectedRecipe == null) return "请先在左侧选择一份配方";
                return string.Empty;
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  选择与过滤
        // ────────────────────────────────────────────────────────────────

        public RecipeListItem? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (!SetProperty(ref _selectedItem, value)) return;

                CancelPreview();
                IsEditingName = false;

                RaisePropertyChanged(nameof(SelectedRecipe));
                RaisePropertyChanged(nameof(SelectedTitle));
                RaisePropertyChanged(nameof(SelectedCodeText));
                RaisePropertyChanged(nameof(SelectedMetaText));
                RaisePropertyChanged(nameof(HasSelection));
                RaisePropertyChanged(nameof(ShowEditor));
                RaisePropertyChanged(nameof(ShowEmptyState));
                RaisePropertyChanged(nameof(ShowNoSelectionState));
                RaisePropertyChanged(nameof(SelectedIsActive));
                RaisePropertyChanged(nameof(SelectedVersion));
                RaisePropertyChanged(nameof(ApplyBlockedReason));

                RefreshParameters();
                RaisePropertyChanged(nameof(CheckedCount));
                RaisePropertyChanged(nameof(CheckAllState));
            }
        }

        /// <summary>左栏搜索词（名称 / 编码 / 备注）</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!SetProperty(ref _searchText, value ?? string.Empty)) return;
                ApplyRecipeFilter();
            }
        }

        /// <summary>参数表搜索词（目标 / 参数名 / 值）</summary>
        public string ParameterFilter
        {
            get => _parameterFilter;
            set
            {
                if (!SetProperty(ref _parameterFilter, value ?? string.Empty)) return;
                ParametersView.Refresh();
            }
        }

        /// <summary>仅显示差异（默认关：全表才看得出"配方里有哪几条"）</summary>
        public bool OnlyDifferences
        {
            get => _onlyDifferences;
            set
            {
                if (!SetProperty(ref _onlyDifferences, value)) return;
                ParametersView.Refresh();
            }
        }

        public RecipeDefinition? SelectedRecipe => SelectedItem?.Recipe;

        public bool HasSelection => SelectedRecipe != null;

        /// <summary>空方案：一份配方都没有（右栏显示"从当前值新建 / 导入"空态块）</summary>
        public bool ShowEmptyState => AllRecipes.Count == 0;

        /// <summary>有配方但没选中（右栏显示"选择一份配方"）</summary>
        public bool ShowNoSelectionState => AllRecipes.Count > 0 && SelectedRecipe == null;

        /// <summary>编辑器整块可见（空态与未选中都不渲染它）</summary>
        public bool ShowEditor => SelectedRecipe != null;

        public string SelectedTitle => SelectedRecipe?.DisplayName ?? "未选择配方";

        public bool SelectedIsActive => SelectedItem?.IsActive == true;

        public string SelectedVersion => SelectedRecipe?.DisplayVersion ?? string.Empty;

        public string SelectedCodeText
        {
            get
            {
                var recipe = SelectedRecipe;
                if (recipe == null) return string.Empty;
                return string.IsNullOrWhiteSpace(recipe.Code) ? "无产品编码" : "产品编码 " + recipe.Code.Trim();
            }
        }

        /// <summary>详情头第二行：N 条参数 · 最后应用 …</summary>
        public string SelectedMetaText
        {
            get
            {
                var recipe = SelectedRecipe;
                if (recipe == null) return string.Empty;

                var applied = recipe.LastAppliedAtUtc.HasValue
                    ? $"最后应用 {recipe.LastAppliedAtUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}"
                    : "尚未应用";

                return $"{recipe.EntryCount} 条参数 · {applied}";
            }
        }

        /// <summary>已勾选的参数条数（工具栏「删除选中 (N)」读它）</summary>
        public int CheckedCount => Parameters.Count(p => p.IsChecked);

        public bool HasCandidates => Candidates.Count > 0;

        /// <summary>候选条数提示（"12 个可添加"；0 时收起）</summary>
        public string CandidateCountText => Candidates.Count > 0 ? $"{Candidates.Count} 个可添加" : "没有可添加的参数";

        /// <summary>参数表是否为空（空态行据此显隐）</summary>
        public bool ParametersIsEmpty => Parameters.Count == 0;

        /// <summary>左栏计数文案（"共 N 份"；搜索过滤后按可见数算）</summary>
        public string RecipeCountText => $"共 {RecipeItems.Count} 份";

        /// <summary>汇总条里的「目标缺失 N」（0 条时整句收起）</summary>
        public string SummaryMissingText => SummaryMissing > 0 ? $"目标缺失 {SummaryMissing}" : string.Empty;

        /// <summary>确认层计数行的错误部分</summary>
        public string PreviewErrorsText => PreviewErrors > 0 ? $"错误 {PreviewErrors}" : string.Empty;

        /// <summary>确认层有没有具名错误（控制错误块显隐）</summary>
        public bool PreviewHasError => !string.IsNullOrWhiteSpace(PreviewErrorText);

        /// <summary>确认层左下说明：能不能确认，以及为什么</summary>
        public string PreviewFooterNote => PreviewCanApply
            ? (_confirmSaveAfter ? "确认后应用并保存方案。" : "确认后应用（只写内存，不保存）。")
            : "预检未通过，确认应用不可用。";

        /// <summary>方案名（标题栏用；未加载方案时显示占位）</summary>
        public string SolutionName
        {
            get
            {
                var name = _service.Solution?.SolutionName;
                return string.IsNullOrWhiteSpace(name) ? "未加载方案" : name!;
            }
        }

        /// <summary>过滤后是否一条都没有（与"配方本身没参数"是两回事）</summary>
        public bool ParametersFilteredEmpty => Parameters.Count > 0 && !ParametersView.Cast<object>().Any();

        /// <summary>汇总条：共 N 条 · 修改 M · 不变 K · 目标缺失 X</summary>
        public string SummaryCountText => $"共 {Parameters.Count} 条";

        public int SummaryModified => Parameters.Count(p => p.StatusKind == RecipeChipKind.Warning);

        public int SummaryMissing => Parameters.Count(p => p.StatusKind == RecipeChipKind.Danger);

        public int SummaryUnchanged => Parameters.Count(p => p.StatusKind == RecipeChipKind.Neutral && !p.IsDifferent);

        // ────────────────────────────────────────────────────────────────
        //  新建 / 重命名
        // ────────────────────────────────────────────────────────────────

        /// <summary>新建表单是否展开（左栏底部「新建配方」展开成一张卡片）</summary>
        public bool IsCapturing
        {
            get => _isCapturing;
            set => SetProperty(ref _isCapturing, value);
        }

        // ────────────────────────────────────────────────────────────────
        //  v2：新建配方弹层
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 新建弹层是否打开（v2 用整层遮罩，不再用左栏那张内联小卡片）。
        /// <para>
        /// 关掉时顺手清空勾选，避免下一次打开时残留上一轮的勾选——
        /// 用户明确要求"默认全不勾、必须自己点"。
        /// </para>
        /// </summary>
        public bool IsCreatingRecipe
        {
            get => _isCreatingRecipe;
            set
            {
                if (!SetProperty(ref _isCreatingRecipe, value)) return;
                if (!value) ClearCaptureSelection();
            }
        }

        /// <summary>候选表搜索词（按目标名 / 参数名匹配）</summary>
        public string CaptureSearchText
        {
            get => _captureSearchText;
            set
            {
                if (!SetProperty(ref _captureSearchText, value ?? string.Empty)) return;
                CaptureCandidatesView.Refresh();
                RefreshCaptureGroupFlags();
                RaisePropertyChanged(nameof(CaptureVisibleCountText));
            }
        }

        /// <summary>候选表筛选档：0=全部，1=只看已勾，2=只看可勾</summary>
        public int CaptureFilterIndex
        {
            get => _captureFilterIndex;
            set
            {
                if (!SetProperty(ref _captureFilterIndex, value)) return;
                CaptureCandidatesView.Refresh();
                RefreshCaptureGroupFlags();
                RaisePropertyChanged(nameof(CaptureVisibleCountText));
            }
        }

        /// <summary>候选总数文案（标题右侧）</summary>
        public string CaptureCountText => CaptureCandidates.Count == 0
            ? "没有可捕获的参数"
            : $"共 {CaptureCandidates.Count} 个可勾选 · 已选 {CaptureCheckedCount} 个";

        /// <summary>当前筛选后可见条数（搜索无结果时给空态用）</summary>
        public string CaptureVisibleCountText => CaptureCandidatesView.IsEmpty
            ? "没有匹配的参数"
            : $"显示 {CaptureCandidates.Count(c => FilterCaptureCandidate(c))} 个";

        /// <summary>已勾选条数</summary>
        public int CaptureCheckedCount => CaptureCandidates.Count(c => c.IsChecked && !c.IsDisabled);

        /// <summary>底栏主按钮文案：零勾选时按钮也按不下去，文案同步说清楚</summary>
        public string CreateRecipeButtonText => $"创建配方（{CaptureCheckedCount}）";

        /// <summary>零勾选时给一句提示，别让人对着灰按钮猜</summary>
        public string CaptureSelectionHint => CaptureCheckedCount == 0
            ? "还没勾选参数。配方至少要存一条参数，请在候选表里勾上。"
            : $"已选 {CaptureCheckedCount} 个参数，创建后这份配方只包含它们。";

        /// <summary>弹层是否显示"没勾选"的空态块</summary>
        public bool ShowCaptureNonePicked => CaptureCandidates.Count > 0 && CaptureCheckedCount == 0;

        /// <summary>弹层是否显示"没有可捕获参数"的空态块</summary>
        public bool ShowCaptureNoCandidates => CaptureCandidates.Count == 0;

        /// <summary>只留给旧 XAML 的兼容位（v2 弹层不用它）</summary>
        public bool ShowCaptureFreshForm => IsCreatingRecipe;

        private bool FilterCaptureCandidate(object item)
            => item is RecipeCaptureCandidate candidate && FilterCaptureCandidate(candidate);

        private bool FilterCaptureCandidate(RecipeCaptureCandidate candidate)
        {
            if (CaptureFilterIndex == 1 && !candidate.IsChecked) return false;
            if (CaptureFilterIndex == 2 && candidate.IsDisabled) return false;

            var keyword = (_captureSearchText ?? string.Empty).Trim();
            if (keyword.Length == 0) return true;

            return candidate.Target.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || candidate.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || candidate.DeclaredType.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private void OnCaptureCandidateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(RecipeCaptureCandidate.IsChecked)) return;
            RaiseCaptureSelectionState();
            // 「只看已勾」开着时，取消勾选要立刻从列表里消失。
            if (CaptureFilterIndex == 1)
            {
                CaptureCandidatesView.Refresh();
                RefreshCaptureGroupFlags();
            }
        }

        private void RaiseCaptureSelectionState()
        {
            RaisePropertyChanged(nameof(CaptureCheckedCount));
            RaisePropertyChanged(nameof(CaptureCountText));
            RaisePropertyChanged(nameof(CreateRecipeButtonText));
            RaisePropertyChanged(nameof(CaptureSelectionHint));
            RaisePropertyChanged(nameof(CreateRecipeBlockedReason));
            RaisePropertyChanged(nameof(ShowCaptureNonePicked));
            RaisePropertyChanged(nameof(ShowCaptureNoCandidates));
            CreateRecipeCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 重算"谁是当前视图里某个目标的第一行"，用来画分组标题。
        /// <para>
        /// 必须按<b>筛选之后</b>的可见序列算：搜索"相机"时若仍按完整列表的相邻关系标，
        /// 会出现只有一条的组、或者组标题落在被隐藏的行上（标题消失，参数孤零零挂在别人组下）。
        /// </para>
        /// </summary>
        private void RefreshCaptureGroupFlags()
        {
            string? previous = null;
            foreach (var row in CaptureCandidates)
            {
                bool visible = FilterCaptureCandidate(row);
                bool starts = visible && !string.Equals(previous, row.GroupName, StringComparison.Ordinal);
                row.IsGroupStart = starts;
                if (visible) previous = row.GroupName;
            }
        }

        private void ClearCaptureSelection()
        {
            foreach (var candidate in CaptureCandidates) candidate.IsChecked = false;
            RaiseCaptureSelectionState();
        }

        private void CheckAllCaptureCandidates()
        {
            // 只勾"当前可见且可勾"的那些：搜索在「相机」上时按全选，不该把别的步骤也勾上。
            foreach (var candidate in CaptureCandidates)
            {
                if (candidate.IsDisabled) continue;
                if (!FilterCaptureCandidate(candidate)) continue;
                candidate.IsChecked = true;
            }

            RaiseCaptureSelectionState();
        }

        /// <summary>
        /// 打开新建弹层并载入候选。
        /// <para>
        /// 候选取 <c>GetCandidates(null)</c>——"还没有这份配方"的视角，不做"已加入排除"。
        /// 注意不能借用右栏的 <see cref="Candidates"/>：它只在选中配方时才有内容。
        /// </para>
        /// </summary>
        private void BindCaptureCandidates()
        {
            foreach (var candidate in CaptureCandidates)
                candidate.PropertyChanged -= OnCaptureCandidateChanged;

            CaptureCandidates.Clear();
            _captureSearchText = string.Empty;
            _captureFilterIndex = 0;
            RaisePropertyChanged(nameof(CaptureSearchText));
            RaisePropertyChanged(nameof(CaptureFilterIndex));

            foreach (var candidate in _service.GetCandidates(null))
            {
                var row = new RecipeCaptureCandidate
                {
                    Kind = candidate.Kind,
                    TargetId = candidate.TargetId,
                    Key = candidate.Key,
                    Target = candidate.TargetName,
                    DeclaredType = candidate.DeclaredType,
                    CurrentValue = candidate.CurrentValue,
                    IsChecked = false,   // 默认全不勾（2026-10-24 用户拍板）
                };
                row.PropertyChanged += OnCaptureCandidateChanged;
                CaptureCandidates.Add(row);
            }

            CaptureCandidatesView.Refresh();
            RefreshCaptureGroupFlags();
            RaiseCaptureSelectionState();
        }

        public string NewRecipeName
        {
            get => _newRecipeName;
            set => SetProperty(ref _newRecipeName, value ?? string.Empty);
        }

        public string NewRecipeCode
        {
            get => _newRecipeCode;
            set => SetProperty(ref _newRecipeCode, value ?? string.Empty);
        }

        public string NewRecipeNotes
        {
            get => _newRecipeNotes;
            set => SetProperty(ref _newRecipeNotes, value ?? string.Empty);
        }

        /// <summary>详情头名字是否处于就地编辑态</summary>
        public bool IsEditingName
        {
            get => _isEditingName;
            set
            {
                if (!SetProperty(ref _isEditingName, value)) return;
                RaisePropertyChanged(nameof(IsNameReadOnly));
            }
        }

        public bool IsNameReadOnly => !IsEditingName;

        public string EditNameText
        {
            get => _editNameText;
            set => SetProperty(ref _editNameText, value ?? string.Empty);
        }

        /// <summary>
        /// 右栏「添加参数」下拉的当前项。
        /// <para>
        /// 必须是会发通知的属性：`AddCandidateCommand` 的 CanExecute 观测它，
        /// 普通自动属性下"选中了但按钮还是灰的"会一直卡住（ComboBox 选中不刷新命令）。
        /// </para>
        /// </summary>
        public RecipeCandidate? SelectedCandidate
        {
            get => _selectedCandidate;
            set => SetProperty(ref _selectedCandidate, value);
        }

        public bool IsConfirmingApply
        {
            get => _isConfirmingApply;
            private set => SetProperty(ref _isConfirmingApply, value);
        }

        public bool PreviewCanApply
        {
            get => _previewCanApply;
            private set => SetProperty(ref _previewCanApply, value);
        }

        public int PreviewErrors
        {
            get => _previewErrors;
            private set => SetProperty(ref _previewErrors, value);
        }

        public int PreviewWarnings
        {
            get => _previewWarnings;
            private set => SetProperty(ref _previewWarnings, value);
        }

        /// <summary>确认层顶部计数行</summary>
        public string PreviewCountText { get; private set; } = string.Empty;

        /// <summary>确认层标题：应用配方 · 配方名</summary>
        public string PreviewTitle => SelectedRecipe == null
            ? "应用配方"
            : "应用配方 · " + SelectedRecipe.DisplayName;

        /// <summary>确认层的具名错误块（空串时整块收起）</summary>
        public string PreviewErrorText { get; private set; } = string.Empty;

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (!SetProperty(ref _statusText, value)) return;
                RaisePropertyChanged(nameof(HasStatus));
            }
        }

        public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

        public bool StatusIsError
        {
            get => _statusIsError;
            private set => SetProperty(ref _statusIsError, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (!SetProperty(ref _isBusy, value)) return;
                RaiseAllStateFlags();
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  刷新
        // ────────────────────────────────────────────────────────────────

        public void Refresh()
        {
            var selectedId = SelectedItem?.RecipeId;
            var activeId = _service.Solution?.ActiveRecipeId;

            AllRecipes.Clear();
            foreach (var recipe in _service.Recipes)
            {
                if (recipe == null) continue;
                AllRecipes.Add(new RecipeListItem(recipe, activeId == recipe.RecipeId));
            }

            ApplyRecipeFilter();

            SelectedItem = RecipeItems.FirstOrDefault(i => i.RecipeId == selectedId)
                           ?? AllRecipes.FirstOrDefault(i => i.RecipeId == selectedId);

            RaiseAllStateFlags();
            RefreshParameters();
        }

        private void ApplyRecipeFilter()
        {
            RecipeItems.Clear();
            foreach (var item in AllRecipes)
            {
                if (item.Matches(SearchText))
                    RecipeItems.Add(item);
            }

            RaisePropertyChanged(nameof(ShowEmptyState));
            RaisePropertyChanged(nameof(ShowNoSelectionState));
            RaisePropertyChanged(nameof(RecipeCountText));
        }

        private void RefreshParameters()
        {
            foreach (var row in Parameters)
                row.PropertyChanged -= OnParameterRowChanged;

            Parameters.Clear();
            var recipe = SelectedRecipe;

            if (recipe == null)
            {
                Candidates.Clear();
                RaiseParameterCounts();
                return;
            }

            var report = _service.Compare(recipe.RecipeId);
            var byKey = report.Items.ToDictionary(
                i => i.TargetId + "\n" + i.Key,
                i => i,
                StringComparer.Ordinal);

            // 值能不能改：权限 + 运行锁 + 目标还在。三条都要，缺一条就只读并把原因挂在悬停上。
            var (valueEditable, valueBlockedReason) = ValueEditState();

            foreach (var entry in recipe.Entries ?? new List<RecipeEntry>())
            {
                if (entry == null) continue;

                byKey.TryGetValue(entry.TargetId + "\n" + entry.Key, out var item);
                var status = item?.Status ?? RecipeDeviationStatus.TargetMissing;

                var row = new RecipeParameterRow
                {
                    Kind = entry.Kind,
                    TargetId = entry.TargetId,
                    // 局部变量没有"目标步骤"：那两列对它是空的，合并成一格写"变量"
                    Target = entry.Kind == RecipeTargetKind.LocalVariable
                        ? "变量"
                        : (string.IsNullOrWhiteSpace(entry.TargetName) ? "（未命名目标）" : entry.TargetName),
                    Key = entry.Kind == RecipeTargetKind.LocalVariable
                        ? (string.IsNullOrWhiteSpace(entry.TargetName) ? "（未命名变量）" : entry.TargetName)
                        : entry.Key,
                    DeclaredType = ShortTypeName(entry.DeclaredType),
                    RecipeValue = entry.DisplayValue,
                    CommittedRecipeValue = entry.DisplayValue,
                    CanEditValue = valueEditable,
                    ValueEditBlockedReason = valueBlockedReason,
                    CurrentValue = string.IsNullOrEmpty(item?.Current) ? "—" : item!.Current,
                    StatusText = DisplayStatus(status),
                    StatusKind = KindOf(status),
                    IsDifferent = status != RecipeDeviationStatus.Equal,
                };

                row.PropertyChanged += OnParameterRowChanged;
                Parameters.Add(row);
            }

            RefreshCandidates();
            ParametersView.Refresh();
            RaiseParameterCounts();
        }

        private void OnParameterRowChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RecipeParameterRow.IsChecked))
            {
                RaisePropertyChanged(nameof(CheckedCount));
                RaisePropertyChanged(nameof(CheckAllState));
            }
        }

        /// <summary>
        /// 「配方值」这一列能不能改、不能改时的原因。三条件与 <see cref="CanEdit"/> / <see cref="CanApplyNow"/> 同源，
        /// 不另立一套口径：能改配方内容 = 工程师权限 + 不在运行中。
        /// </summary>
        private (bool Editable, string Reason) ValueEditState()
        {
            if (!_service.IsLoggedIn)
                return (false, "未登录，只能查看");

            if (!_service.CanEdit(out var editError))
                return (false, editError);

            if (IsRunLocked)
                return (false, "流程运行中，配方锁定");

            return (true, string.Empty);
        }

        /// <summary>
        /// 提交一次「配方值」编辑。失败时把界面上的字<b>顶回服务端那份</b>并报原因——
        /// 不能让错误的值留在框里，否则用户以为存进去了。
        /// </summary>
        public void CommitParameterValue(RecipeParameterRow? row)
        {
            if (row == null) return;

            if (!row.CanEditValue)
            {
                SetStatus(string.IsNullOrEmpty(row.ValueEditBlockedReason) ? "没有修改配方值的权限" : row.ValueEditBlockedReason, isError: true);
                row.RecipeValue = row.CommittedRecipeValue;
                return;
            }

            if (string.Equals(row.RecipeValue, row.CommittedRecipeValue, StringComparison.Ordinal))
                return;   // 没改，不动版本

            var recipe = SelectedRecipe;
            if (recipe == null) return;

            if (!_service.UpdateEntryValue(recipe.RecipeId, row.Kind, row.TargetId, row.Key, row.RecipeValue, out var error))
            {
                SetStatus(error, isError: true);
                row.RecipeValue = row.CommittedRecipeValue;
                return;
            }

            SetStatus($"已修改「{row.Key}」的配方值", isError: false);
            Refresh();
        }

        private void RaiseParameterCounts()
        {
            RaisePropertyChanged(nameof(ParametersIsEmpty));
            RaisePropertyChanged(nameof(ParametersFilteredEmpty));
            RaisePropertyChanged(nameof(SummaryCountText));
            RaisePropertyChanged(nameof(SummaryModified));
            RaisePropertyChanged(nameof(SummaryMissing));
            RaisePropertyChanged(nameof(SummaryMissingText));
            RaisePropertyChanged(nameof(SummaryUnchanged));
            RaisePropertyChanged(nameof(CheckedCount));
            RaisePropertyChanged(nameof(CheckAllState));
            CheckAllCommand.RaiseCanExecuteChanged();
            RemoveParameterCommand.RaiseCanExecuteChanged();
        }

        private void RefreshCandidates()
        {
            Candidates.Clear();
            SelectedCandidate = null;
            RaisePropertyChanged(nameof(SelectedCandidate));

            var recipe = SelectedRecipe;
            if (recipe == null)
            {
                RaisePropertyChanged(nameof(HasCandidates));
                RaisePropertyChanged(nameof(CandidateCountText));
                return;
            }

            foreach (var candidate in _service.GetCandidates(recipe.RecipeId))
                Candidates.Add(candidate);

            RaisePropertyChanged(nameof(HasCandidates));
            RaisePropertyChanged(nameof(CandidateCountText));
        }

        private bool FilterParameter(object item)
        {
            if (item is not RecipeParameterRow row) return false;

            if (OnlyDifferences && !row.IsDifferent) return false;

            if (string.IsNullOrWhiteSpace(ParameterFilter)) return true;

            var k = ParameterFilter.Trim();
            return Contains(row.Target, k) || Contains(row.Key, k) || Contains(row.DeclaredType, k)
                   || Contains(row.RecipeValue, k) || Contains(row.CurrentValue, k);
        }

        private static bool Contains(string? text, string keyword)
            => !string.IsNullOrEmpty(text)
               && text!.Contains(keyword, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 类型显示名：<c>System.Double</c> → <c>double</c>。
        /// 表格里那一列是"扫一眼知道这是数还是枚举"，全限定名会把列宽吃光。
        /// </summary>
        private static string ShortTypeName(string? declaredType)
        {
            if (string.IsNullOrWhiteSpace(declaredType)) return string.Empty;

            var text = declaredType!.Trim();

            if (text.StartsWith("System.Collections", StringComparison.Ordinal))
                return "list";

            if (text.StartsWith("Newtonsoft.Json.Linq", StringComparison.Ordinal))
                return "json";

            var dot = text.LastIndexOf('.');
            var shortName = dot >= 0 && dot < text.Length - 1 ? text[(dot + 1)..] : text;

            return shortName switch
            {
                "Double" => "double",
                "Single" => "float",
                "Int32" => "int",
                "Int64" => "long",
                "Int16" => "short",
                "Boolean" => "bool",
                "String" => "string",
                "Decimal" => "decimal",
                "JArray" => "json",
                "JObject" => "json",
                _ => shortName,
            };
        }

        private static string DisplayStatus(RecipeDeviationStatus status) => status switch
        {
            RecipeDeviationStatus.Equal => "一致",
            RecipeDeviationStatus.ValueDiff => "不同",
            RecipeDeviationStatus.TargetMissing => "目标缺失",
            RecipeDeviationStatus.TypeChanged => "类型变化",
            RecipeDeviationStatus.LinkedNow => "已链接",
            RecipeDeviationStatus.TriggerSource => "触发源",
            _ => "未应用",
        };

        private static RecipeChipKind KindOf(RecipeDeviationStatus status) => status switch
        {
            RecipeDeviationStatus.Equal => RecipeChipKind.Neutral,
            RecipeDeviationStatus.ValueDiff => RecipeChipKind.Warning,
            RecipeDeviationStatus.TargetMissing => RecipeChipKind.Danger,
            RecipeDeviationStatus.TypeChanged => RecipeChipKind.Danger,
            RecipeDeviationStatus.LinkedNow => RecipeChipKind.Accent,
            RecipeDeviationStatus.TriggerSource => RecipeChipKind.Warning,
            _ => RecipeChipKind.Neutral,
        };

        private void RaiseAllStateFlags()
        {
            RaisePropertyChanged(nameof(CanEdit));
            RaisePropertyChanged(nameof(CanOperateNow));
            RaisePropertyChanged(nameof(IsRunLocked));
            RaisePropertyChanged(nameof(CanApplyNow));
            RaisePropertyChanged(nameof(IsLoggedIn));
            RaisePropertyChanged(nameof(ActorName));
            RaisePropertyChanged(nameof(ShowPermissionBanner));
            RaisePropertyChanged(nameof(ShowLoginEntry));
            RaisePropertyChanged(nameof(LoginButtonText));
            RaisePropertyChanged(nameof(LoginButtonToolTip));
            RaisePropertyChanged(nameof(RoleText));
            RaisePropertyChanged(nameof(RoleKind));
            RaisePropertyChanged(nameof(RunStateText));
            RaisePropertyChanged(nameof(RunStateKind));
            RaisePropertyChanged(nameof(FooterHint));
            RaisePropertyChanged(nameof(FooterHintIsWarning));
            RaisePropertyChanged(nameof(EditBlockedReason));
            RaisePropertyChanged(nameof(ApplyBlockedReason));
            RaisePropertyChanged(nameof(ShowEmptyState));
            RaisePropertyChanged(nameof(ShowNoSelectionState));
            RaisePropertyChanged(nameof(ShowEditor));
            RaisePropertyChanged(nameof(CaptureCountText));
            RaisePropertyChanged(nameof(CaptureVisibleCountText));
            RaisePropertyChanged(nameof(CaptureCheckedCount));
            RaisePropertyChanged(nameof(CreateRecipeButtonText));
            RaisePropertyChanged(nameof(CaptureSelectionHint));
            RaisePropertyChanged(nameof(CreateRecipeBlockedReason));
            RaisePropertyChanged(nameof(ShowCaptureNonePicked));
            RaisePropertyChanged(nameof(ShowCaptureNoCandidates));
            CreateRecipeCommand.RaiseCanExecuteChanged();

            CaptureCommand.RaiseCanExecuteChanged();
            ApplyCommand.RaiseCanExecuteChanged();
            ApplyAndSaveCommand.RaiseCanExecuteChanged();
            AddCandidateCommand.RaiseCanExecuteChanged();
            RemoveParameterCommand.RaiseCanExecuteChanged();
            DeleteCommand.RaiseCanExecuteChanged();
            BeginRenameCommand.RaiseCanExecuteChanged();
            ClearActiveCommand.RaiseCanExecuteChanged();
            ExportCommand.RaiseCanExecuteChanged();
            ImportCommand.RaiseCanExecuteChanged();
            OpenCaptureCommand.RaiseCanExecuteChanged();
        }

        private void OnServiceStateChanged()
        {
            // 服务改的是"配方集合/生效指针"；权限与运行锁是另一条链，
            // 一起刷新是因为应用成功会改 ActiveRecipeId、进而影响"生效中"标记。
            Refresh();
        }

        // ────────────────────────────────────────────────────────────────
        //  命令
        // ────────────────────────────────────────────────────────────────

        private bool CanCapture()
            => !IsBusy && CanEdit && !string.IsNullOrWhiteSpace(NewRecipeName);

        private bool CanApply()
            => !IsBusy && !IsConfirmingApply && SelectedRecipe != null && CanOperateNow && !IsRunLocked;

        /// <summary>
        /// 表头全选框：只作用于<b>表格里此刻看得见的行</b>（<see cref="ParametersView"/> 已按"仅显示差异"
        /// 与查找框过滤过）。拿 <see cref="Parameters"/> 全量去勾会把用户看不见的行也勾上——
        /// 用户看到"我只勾了 3 行"，点删除却删掉 1195 行，那是灾难。
        /// </summary>
        private void ToggleCheckAll()
        {
            var rows = ParametersView.Cast<RecipeParameterRow>().ToList();
            if (rows.Count == 0) return;

            // 全选 / 全不选按"可见行是否已全勾"判，而不是拿勾选数跟总数比：
            // 总数里含被过滤掉的行，比出来的结果会让点一次之后永远停在半选。
            bool target = rows.Any(r => !r.IsChecked);
            foreach (var row in rows)
                row.IsChecked = target;
        }

        /// <summary>
        /// 表头全选框的三态：可见行全勾 → true，全不勾 → false，部分勾 → null。
        /// 与 <see cref="ToggleCheckAll"/> 同源，都只认表格里看得见的行。
        /// </summary>
        public bool? CheckAllState
        {
            get
            {
                var rows = ParametersView.Cast<RecipeParameterRow>().ToList();
                if (rows.Count == 0) return false;

                int checkedVisible = rows.Count(r => r.IsChecked);
                if (checkedVisible == 0) return false;
                return checkedVisible == rows.Count ? true : null;
            }
        }

        private void Capture()
        {
            var result = _service.Capture(NewRecipeName, NewRecipeCode, NewRecipeNotes);
            SetStatus(result.Message, !result.Success);
            if (!result.Success) return;

            NewRecipeName = string.Empty;
            NewRecipeCode = string.Empty;
            NewRecipeNotes = string.Empty;
            IsCapturing = false;

            Refresh();
            if (result.Recipe != null)
                SelectedItem = RecipeItems.FirstOrDefault(i => i.RecipeId == result.Recipe.RecipeId);
        }

        /// <summary>
        /// v2 底栏「创建配方（N）」：只捕获用户勾上的那些参数。
        /// <para>
        /// 与旧 <see cref="Capture"/>（全量）不是同一条路：零勾选时 Service 直接拒绝，
        /// 不会静默退化成"全部捕获"——全量正是用户否掉的那种做法。
        /// </para>
        /// </summary>
        /// <summary>
        /// 「创建配方」为什么点不了 —— 灰按钮必须自己说清原因，别让人对着它猜。
        /// 三条判据与 <see cref="CanCreateRecipe"/> 一一对应，顺序也是"先说最容易撞上的那条"。
        /// 全部满足时返回 null（XAML 侧 TargetNullValue 回落到正常提示）。
        /// </summary>
        public string? CreateRecipeBlockedReason
        {
            get
            {
                if (IsBusy) return "正在处理上一项操作…";
                if (string.IsNullOrWhiteSpace(NewRecipeName)) return "还没填配方名称";
                if (CaptureCheckedCount == 0) return "还没勾选参数：配方至少要有 1 条，在候选表里勾上即可";
                return null;
            }
        }

        private bool CanCreateRecipe()
            => !IsBusy && !string.IsNullOrWhiteSpace(NewRecipeName) && CaptureCheckedCount > 0;

        private void CreateRecipe()
        {
            if (!CanEdit)
            {
                // 与 OpenCapture 一致：没权限就把话说出来，不静默。
                SetStatus(_service.CanEdit(out var blocked) ? "当前权限不足，无法创建配方。" : blocked, isError: true);
                return;
            }

            var selection = CaptureCandidates
                .Where(c => c.IsChecked && !c.IsDisabled)
                .Select(c => (c.Kind, c.TargetId, c.Key))
                .ToList();

            if (selection.Count == 0)
            {
                SetStatus("还没有勾选任何参数。请在候选表里勾上要存进配方的参数。", isError: true);
                return;
            }

            var result = _service.Capture(NewRecipeName, NewRecipeCode, NewRecipeNotes, selection);
            SetStatus(result.Message, !result.Success);
            if (!result.Success) return;

            NewRecipeName = string.Empty;
            NewRecipeCode = string.Empty;
            NewRecipeNotes = string.Empty;
            IsCreatingRecipe = false;

            Refresh();
            if (result.Recipe != null)
                SelectedItem = RecipeItems.FirstOrDefault(i => i.RecipeId == result.Recipe.RecipeId);
        }

        private void BeginPreview(bool saveAfter)
        {
            var recipe = SelectedRecipe;
            if (recipe == null) return;

            var preview = _service.Preview(recipe.RecipeId);
            _confirmSaveAfter = saveAfter;

            PreviewItems.Clear();
            foreach (var item in preview.Items)
            {
                PreviewItems.Add(new RecipePreviewRow
                {
                    Target = item.Target,
                    Key = item.Key,
                    Current = string.IsNullOrEmpty(item.Current) ? "—" : item.Current,
                    Recipe = string.IsNullOrEmpty(item.Recipe) ? "—" : item.Recipe,
                    StatusText = DisplayStatus(item.Status),
                    StatusKind = KindOf(item.Status),
                    IsDifferent = item.Status != RecipeDeviationStatus.Equal,
                });
            }

            PreviewCountText = $"共 {preview.Total} 条 · 修改 {preview.Modified} · 不变 {preview.Unchanged}";
            PreviewErrors = preview.Errors;
            PreviewWarnings = preview.Warnings;
            PreviewErrorText = BuildPreviewError(preview);
            PreviewCanApply = preview.CanApply;

            RaisePropertyChanged(nameof(PreviewCountText));
            RaisePropertyChanged(nameof(PreviewTitle));
            RaisePropertyChanged(nameof(PreviewErrorText));
            RaisePropertyChanged(nameof(PreviewErrorsText));
            RaisePropertyChanged(nameof(PreviewHasError));
            RaisePropertyChanged(nameof(PreviewFooterNote));

            IsConfirmingApply = true;

            SetStatus(
                preview.CanApply
                    ? (saveAfter ? "确认后将应用并保存方案" : "确认后将应用（只写内存，不保存）")
                    : "预检有错误，整单拒绝；请先修正配方后再应用",
                !preview.CanApply);
        }

        /// <summary>
        /// 确认层的具名错误块：把第一条错误原文摆出来，而不是只给一个数字。
        /// 用户要的是"哪一条坏了"，不是一个"错误 1"。
        /// </summary>
        private static string BuildPreviewError(RecipeApplyPreview preview)
        {
            var first = preview.Items.FirstOrDefault(i => i.Status == RecipeDeviationStatus.TargetMissing)
                        ?? preview.Items.FirstOrDefault(i => i.Status == RecipeDeviationStatus.TypeChanged);

            if (first == null)
                return preview.Errors > 0 ? $"预检未通过（{preview.Errors} 处错误），确认应用不可用。" : string.Empty;

            var reason = first.Status == RecipeDeviationStatus.TargetMissing
                ? "目标已从流程中移除。请先删除这条参数，或恢复该步骤后再应用。"
                : "当前参数类型与配方记录的不一致，写入会被拒绝。请重新捕获这条参数。";

            return $"{first.Target} · {first.Key}：{reason}";
        }

        private async void ConfirmApply()
        {
            var recipe = SelectedRecipe;
            if (recipe == null) return;

            IsBusy = true;
            try
            {
                var apply = _service.Apply(recipe.RecipeId);
                if (!apply.Success)
                {
                    SetStatus(apply.Message, true);
                    return;
                }

                if (_confirmSaveAfter)
                {
                    var save = await _saveCoordinator.SaveToCurrentPathAsync();
                    SetStatus(
                        apply.Message + "；" + (save.Success ? "方案已保存" : "保存失败：" + save.Message),
                        !save.Success);
                }
                else
                {
                    SetStatus(apply.Message, false);
                }
            }
            catch (Exception ex)
            {
                SetStatus("应用失败：" + ex.Message, true);
            }
            finally
            {
                IsBusy = false;
                IsConfirmingApply = false;
                Refresh();
            }
        }

        private void CancelPreview()
        {
            IsConfirmingApply = false;
            PreviewCanApply = false;
            PreviewItems.Clear();
            PreviewCountText = string.Empty;
            PreviewErrorText = string.Empty;
            PreviewErrors = 0;
            PreviewWarnings = 0;
            RaisePropertyChanged(nameof(PreviewCountText));
            RaisePropertyChanged(nameof(PreviewErrorText));
            RaisePropertyChanged(nameof(PreviewErrorsText));
            RaisePropertyChanged(nameof(PreviewHasError));
            RaisePropertyChanged(nameof(PreviewFooterNote));
        }

        private void AddCandidate()
        {
            var recipe = SelectedRecipe;
            var candidate = SelectedCandidate;
            if (recipe == null || candidate == null) return;

            var ok = _service.AddEntry(recipe.RecipeId, candidate.Kind, candidate.TargetId, candidate.Key, out var error);
            SetStatus(ok ? $"已添加参数「{candidate.DisplayName}」" : error, !ok);
            Refresh();
        }

        /// <summary>
        /// 删除勾选的参数。逐条走服务（每条都会递增内容版本），失败的那条留在表里并报原因；
        /// 一条都没删成时不刷新，避免"看起来删了其实没删"。
        /// </summary>
        private void RemoveCheckedParameters()
        {
            var recipe = SelectedRecipe;
            if (recipe == null) return;

            var targets = Parameters.Where(p => p.IsChecked).Select(p => p.Identity).ToList();
            if (targets.Count == 0) return;

            int removed = 0;
            string? firstError = null;

            foreach (var (kind, targetId, key) in targets)
            {
                if (_service.RemoveEntry(recipe.RecipeId, kind, targetId, key, out var error))
                    removed++;
                else
                    firstError ??= error;
            }

            if (removed == 0)
            {
                SetStatus(firstError ?? "删除参数失败", true);
                return;
            }

            SetStatus(
                firstError == null
                    ? $"已删除 {removed} 条参数"
                    : $"已删除 {removed} 条；另有失败：{firstError}",
                firstError != null);

            Refresh();
        }

        private void Delete()
        {
            var recipe = SelectedRecipe;
            if (recipe == null) return;

            var answer = MessageBox.Show(
                $"删除配方「{recipe.Name}」？\n\n只删除方案内的配方定义，不删除 .vms 文件；当前参数值保持现状。",
                "删除配方",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            var ok = _service.Delete(recipe.RecipeId, out var error);
            SetStatus(ok ? $"已删除配方「{recipe.Name}」" : error, !ok);
            Refresh();
        }

        private void BeginRename()
        {
            var recipe = SelectedRecipe;
            if (recipe == null) return;

            EditNameText = recipe.Name;
            IsEditingName = true;
        }

        private void CommitRename()
        {
            var recipe = SelectedRecipe;
            if (recipe == null) return;

            var name = EditNameText.Trim();
            if (string.Equals(name, recipe.Name, StringComparison.Ordinal))
            {
                IsEditingName = false;
                return;
            }

            var ok = _service.Rename(recipe.RecipeId, name, out var error);
            SetStatus(ok ? $"已重命名为「{name}」" : error, !ok);
            IsEditingName = false;
            Refresh();
        }

        private void ClearActive()
        {
            var ok = _service.ClearActive(out var error);
            SetStatus(ok ? "已取消生效标记；参数值保持现状" : error, !ok);
            Refresh();
        }

        private void Export()
        {
            var recipe = SelectedRecipe;
            if (recipe == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "VisionMaster配方 (*.vmrecipe)|*.vmrecipe|所有文件 (*.*)|*.*",
                Title = "导出配方",
                DefaultExt = ".vmrecipe",
                FileName = recipe.Name + ".vmrecipe",
            };
            if (dialog.ShowDialog() != true) return;

            var ok = _fileService.Export(recipe.RecipeId, dialog.FileName, out var error);
            SetStatus(ok ? $"已导出到 {dialog.FileName}" : error, !ok);
        }

        private void Import()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "VisionMaster配方 (*.vmrecipe)|*.vmrecipe|所有文件 (*.*)|*.*",
                Title = "导入配方",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog() != true) return;

            var ok = _fileService.Import(dialog.FileName, out var imported, out var error);
            SetStatus(ok
                ? $"已导入配方「{imported?.Name}」（未应用；请核对目标后手动应用）"
                : error, !ok);
            Refresh();
            if (ok && imported != null)
                SelectedItem = RecipeItems.FirstOrDefault(i => i.RecipeId == imported.RecipeId);
        }

        private void SetStatus(string text, bool isError)
        {
            StatusText = text ?? string.Empty;
            StatusIsError = isError;
        }

        public bool CanCloseDialog() => !IsBusy;

        public void OnDialogClosed()
        {
            _service.StateChanged -= OnServiceStateChanged;
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            // 宿主经参数把「登录」出口交进来（Action&lt;Action&gt;：参数就是本 VM 要赋值的那个委托）。
            // 拿不到就不赋值——按钮自己藏起来，不留死键。
            if (parameters.ContainsKey(RequestLoginParameter)
                && parameters.GetValue<Action<Action>>(RequestLoginParameter) is { } register)
            {
                register(ShowLoginByHost);
            }

            Refresh();
        }

        /// <summary>参数键：宿主注册「登录」出口用</summary>
        public const string RequestLoginParameter = "RequestLogin";

        /// <summary>
        /// 按钮被按下时转给宿主；宿主自己的弹窗结束后会回到这里刷新权限。
        /// <para>
        /// 重入闸：出口若被宿主接成"再调回来"（例如把回调误绑成同一个命令），
        /// 不拦就是无限递归——探针实测栈溢出 10706 层。这里只放行最外层那一次。
        /// </para>
        /// </summary>
        private void ShowLoginByHost()
        {
            if (_loginEntryActive) return;
            _loginEntryActive = true;
            try
            {
                RequestLogin?.Invoke();
                Refresh();
            }
            finally
            {
                _loginEntryActive = false;
            }
        }

        private bool _loginEntryActive;

        /// <summary>
        /// 「新建配方」入口：先看权限，够了才真开表单。
        /// <para>
        /// 未登录 / 非工程师时<b>不是把按钮灰掉</b>，而是按下之后先弹登录
        /// （<see cref="RequestLogin"/> 由宿主接成 <c>ScadaLoginView</c>），登录成功再继续开表单。
        /// 用户的原话是"点新建没反应"——置灰的按钮既没反馈也不说理由，等于死键。
        /// </para>
        /// <para>
        /// 登录后仍然没有工程师权限（比如用操作员账号登录），就在状态行说清楚，
        /// 不留"点两次还是没动"的悬念。
        /// </para>
        /// </summary>
        private void OpenCapture()
        {
            if (IsBusy) return;

            if (!CanEdit)
            {
                if (RequestLogin == null)
                {
                    // 宿主没注入登录出口：只能把理由说清楚，别装作点了有用。
                    SetStatus(_service.CanEdit(out var blocked)
                        ? "没有可用的登录出口，无法在这里提权。"
                        : blocked, isError: true);
                    return;
                }

                ShowLoginByHost();

                if (!CanEdit)
                {
                    _service.CanEdit(out var stillBlocked);
                    SetStatus(string.IsNullOrWhiteSpace(stillBlocked)
                        ? "登录后仍没有新建配方所需的权限（需要工程师及以上）。"
                        : stillBlocked, isError: true);
                    return;
                }
            }

            IsCreatingRecipe = true;
            IsCapturing = true;   // 兼容旧 XAML 的内联卡片；v2 弹层只看 IsCreatingRecipe
            BindCaptureCandidates();
        }
    }
}
