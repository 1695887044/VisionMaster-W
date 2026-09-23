using System;
using System.Windows.Threading;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaAccessPolicy"/> 的默认实现：<b>WPF 侧的"谁在操作"</b>——
    /// 登录会话 + 空闲自动登出 + 权限判定出口。
    ///
    /// 它解决的是什么问题
    /// ---------
    /// 领域层（<see cref="ScadaRuntime"/>）需要回答"此刻这个人配不配按这个按钮"，
    /// 但它<b>不知道也不该知道</b>密码框长什么样、有没有人在场、空闲了多久。
    /// 本类就是那一半：把"界面能力"翻译成领域层认的四个问题
    /// （<see cref="CurrentRole"/> / <see cref="IsLoggedIn"/> / <see cref="CurrentUserName"/> /
    /// <see cref="CanOperate"/>）。
    ///
    /// 判定规则<b>一条都不在这里写</b>
    /// ---------
    /// "未登录算操作员""高角色含低角色""非法值一律拒绝"三条全部委托给
    /// <see cref="ScadaRoleExtensions.Allows"/>。本类只负责"此刻角色是几"，
    /// 规则归领域层——两份实现分头演化的后果是"预览里能按、运行起来不能按"。
    ///
    /// 为什么是<b>单例</b>（App 里注册）
    /// ---------
    /// "此刻是谁登录着"是全局唯一的一份状态。两份实例的表现是：状态栏显示已登录、
    /// 而权限判定用的是另一份（未登录），于是"登录了却还是按不动"。
    ///
    /// 线程：只在 UI 线程上读写（登录窗口、状态栏、空闲计时器都在 UI 线程），故不加锁。
    /// 这与 <see cref="ScadaRuntime"/> 的线程口径一致。
    /// </summary>
    public sealed class ScadaAccessPolicy : BindableBase, IScadaAccessPolicy
    {
        /// <summary>
        /// 默认空闲多久自动登出。10 分钟是工业 HMI 的常见取值：
        /// 短于它，操作员去拧一个阀门回来就得重登；长于它，"人走了机器还开着管理员权限"。
        ///
        /// 这个值现在只是<b>兜底</b>——真值来自软件级配置（见 <see cref="IdleTimeout"/>），
        /// 常量从 <c>AppConfigModel.DefaultIdleTimeoutMinutes</c> 取，保证"配置默认值"
        /// 与"没有配置时的兜底值"永远是同一个数。
        /// </summary>
        public static readonly TimeSpan DefaultIdleTimeout =
            TimeSpan.FromMinutes(AppConfigModel.DefaultIdleTimeoutMinutes);

        /// <summary>
        /// 空闲检查的节拍。它只是"多久查一次到没到时间"，不是超时精度本身——
        /// 精度由 <see cref="IdleTimeout"/> 决定，这里取 15 秒是为了让"最多晚 15 秒登出"
        /// 这个误差可接受，同时不为此每秒唤醒一次 UI 线程。
        /// </summary>
        private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(15);

        /// <summary>
        /// 空闲计时器。构造时建、只跑在登录期间（<see cref="Login"/> 起、<see cref="Logout"/> 停）。
        ///
        /// 为什么不用 <c>System.Timers.Timer</c>：超时登出要改绑定属性（状态栏跟着变），
        /// 而绑定只能从 UI 线程改。用 <see cref="DispatcherTimer"/> 就不必自己切线程，
        /// 也就不会写出"忘了切线程"这种只在超时那一刻才复现的 bug。
        /// </summary>
        private readonly DispatcherTimer _idleTimer;

        /// <summary>此刻登录者的名字；<c>null</c> = 没人登录（未登录 ≠ 零权限，见 <see cref="IScadaAccessPolicy"/>）</summary>
        private string? _userName;

        /// <summary>登录者的角色。<b>只在 <see cref="_userName"/> 非 null 时有意义</b></summary>
        private ScadaRole _role = ScadaRole.Operator;

        /// <summary>最近一次"有人在动"的时刻（UTC）。空闲判定只看它与现在的差值</summary>
        private DateTime _lastActivityUtc;

        /// <summary>
        /// 超时值的<b>配置读取口</b>（宿主注入：读软件级配置里的分钟数）。
        ///
        /// 为什么是"每次现读"而不是"构造时读一次存下来"
        /// ---------
        /// 现场改完超时不该重启软件，也不该等下次登录才生效。把读取口交给这里，
        /// <see cref="OnIdleTick"/> 每一拍都拿当前配置，于是"改完立刻生效"是<b>结构上保证</b>的，
        /// 而不是靠"记得在某处把新值推给我"——后者漏一次就变成"界面改了、实际没改"，
        /// 而这种失效不报错、只在超时那一刻才看得出来。
        ///
        /// 为 null（断言、单机调试直接 new）时回落到 <see cref="_idleTimeoutOverride"/> /
        /// <see cref="DefaultIdleTimeout"/>。
        /// </summary>
        private readonly Func<TimeSpan>? _idleTimeoutProvider;

        /// <summary>手工设定的超时（无配置读取口时生效）；<c>null</c> = 还没设过</summary>
        private TimeSpan? _idleTimeoutOverride;

        /// <param name="idleTimeoutProvider">
        /// 空闲超时的配置读取口；传 <c>null</c> = 用 <see cref="DefaultIdleTimeout"/>
        /// （可用 <see cref="IdleTimeout"/> 的 setter 覆盖）。宿主应当注入它，
        /// 让「系统 → 系统参数设置」改完立刻生效。
        /// </param>
        public ScadaAccessPolicy(Func<TimeSpan>? idleTimeoutProvider = null)
        {
            _idleTimeoutProvider = idleTimeoutProvider;

            _idleTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = IdleCheckInterval,
            };
            _idleTimer.Tick += OnIdleTick;
        }

        /// <summary>
        /// 空闲多久自动登出。<see cref="TimeSpan.Zero"/> 或负值 = <b>不自动登出</b>
        /// （调试、无人值守的工程会这么用）。改这个值不必重启计时器——每次 Tick 现读。
        ///
        /// 取值优先级：配置读取口（有就<b>一律以它为准</b>）→ setter 设过的值 → 默认值。
        /// 注意：注入了读取口之后，setter 写进去的值<b>不会</b>被读到——配置是唯一真相，
        /// 两处都能改只会让"界面上明明改了"变成说不清的事。
        /// </summary>
        public TimeSpan IdleTimeout
        {
            get => _idleTimeoutProvider?.Invoke() ?? _idleTimeoutOverride ?? DefaultIdleTimeout;
            set => _idleTimeoutOverride = value;
        }

        /// <summary>
        /// 因为空闲超时被自动登出（参数是被登出的用户名）。
        /// 界面接它是为了说一句"已因超时自动登出"——<b>不吭声地退出登录</b>会让操作员
        /// 以为软件出了故障，然后反复重登、反复被踢。
        /// </summary>
        public event Action<string>? AutoLoggedOut;

        /// <inheritdoc/>
        public ScadaRole CurrentRole => IsLoggedIn ? _role : ScadaRole.Operator;

        /// <inheritdoc/>
        public bool IsLoggedIn => _userName != null;

        /// <inheritdoc/>
        public string CurrentUserName => _userName ?? "未登录";

        /// <summary>
        /// 状态栏那一行要显示的文字："张三（工程师）" / "未登录（操作员）"。
        ///
        /// 为什么把两个信息合在一处显示而不是并排两个控件：现场看一眼就要知道
        /// "现在是谁、他能干什么"，分成两栏反而要在两个地方各扫一眼。
        /// 未登录也把"（操作员）"写出来，是为了让操作员明白<b>不是软件坏了，是权限就这么多</b>。
        /// </summary>
        public string LoginStateText => $"{CurrentUserName}（{CurrentRole.DisplayName()}）";

        /// <inheritdoc/>
        public bool CanOperate(ScadaRole? required, out string? reason)
            => CurrentRole.Allows(required, out reason);

        /// <summary>
        /// 让一个<b>已经通过验证</b>的身份进入会话（换用户直接再调一次即可）。
        ///
        /// 本方法<b>不验密码</b>：账号从哪儿来、密码怎么比，是账号存储的事
        /// （见 <c>ScadaUserStore</c>）。把验证塞进来，本类就得认识密码学与文件路径，
        /// 而它真正要守的只有一条不变式——<b>已登录 ⇒ 角色是合法值</b>
        /// （运行态权限判定依赖它，非法角色进来会让 <see cref="ScadaRoleExtensions.Allows"/>
        /// 把每一次操作都拒掉，现场表现是"登录之后反而什么都按不动"）。
        /// </summary>
        /// <param name="userName">显示名（审计与状态栏都用它）</param>
        /// <param name="role">已验证的角色，必须是 <see cref="ScadaRoleExtensions.IsDefined"/> 的</param>
        /// <exception cref="ArgumentException">用户名为空白</exception>
        /// <exception cref="ArgumentOutOfRangeException">角色是非法值</exception>
        public void Login(string userName, ScadaRole role)
        {
            if (string.IsNullOrWhiteSpace(userName))
                throw new ArgumentException("登录名不能为空", nameof(userName));

            if (!role.IsDefined())
                throw new ArgumentOutOfRangeException(nameof(role), role, "登录角色必须是已定义的角色值");

            _userName = userName.Trim();
            _role = role;

            // 登录这一刻就算一次活动：否则"登录后一直没动"会立刻撞上超时。
            _lastActivityUtc = DateTime.UtcNow;
            _idleTimer.Start();

            RaiseLoginStateChanged();
        }

        /// <summary>
        /// 退出登录，回到"未登录 = 操作员"。可重复调用（超时踢出与手动登出都会走到这里）。
        /// </summary>
        public void Logout()
        {
            if (!IsLoggedIn)
                return;

            _userName = null;
            _role = ScadaRole.Operator;
            _idleTimer.Stop();

            RaiseLoginStateChanged();
        }

        /// <summary>
        /// 上报一次"有人在动"（鼠标、键盘、触摸都算），把空闲计时重新拨到 0。
        ///
        /// 由界面在<b>全局输入</b>上调用一次即可（见 App 启动时的 <c>PreProcessInput</c> 钩子）：
        /// 让每个窗口各自上报，等于每加一个窗口就要记得加一次，漏一个的表现就是
        /// "在那个窗口里操作也会被踢下线"。未登录时调用无害（只是白写一个时间戳）。
        /// </summary>
        public void Touch() => _lastActivityUtc = DateTime.UtcNow;

        /// <summary>
        /// 空闲检查的一拍。到点就登出并广播 <see cref="AutoLoggedOut"/>。
        ///
        /// 先 <see cref="Logout"/> 再抛事件，顺序不能反：订阅方（提示气泡、审计）读到的
        /// <see cref="CurrentUserName"/> 必须已经是"未登录"——反过来的话，那句"已因超时自动登出"
        /// 后面跟的署名会是刚被踢掉的那个人，审计文件上就成了"张三把张三踢了"。
        /// </summary>
        private void OnIdleTick(object? sender, EventArgs e)
        {
            if (!IsLoggedIn)
            {
                // 兜底：正常路径走不到（Logout 已经停了计时器），但"计时器还跑着而人已经走了"
                // 是那种会一直空转、还很难查的脏状态，顺手收掉。
                _idleTimer.Stop();
                return;
            }

            var timeout = IdleTimeout;
            if (timeout <= TimeSpan.Zero)
                return;   // 配成"不自动登出"

            if (DateTime.UtcNow - _lastActivityUtc < timeout)
                return;

            var who = _userName!;
            Logout();
            AutoLoggedOut?.Invoke(who);
        }

        /// <summary>
        /// 三个公开属性一起变，所以逐个显式通知，而不是传 null 让 WPF 全量刷新：
        /// 全量刷新会把界面上所有绑定都重算一遍，而这里要动的只有状态栏那几处。
        /// </summary>
        private void RaiseLoginStateChanged()
        {
            RaisePropertyChanged(nameof(IsLoggedIn));
            RaisePropertyChanged(nameof(CurrentUserName));
            RaisePropertyChanged(nameof(CurrentRole));
            RaisePropertyChanged(nameof(LoginStateText));
        }
    }
}
