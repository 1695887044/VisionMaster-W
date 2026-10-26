using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 用户管理大窗（v4）：账号与用户组 / 在线会话 / 审计记录三页签。
    /// 账号页签的表单逻辑在 <see cref="ScadaUserManagerViewModel"/>；本类只做外壳、状态与另外两页。
    /// </summary>
    public sealed class UserManagementViewModel : BindableBase, IDialogAware
    {
        /// <summary>审计结果列的固定筛选项（与 <see cref="ScadaAuditOutcome"/> 的四个值 + 历史值归一桶一致）。</summary>
        private static readonly string[] OutcomeFilterOptions =
            { "全部", "成功", "失败", "未执行", "被拒绝", "警告" };

        private readonly ScadaUserStore _store;
        private readonly ScadaAccessPolicy _accessPolicy;
        private readonly SolutionIdentityHolder _identity;
        private readonly AppSettingsService? _settings;
        private IScadaWebSessionAdmin? _webSessionAdmin;
        private string? _message;

        private int _selectedTabIndex;
        private string _fromDateText = DateTime.Today.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private string _toDateText = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private string _actorFilter = "全部";
        private string _outcomeFilter = "全部";
        private bool _showUnassigned;
        private string _auditMetaText = string.Empty;
        private bool _confirmingKickAll;

        public UserManagementViewModel(
            ScadaUserStore store,
            ScadaAccessPolicy accessPolicy,
            SolutionIdentityHolder identity)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            try { _webSessionAdmin = Prism.Ioc.ContainerLocator.Container.Resolve<IScadaWebSessionAdmin>(); }
            catch { _webSessionAdmin = null; }
            try { _settings = Prism.Ioc.ContainerLocator.Container.Resolve<AppSettingsService>(); }
            catch { _settings = null; }

            Account = new ScadaUserManagerViewModel(store, accessPolicy,
                () => _settings?.Current.AuditRetentionDays ?? AppConfigModel.DefaultAuditRetentionDays);

            RefreshCommand = new DelegateCommand(Refresh);
            QueryAuditCommand = new DelegateCommand(LoadAudit);
            KickCommand = new DelegateCommand<string>(Kick);
            KickAllCommand = new DelegateCommand(KickAll, CanKickAll);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));

            Refresh();
        }

        public DialogCloseListener RequestClose { get; set; }
        public string Title => "用户管理";

        public ScadaUserManagerViewModel Account { get; }

        public ObservableCollection<UserSessionRow> Sessions { get; } = new();
        public ObservableCollection<ScadaAuditRowView> AuditRows { get; } = new();

        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand QueryAuditCommand { get; }
        public DelegateCommand<string> KickCommand { get; }
        public DelegateCommand KickAllCommand { get; }
        public DelegateCommand CloseCommand { get; }

        #region 页签 / 底栏

        /// <summary>当前页签（0 账号 / 1 会话 / 2 审计）。绑 TabControl.SelectedIndex。</summary>
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (SetProperty(ref _selectedTabIndex, value))
                {
                    RaisePropertyChanged(nameof(FooterNote));
                    RaisePropertyChanged(nameof(FooterText));
                }
            }
        }

        public string FooterNote => SelectedTabIndex switch
        {
            1 => "会话由 Web 运行态创建；撤销后该浏览器立即掉线。",
            2 => "审计只读；默认只显示当前方案标签的行。",
            _ => "账号本属于当前方案；修改在保存方案后生效。",
        };

        /// <summary>底栏左：有操作回执时显示回执，否则显示当前页签的固定说明。</summary>
        public string FooterText => string.IsNullOrEmpty(Message) ? FooterNote : Message!;

        #endregion

        #region 状态 / 横幅

        public bool IsAdmin => _accessPolicy.Can(ScadaPermissionKeys.UserAdministration, out _);
        public bool IsNoSolution => _store.BookState == ScadaAccountBookState.NoSolution;
        public bool IsUnsavedSolution => _store.BookState == ScadaAccountBookState.UnsavedSolution;
        public bool IsNotAdministrator => !IsAdmin;
        public bool IsFactoryFallback => _store.BookSource == ScadaBookSource.Factory;
        public bool HasLoadError => !string.IsNullOrWhiteSpace(_store.LastLoadError);
        public string LoadErrorText => _store.LastLoadError ?? string.Empty;

        /// <summary>在线会话管理口。<b>测试/探针注入口</b>：生产由容器解析，探针直接赋值。</summary>
        public IScadaWebSessionAdmin? WebSessionAdmin
        {
            get => _webSessionAdmin;
            set
            {
                _webSessionAdmin = value;
                RaisePropertyChanged(nameof(IsWebUnavailable));
                KickAllCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsWebUnavailable => _webSessionAdmin == null || !_webSessionAdmin.IsAvailable;

        /// <summary>审计读取端。<b>测试/探针注入口</b>：生产用默认目录，探针指向临时目录。</summary>
        public ScadaAuditReader AuditReader { get; set; } = new();

        /// <summary>只显示一条状态横幅，优先级：加载失败 → 无方案 → 未保存 → 出厂账号。</summary>
        public string? BannerText
        {
            get
            {
                if (HasLoadError) return LoadErrorText;
                if (IsNoSolution) return "尚未打开方案：账号本未绑定，只能查看空态。";
                if (IsUnsavedSolution) return "方案尚未保存：账号修改会在保存方案后生效；请先保存方案。";
                if (IsFactoryFallback) return "当前使用出厂账号（admin/admin123），首次保存方案时写入账号本。";
                return null;
            }
        }

        public bool HasBanner => !string.IsNullOrEmpty(BannerText);

        public string? Message
        {
            get => _message;
            private set
            {
                if (SetProperty(ref _message, value))
                    RaisePropertyChanged(nameof(FooterText));
            }
        }

        #endregion

        #region 在线会话

        public string SessionCountText => $"{Sessions.Count} 个在线";

        public bool HasSessions => Sessions.Count > 0;

        /// <summary>「全部踢下线」的二次确认态。按钮文案随之切换。</summary>
        public bool ConfirmingKickAll
        {
            get => _confirmingKickAll;
            private set
            {
                if (SetProperty(ref _confirmingKickAll, value))
                    RaisePropertyChanged(nameof(KickAllText));
            }
        }

        public string KickAllText => ConfirmingKickAll ? "确认全部踢下线" : "全部踢下线";

        private bool CanKickAll() => !IsWebUnavailable && Sessions.Count > 0;

        #endregion

        #region 审计筛选

        /// <summary>开始日期（<c>yyyy-MM-dd</c>）。用文本框而不是 DatePicker：全仓没有 DatePicker 样式，
        /// 引一个未换皮的控件会在弹窗里显得像另一个年代（基线也是纯文本输入）。</summary>
        public string FromDateText
        {
            get => _fromDateText;
            set => SetProperty(ref _fromDateText, value);
        }

        public string ToDateText
        {
            get => _toDateText;
            set => SetProperty(ref _toDateText, value);
        }

        public ObservableCollection<string> ActorOptions { get; } = new() { "全部" };

        public IReadOnlyList<string> OutcomeOptions => OutcomeFilterOptions;

        public string ActorFilter
        {
            get => _actorFilter;
            set => SetProperty(ref _actorFilter, value);
        }

        public string OutcomeFilter
        {
            get => _outcomeFilter;
            set => SetProperty(ref _outcomeFilter, value);
        }

        public bool ShowUnassigned
        {
            get => _showUnassigned;
            set => SetProperty(ref _showUnassigned, value);
        }

        public string AuditMetaText
        {
            get => _auditMetaText;
            private set => SetProperty(ref _auditMetaText, value);
        }

        public bool HasAuditRows => AuditRows.Count > 0;

        #endregion

        public void Refresh()
        {
            Account.Reload();

            Sessions.Clear();
            if (!IsWebUnavailable)
            {
                foreach (var session in _webSessionAdmin!.ListOnline())
                    Sessions.Add(new UserSessionRow(session));
            }

            LoadAudit();

            RaisePropertyChanged(nameof(SessionCountText));
            RaisePropertyChanged(nameof(HasSessions));
            RaisePropertyChanged(nameof(IsNoSolution));
            RaisePropertyChanged(nameof(IsUnsavedSolution));
            RaisePropertyChanged(nameof(IsNotAdministrator));
            RaisePropertyChanged(nameof(IsFactoryFallback));
            RaisePropertyChanged(nameof(HasLoadError));
            RaisePropertyChanged(nameof(LoadErrorText));
            RaisePropertyChanged(nameof(IsWebUnavailable));
            RaisePropertyChanged(nameof(BannerText));
            RaisePropertyChanged(nameof(HasBanner));
            KickAllCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 读审计并按页头口径过滤：
        /// <list type="number">
        /// <item>默认只留"当前方案标签"的行（别的方案永远不混入）；</item>
        /// <item>勾选「显示未归属」再并入 <c>[sol=none]</c> 与完全没有标签的历史行；</item>
        /// <item>最后叠加操作者 / 结果两个下拉。</item>
        /// </list>
        /// 「共 N 行」= 第 1、2 步之后的行数（即本次范围内的总量），「显示 X 条」= 三个条件都过完的行数。
        /// </summary>
        public void LoadAudit()
        {
            AuditRows.Clear();

            if (!DateTime.TryParseExact((FromDateText ?? string.Empty).Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
                || !DateTime.TryParseExact((ToDateText ?? string.Empty).Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
            {
                AuditMetaText = "日期格式不正确：请用 yyyy-MM-dd。";
                RaisePropertyChanged(nameof(HasAuditRows));
                return;
            }

            if (from.Date > to.Date)
            {
                AuditMetaText = "日期范围不合法：开始日期晚于结束日期。";
                RaisePropertyChanged(nameof(HasAuditRows));
                return;
            }

            var read = AuditReader.Read(from, to);
            var tag = _identity.AuditTag;

            var scoped = new List<ScadaAuditRow>();
            if (!string.IsNullOrEmpty(tag) && tag != "[sol=none]")
            {
                scoped.AddRange(read.Rows.Where(r =>
                    r.HasTag && r.Detail.StartsWith(tag, StringComparison.Ordinal)));
            }

            if (ShowUnassigned)
            {
                // [sol=none]（明确无方案）与完全没有标签的历史行；别的未保存方案不算未归属。
                scoped.AddRange(read.Rows.Where(r =>
                    !r.HasTag || (!r.UnsavedSolution && r.SolutionId == null)));
            }

            scoped = scoped.OrderByDescending(r => r.Timestamp).ToList();

            RebuildActorOptions(scoped);

            var filtered = scoped
                .Where(r => ActorFilter == "全部" || ScadaAuditRowView.ActorLabel(r.Actor) == ActorFilter)
                .Where(r => OutcomeFilter == "全部" || ScadaAuditRowView.OutcomeLabel(r.OutcomeText) == OutcomeFilter)
                .ToList();

            foreach (var row in filtered)
                AuditRows.Add(new ScadaAuditRowView(row));

            AuditMetaText = $"显示 {AuditRows.Count} 条 / 共 {scoped.Count} 行 / 已跳过 {read.SkippedLines} 行（格式异常）";
            RaisePropertyChanged(nameof(HasAuditRows));
        }

        private void RebuildActorOptions(IReadOnlyList<ScadaAuditRow> rows)
        {
            var actors = rows.Select(r => ScadaAuditRowView.ActorLabel(r.Actor))
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(a => a, StringComparer.Ordinal)
                .ToArray();

            ActorOptions.Clear();
            ActorOptions.Add("全部");
            foreach (var actor in actors)
                ActorOptions.Add(actor);

            if (!ActorOptions.Contains(ActorFilter))
                ActorFilter = "全部";
        }

        private void Kick(string? sessionRef)
        {
            if (IsWebUnavailable || string.IsNullOrWhiteSpace(sessionRef)) return;
            ConfirmingKickAll = false;

            if (!_webSessionAdmin!.TryKick(sessionRef))
            {
                Message = "该会话已断开或撤销失败";
                return;
            }

            Message = "会话已撤销";
            Refresh();
        }

        private void KickAll()
        {
            if (IsWebUnavailable || Sessions.Count == 0) return;

            if (!ConfirmingKickAll)
            {
                ConfirmingKickAll = true;
                Message = "再次点击「确认全部踢下线」将断开所有 Web 会话。";
                return;
            }

            ConfirmingKickAll = false;
            var count = _webSessionAdmin!.KickAll();
            Message = $"已断开 {count} 个会话。";
            Refresh();
        }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }
        public void OnDialogOpened(IDialogParameters parameters) => Refresh();
    }

    public sealed class UserSessionRow
    {
        public UserSessionRow(ScadaWebSessionInfo info) => Info = info;

        public ScadaWebSessionInfo Info { get; }

        public string SessionRef => Info.SessionRef;

        /// <summary>会话列只显示前 8 位（完整值在 ToolTip 里）。16 hex 铺满列会把 IP 挤掉。</summary>
        public string SessionShort =>
            string.IsNullOrEmpty(Info.SessionRef) ? "—"
            : Info.SessionRef.Length <= 8 ? Info.SessionRef
            : Info.SessionRef.Substring(0, 8);

        public string UserName => string.IsNullOrWhiteSpace(Info.UserName) ? "匿名" : Info.UserName!;

        public string GroupName => string.IsNullOrWhiteSpace(Info.GroupName) ? Info.GroupId : Info.GroupName;

        public string RemoteIp => string.IsNullOrWhiteSpace(Info.RemoteIp) ? "—" : Info.RemoteIp!;

        public string ConnectedText => Info.ConnectedAtUtc.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        public string LastSeenText => Info.LastSeenUtc.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        public bool IsReadOnly => Info.ReadOnly;

        public string ReadOnlyText => Info.ReadOnly ? "只读" : "可操作";
    }

    /// <summary>
    /// 审计表的一行（显示层）。
    ///
    /// 两个"归一"都在这里做，不散到 XAML：
    /// <list type="bullet">
    /// <item>时间：<c>MM-dd HH:mm:ss</c>——日期范围可跨天，只有时分秒定位不到；</item>
    /// <item>结果：CSV 里出现的历史/外部值（如 <c>Warning</c>）统一落进「警告」桶，
    ///       英文原值不上屏（R84/R85 同类口径）。</item>
    /// </list>
    /// </summary>
    public sealed class ScadaAuditRowView
    {
        public ScadaAuditRowView(ScadaAuditRow row) => Row = row;

        public ScadaAuditRow Row { get; }

        public string TimeText => Row.Timestamp.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        public string ActorText => ActorLabel(Row.Actor);

        public string Subject => string.IsNullOrWhiteSpace(Row.Subject) ? "—" : Row.Subject;

        public string EventText => string.IsNullOrWhiteSpace(Row.EventText) ? "—" : Row.EventText;

        public string ActionText => string.IsNullOrWhiteSpace(Row.ActionText) ? "—" : Row.ActionText;

        public string OutcomeText => OutcomeLabel(Row.OutcomeText);

        /// <summary>chip 的语义色：ok / bad / muted / warn。</summary>
        public string OutcomeKind => OutcomeLabel(Row.OutcomeText) switch
        {
            "成功" => "ok",
            "失败" => "bad",
            "被拒绝" => "bad",
            "未执行" => "muted",
            _ => "warn",
        };

        public string DetailText =>
            string.IsNullOrWhiteSpace(Row.DetailRemainder) ? "—" : Row.DetailRemainder;

        /// <summary>「未登录」= 运行态没人登录时按的按钮；直接写"未登录"会被读成系统 bug。</summary>
        public static string ActorLabel(string? actor)
            => string.IsNullOrWhiteSpace(actor) ? "运行态未登录"
            : string.Equals(actor.Trim(), "未登录", StringComparison.Ordinal) ? "运行态未登录"
            : actor.Trim();

        /// <summary>结果归一到五桶。空值与任何未登记值都落「警告」，绝不把原值直接上屏。</summary>
        public static string OutcomeLabel(string? outcome)
        {
            var text = (outcome ?? string.Empty).Trim();
            return text switch
            {
                "成功" => "成功",
                "失败" => "失败",
                "未执行" => "未执行",
                "被拒绝" => "被拒绝",
                "" => "未执行",
                _ => "警告",
            };
        }
    }
}
