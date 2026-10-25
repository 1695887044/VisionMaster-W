using System;
using System.ComponentModel;
using System.Windows.Media;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 标题栏配方 chip 的视图模型：只做“现在跑的是哪个配方、偏没偏”的展示，
    /// 切换与编辑都进配方管理器（v1 不做快捷切换浮层）。
    /// </summary>
    public sealed class RecipeViewModel : BindableBase
    {
        private readonly RecipeService _service;
        private readonly IDialogService _dialogService;

        /// <summary>配方管理器交回来的「登录」出口（null = 没接上，按钮会自己藏起来）</summary>
        private Action? _requestLogin;

        public DelegateCommand OpenManagerCommand { get; }

        public RecipeViewModel(RecipeService service, IDialogService dialogService, IWorkspaceManager workspace)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            OpenManagerCommand = new DelegateCommand(OpenManager);

            _service.StateChanged += Refresh;
            if (workspace is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += OnWorkspacePropertyChanged;

            Refresh();
        }

        private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IWorkspaceManager.CurrentSolution)
                || e.PropertyName == nameof(IWorkspaceManager.CurrentFlow))
            {
                Refresh();
            }
        }

        private void OpenManager()
        {
            // 「登录」按钮的出口由宿主给：配方管理器自己不碰 IDialogService。
            // 出口为 null 时那个按钮自己会藏起来（不再留一个点了没反应的死键）。
            var parameters = new DialogParameters
            {
                { DialogViewModels.RecipeManagerViewModel.RequestLoginParameter, new Action<Action>(RegisterLoginEntry) },
            };
            _dialogService.ShowDialog("RecipeManagerView", parameters);
            Refresh();
        }

        private void ShowLoginDialog()
        {
            _dialogService.ShowDialog("ScadaLoginView");
            Refresh();
        }

        /// <summary>
        /// 配方管理器把它的「登录」出口交回来，本 VM 接成弹 Shell 那个登录弹窗
        /// （同一份会话状态：登录成功后 <see cref="RecipeService.StateChanged"/> 会把权限刷成最新）。
        /// <para>
        /// 这里必须把 <paramref name="request"/> 真正包进委托里再交给它，不能只"存下"。
        /// 2026-10-24 踩过：旧实现是 <c>_requestLogin = request;</c>，而 <c>_requestLogin</c>
        /// 全仓没有读取点，<see cref="ShowLoginDialog"/> 也没有调用点 —— 于是登录按钮
        /// 弹不出窗、也没有任何报错（探针自建回调把这条断链掩盖了）。
        /// </para>
        /// </summary>
        private void RegisterLoginEntry(Action request)
            => _requestLogin = () =>
            {
                ShowLoginDialog();
                request();
            };

        public void Refresh()
        {
            var solution = _service.Solution;
            var active = _service.ActiveRecipe;
            var recipes = _service.Recipes;
            var deviation = _service.ComputeDeviation();

            HasRecipes = recipes.Count > 0;
            IsDangling = solution?.ActiveRecipeId != null && active == null;
            IsDeviated = active != null && deviation.Deviated > 0;
            DeviationCount = deviation.Deviated;
            ActiveRecipeName = active?.Name ?? (IsDangling ? "配方已丢失" : "未应用配方");
            ActiveRecipeVersion = active?.ContentVersion ?? 0;

            ChipText = active != null
                ? $"{active.Name} · v{active.ContentVersion}"
                : HasRecipes ? "未应用配方" : "无配方";

            ChipStatus = IsDangling
                ? "配方悬空"
                : IsDeviated
                    ? $"偏离 {DeviationCount}"
                    : active != null
                        ? "已生效"
                        : HasRecipes ? "未应用" : "未启用";

            ChipBrush = IsDangling || IsDeviated
                ? Brushes.OrangeRed
                : active != null
                    ? Brushes.ForestGreen
                    : Brushes.Gray;

            ChipToolTip = IsDangling
                ? "当前生效指针指向的配方已不存在；请打开配方管理器清除指针"
                : active != null
                    ? $"当前配方：{active.Name}（v{active.ContentVersion}）"
                      + (IsDeviated ? $"，{DeviationCount} 项参数已偏离" : "，参数一致")
                    : HasRecipes
                        ? "尚未应用任何配方；打开配方管理器选择并应用"
                        : "本方案还没有配方；打开配方管理器从当前值捕获";

            RaisePropertyChanged(nameof(HasRecipes));
            RaisePropertyChanged(nameof(IsDangling));
            RaisePropertyChanged(nameof(IsDeviated));
            RaisePropertyChanged(nameof(DeviationCount));
            RaisePropertyChanged(nameof(ActiveRecipeName));
            RaisePropertyChanged(nameof(ActiveRecipeVersion));
            RaisePropertyChanged(nameof(ChipText));
            RaisePropertyChanged(nameof(ChipStatus));
            RaisePropertyChanged(nameof(ChipBrush));
            RaisePropertyChanged(nameof(ChipToolTip));
        }

        public bool HasRecipes { get; private set; }
        public bool IsDangling { get; private set; }
        public bool IsDeviated { get; private set; }
        public int DeviationCount { get; private set; }
        public string ActiveRecipeName { get; private set; } = "未应用配方";
        public int ActiveRecipeVersion { get; private set; }
        public string ChipText { get; private set; } = "无配方";
        public string ChipStatus { get; private set; } = "未启用";
        public Brush ChipBrush { get; private set; } = Brushes.Gray;
        public string ChipToolTip { get; private set; } = string.Empty;
    }
}
