using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 账号清单里的一行（给界面看的<b>脱敏视图</b>）：只有名字和角色。
    ///
    /// 盐与哈希<b>刻意不在这上面</b>——界面拿不到，就不会有人图省事把它们绑到某个
    /// "调试用"的 TextBox 上，也就不会出现在截图、日志、导出的表格里。
    /// 密码材料只有一个出口：<see cref="ScadaUserStore.TryValidate"/>。
    /// </summary>
    /// <param name="Name">登录名</param>
    /// <param name="Role">该账号的角色</param>
    public sealed record ScadaUserInfo(string Name, ScadaRole Role);

    /// <summary>
    /// 账号存储（S12）：<b>"谁能登录、登录后是几档角色"的唯一一份数据</b>。
    ///
    /// 它在整个权限体系里的位置
    /// ---------
    /// <see cref="ScadaAccessPolicy"/> 管"此刻谁登录着"，但它<b>不验密码</b>（见其类注释）。
    /// 本类就是它让出去的那一半：把"用户名 + 密码"换成"一个已经通过验证的
    /// <see cref="ScadaRole"/>"，交给 <see cref="ScadaAccessPolicy.Login"/> 去开会话。
    /// 两个类合起来是一条单向链：<c>ScadaUserStore（验身份） → ScadaAccessPolicy（记会话）</c>，
    /// 谁也不认识谁的另一半。验证失败时角色根本没被生产出来，也就不存在
    /// "先登录再补验"这种能被绕过的中间态。
    ///
    /// 为什么<b>不存明文密码</b>，也不存"可逆加密"
    /// ---------
    /// 现场机器的程序目录往往是共享盘或者谁都能拷走的 U 盘。存明文 = 一份文件泄漏全部账号；
    /// 可逆加密（AES 之类）只是把钥匙也放进同一台机器，等于没加密。
    /// 这里用 <b>PBKDF2-SHA256</b>：单向、带随机盐、迭代十万次——
    /// 拿到文件的人只能对每个账号逐个暴力试，且同一个密码在不同账号上算出来的哈希也不同
    /// （盐不同），没法用一张彩虹表通杀。
    ///
    /// 文件长什么样
    /// ---------
    /// <c>ScadaUsers.json</c>，与 <c>AppConfig.json</c> 同级（程序目录），
    /// 结构为 <c>[{ Name, Salt, Hash, Iterations, Role }]</c>。
    /// 写盘走<b>原子写</b>（临时文件 → 替换），断电只会丢这一次改动，不会留下半个文件——
    /// 账号文件读不出来，现场就没人能登录了。
    ///
    /// 出厂账号
    /// ---------
    /// 文件不存在（首次运行）或读坏时，内存里给一个内置管理员
    /// <see cref="DefaultAdminName"/> / <see cref="DefaultAdminPassword"/>。
    /// <b>这是刻意的</b>：没有它，一台全新的机器谁也登不进去，权限功能等于锁死了自己。
    /// 代价是"删掉文件就能用默认密码进"——但能写程序目录的人本来就能直接替换这个文件，
    /// 并没有多出一条攻击路径；而"现场进不去、只能重装软件"是实打实的停线事故。
    /// 登录成功后用 <see cref="IsUsingDefaultPassword"/> 提示改密，把这个窗口尽快关掉。
    ///
    /// 线程：<b>桌面（UI 线程）与 Web（后台线程）会同时用</b>，所以内部按"两把锁"分工：
    /// <list type="bullet">
    /// <item><see cref="_gate"/> 只护内存清单——锁内只做"复制一条记录 / 改一条记录 / 序列化快照"，
    /// 都是微秒级的纯内存操作；<b>PBKDF2（十万次）与文件 I/O 一律在锁外</b>，
    /// 否则一次 Web 登录会把 UI 线程上的用户管理页卡住几十毫秒。</item>
    /// <item><see cref="_writeGate"/> 把"改内存 → 序列化 → 写盘"整段串起来：
    /// 两个写操作（桌面点保存 + 后台并发改密）既不能交错写同一个 <c>.tmp</c>，
    /// 也不能出现"后改的被先改的覆盖"。读路径（<see cref="TryValidate"/> /
    /// <see cref="Users"/> / <see cref="IsUsingDefaultPassword"/>）<b>不碰</b>它。</item>
    /// </list>
    /// 锁序永远是 <see cref="_writeGate"/> → <see cref="_gate"/>，反向没有任何路径，故不会死锁。
    /// 审计落盘（<see cref="ScadaAuditWriter.Append"/> 是同步文件 I/O）与署名提供者
    /// 一律在锁外调用——后者摸的是桌面会话状态，锁内调它等于把会话线程拖进账号锁。
    /// <para>
    /// <b>为什么不把内部记录带出锁</b>：<see cref="ScadaUserRecord"/> 是可变的，把引用交出去，
    /// 就可能在"取到盐之后、比对之前"被另一个线程改掉，盐与哈希不再是一对儿。
    /// 所以出锁的永远是 <see cref="UserSnapshot"/>（不可变值）。
    /// </para>
    ///
    /// 为什么<b>改账号这件事也要落审计</b>
    /// ---------
    /// "谁在什么时候把谁的密码改成了什么"是权限体系里唯一真正致命的一问：提权最省事的办法
    /// 不是破解哈希，而是让人把某个账号的角色改一下。只记在运行日志里不够——日志会滚动、
    /// 会被现场随手清掉（见 <see cref="ScadaAuditWriter"/> 的类注释），而这一问必须三个月后
    /// 还答得上来。所以五个写操作成功/失败时各落一条审计（<see cref="TryValidate"/> 不算：
    /// 它不改变任何数据）。
    ///
    /// 为什么落笔点选在<b>本类</b>、而不是两个弹窗的 ViewModel 里
    /// ---------
    /// 这五个方法是账号数据的<b>全部出口</b>，落在这里就不可能漏：将来多一条调用路径
    /// （运维用的命令行改密、恢复出厂管理员之类）也自动被记上。反过来，落在调用方就要求
    /// 每个调用方都记得记一笔——而审计最怕的恰恰不是记错，是<b>漏记而无人知道</b>：
    /// 文件看着完好，只是少了那几行。
    ///
    /// 署名（操作者）从哪来
    /// ---------
    /// 本类<b>不认识会话</b>（它连 <see cref="ScadaAccessPolicy"/> 都不引用，见上面的单向链），
    /// 所以署名由一个 <c>Func&lt;string?&gt;</c> 提供者注入——宿主把它接到"此刻登录的是谁"上。
    /// 取不到（没注入、或返回空）时由 <see cref="ScadaAuditEntry"/> 回落成"未登录"，
    /// 而"未登录"这三个字在审计里本身就是一条线索。
    /// </summary>
    public sealed class ScadaUserStore : IScadaUserAuthenticator, IScadaUserChangeNotifier
    {
        /// <inheritdoc/>
        public event Action<string, string>? UserChanged;

        /// <summary>出厂管理员的名字</summary>
        public const string DefaultAdminName = "admin";

        /// <summary>
        /// 出厂管理员的密码。故意做得短且好念——它只在"第一次开机"这段时间里存在，
        /// 登录后会被提示改掉（见 <see cref="IsUsingDefaultPassword"/>）。
        /// </summary>
        public const string DefaultAdminPassword = "admin123";

        /// <summary>
        /// 密码最短长度的默认值。四位数够拦住"空密码"和"手滑一个字符"，又不至于让现场骂人。
        /// 实际生效值见 <see cref="MinPasswordLength"/>（软件级配置可改）。
        /// </summary>
        public const int DefaultMinPasswordLength = AppConfigModel.DefaultMinPasswordLength;

        /// <summary>
        /// PBKDF2 迭代次数。十万次是"在一台工控机上验证一次约几十毫秒"的量级——
        /// 人手输密码完全无感，而暴力枚举的代价被放大十万倍。
        /// 这个值<b>会随记录一起落盘</b>，所以将来调大它不会让旧账号失效：
        /// 旧记录按它自己存的次数验，新记录按新值算。
        /// </summary>
        public const int DefaultIterations = 100_000;

        /// <summary>盐长度（字节）。16 字节 = 128 位，足够让"同一密码不同账号哈希不同"</summary>
        private const int SaltBytes = 16;

        /// <summary>派生哈希长度（字节）。32 字节 = 256 位，与 SHA256 输出同宽</summary>
        private const int HashBytes = 32;

        /// <summary>
        /// 账号操作落审计时的"事件"列。与运行态越权那条（"权限校验"）同为<b>类别名</b>：
        /// 这一列回答"从哪儿来的"，具体干了什么在"动作"列。
        /// </summary>
        private const string AuditEventText = "账号管理";

        private static readonly JsonSerializerSettings JsonOptions = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
        };

        /// <summary>名字比对一律忽略大小写：现场没人记得住当初建的是 Admin 还是 admin</summary>
        private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

        private readonly string _storePath;
        private readonly List<ScadaUserRecord> _users = new();

        /// <summary>
        /// 内存清单的锁。<b>只罩住"复制 / 改一条记录 / 序列化"</b>——PBKDF2 与文件 I/O
        /// 必须留在外面，否则 Web 登录的十万次哈希会把桌面用户管理页卡住。
        /// 所有对 <see cref="_users"/> 的读写都必须持它（<see cref="FindLocked"/> 的调用点即判据）。
        /// </summary>
        private readonly object _gate = new();

        /// <summary>
        /// 写事务的锁：<b>改内存 → 序列化 → 写盘</b>整段串行化。
        ///
        /// 只有 <see cref="_gate"/> 不够：序列化在锁内、写盘在锁外（不能拿磁盘 I/O 占着内存锁），
        /// 两个写操作就会同时写 <c>ScadaUsers.json.tmp</c>，且可能"先改的写在后"——
        /// 磁盘上留下旧状态、内存里是新状态，重启后又变回去。
        /// 读路径不碰它，所以 Web 登录照旧只等 <see cref="_gate"/>。
        /// </summary>
        private readonly object _writeGate = new();

        private readonly ScadaAuditWriter? _audit;
        private readonly Func<string?>? _actorProvider;
        private readonly Func<int>? _minPasswordLengthProvider;

        /// <param name="storePath">
        /// 账号文件路径。传 <c>null</c> 用程序目录下的 <c>ScadaUsers.json</c>；
        /// 断言工程与将来的"多套账号"会显式指定它。
        /// </param>
        /// <param name="audit">
        /// 操作审计落盘端。传 <c>null</c> = 不记审计（断言工程与无界面调用点走这条默认）；
        /// 宿主必须接上——"谁改了谁的密码"只留在运行日志里等于没记（理由见类注释）。
        /// </param>
        /// <param name="actorProvider">
        /// 署名来源：返回"此刻是谁在操作"。宿主接到登录会话上；返回空则由审计回落成"未登录"。
        /// </param>
        /// <param name="minPasswordLengthProvider">
        /// 密码最小长度的读取口（软件级配置）。传 <c>null</c> = 用
        /// <see cref="DefaultMinPasswordLength"/>——断言与单机调试走这条默认。
        /// 每次校验现读，所以改完不需要重启软件。
        /// </param>
        public ScadaUserStore(string? storePath = null,
                              ScadaAuditWriter? audit = null,
                              Func<string?>? actorProvider = null,
                              Func<int>? minPasswordLengthProvider = null)
        {
            _storePath = storePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScadaUsers.json");
            _audit = audit;
            _actorProvider = actorProvider;
            _minPasswordLengthProvider = minPasswordLengthProvider;
            Load();
        }

        /// <summary>
        /// <b>只给并发断言用</b>的暂停点：在 <see cref="TryValidate"/> / <see cref="ResetPassword"/>
        /// 复制完快照、开始算 PBKDF2 之前调用一次。
        ///
        /// 为什么需要它：这两处的"提交前复核"只在"PBKDF2 那几十毫秒里别人动了账号"时才生效，
        /// 靠自然时序撞这个窗口只能碰运气——撞不上时断言是绿的，但那不是"没有被并发问题影响"，
        /// 只是"没测到"（见 AGENTS.md 里"跳过型断言必须能被反证"）。
        /// 断言用它在窗口正中间插一脚（暂停 → 改角色 / 删号 / 改密 → 放行），把"偶发"变成"必然"。
        ///
        /// <b>生产路径上它恒为 <c>null</c></b>（没有默认实现、宿主也不赋值），
        /// 所以 <c>?.Invoke()</c> 只是一次空判断；实现里不许有任何副作用。
        /// 调用点<b>只许有这两处</b>（都在"快照已取、PBKDF2 未算"的同一位置），别往别的方法里复制。
        /// </summary>
        internal Action? AfterSnapshotForTest { get; set; }

        /// <summary>
        /// 生效中的密码最短长度：现读配置，<b>0 或负数按默认值处理</b>。
        ///
        /// 为什么负数/0 不当作"允许空密码"：这条配置的语义是"至少几位"，
        /// 把它配成 0 就成了一条悄悄关掉校验的后门——那不是现场会想要的东西。
        /// </summary>
        public int MinPasswordLength
        {
            get
            {
                var configured = _minPasswordLengthProvider?.Invoke() ?? DefaultMinPasswordLength;
                return configured > 0 ? configured : DefaultMinPasswordLength;
            }
        }

        /// <summary>账号文件的实际路径（界面上"账号文件在哪"要能查到）</summary>
        public string StorePath => _storePath;

        /// <summary>
        /// 上次 <see cref="Load"/> 的失败原因；成功为 <c>null</c>。
        ///
        /// 为什么不直接抛：账号文件读不出来时软件仍要能启动（回落到出厂账号），
        /// 但"用的是出厂账号"这件事必须能被告知——否则现场会以为自己在用自己的账号登录。
        /// </summary>
        public string? LastLoadError { get; private set; }

        /// <summary>当前账号清单（脱敏视图，按名字排序，界面直接绑）</summary>
        public IReadOnlyList<ScadaUserInfo> Users
        {
            get
            {
                // 锁内只复制"名字 + 角色"这两个值：不把内部 List（以及可变的记录对象）交出去，
                // 排序与投影在锁外做——界面刷新不该占着登录校验要用的锁。
                List<(string Name, ScadaRole Role)> snapshot;
                lock (_gate)
                {
                    snapshot = new List<(string, ScadaRole)>(_users.Count);
                    foreach (var user in _users) snapshot.Add((user.Name, user.Role));
                }

                return snapshot
                    .OrderBy(u => u.Name, NameComparer)
                    .Select(u => new ScadaUserInfo(u.Name, u.Role))
                    .ToArray();
            }
        }

        /// <summary>
        /// <b>唯一的密码验证出口</b>：用户名 + 密码 → 角色。
        ///
        /// 成功时返回 true 并给出角色，调用方随后应把它交给
        /// <see cref="ScadaAccessPolicy.Login"/> 开会话——本类<b>不碰会话</b>，
        /// 所以"验过了但没登录"不会有任何残留状态。
        ///
        /// 失败原因刻意<b>不区分"没有这个账号"与"密码不对"</b>：两者合并成一句话。
        /// 这是登录界面的通行做法，也让"试探哪些账号存在"这件事在界面上无从下手。
        /// 但"角色值非法"必须单独报——那不是用户输错了，是文件被改坏或者版本不匹配，
        /// 照着"密码不正确"去反复重输，永远也解决不了。
        ///
        /// <b>线程</b>：这是 <see cref="IScadaUserAuthenticator"/> 的实现，Web 登录会在
        /// <b>后台线程</b>上并发调用它，而桌面登录/用户管理仍在 UI 线程。
        /// 所以锁内只复制"名字 + 盐 + 哈希 + 迭代次数 + 角色"，PBKDF2 与定长比较全在锁外——
        /// 一次校验几十毫秒，锁内做等于把另一条线程一起按住。
        ///
        /// <b>PBKDF2 之后必须复核一次</b>（第二次进 <see cref="_gate"/>，只做三次值比较）：
        /// 那几十毫秒里账号可能被改密 / 降权 / 删除，只认进入时的快照就会让"密码正确"
        /// 替"账号还是那个账号、角色还是那个角色"作证。复核失败按原因分别报
        /// "密码已被他人修改" / "账号权限已变化，请重新登录" / "账号已被删除"。
        /// </summary>
        public bool TryValidate(string? userName, string? password, out ScadaRole role, out string? error)
        {
            role = ScadaRole.Operator;

            var name = (userName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                error = "请输入用户名";
                return false;
            }

            if (string.IsNullOrEmpty(password))
            {
                error = "请输入密码";
                return false;
            }

            // 锁内只做一次线性查找 + 值复制（微秒级），出锁之后这份快照就是只读的。
            UserSnapshot snapshot;
            lock (_gate)
            {
                var record = FindLocked(name);
                if (record == null)
                {
                    error = "用户名或密码不正确";
                    return false;
                }

                snapshot = UserSnapshot.From(record);
            }

            // 断言专用暂停点（生产路径上恒为 null）：让"快照已取、PBKDF2 未算"这个窗口可以被
            // 稳定地插一脚，否则下面那段复核只能靠运气撞上（撞不上时断言是绿的，但那只是"没测到"）。
            AfterSnapshotForTest?.Invoke();

            // 十万次 PBKDF2 在锁外：并发登录各算各的，谁也不挡谁。
            if (!Verify(password, snapshot))
            {
                error = "用户名或密码不正确";
                return false;
            }

            // 密码对了之后<b>重新核对一次</b>：PBKDF2 要跑几十毫秒，这期间桌面用户管理完全可能
            // 把这个账号改密 / 降权 / 删掉。只用进入时的那份快照，"密码正确"就会替"账号还是那个账号"
            // 作证——登录以旧角色通过（提权/降权被绕过），甚至一个已经删掉的账号照样登进去。
            // 复核放在锁内只做三次值比较（微秒级），PBKDF2 仍然在锁外。
            ScadaRole currentRole;
            lock (_gate)
            {
                var record = FindLocked(name);
                if (record == null)
                {
                    error = "账号已被删除";
                    return false;
                }

                if (!string.Equals(record.Hash, snapshot.Hash, StringComparison.Ordinal))
                {
                    error = "密码已被他人修改";
                    return false;
                }

                if (record.Role != snapshot.Role)
                {
                    error = "账号权限已变化，请重新登录";
                    return false;
                }

                currentRole = record.Role;
            }

            // 密码已经对了，才轮到检查角色：反过来的话，一个非法角色值会以
            // "登录失败"的形式表现出来，而真正的原因（文件里的角色数字不认识）被吞掉了。
            if (!currentRole.IsDefined())
            {
                error = $"账号「{snapshot.Name}」的角色值非法（{(int)currentRole}），请用管理员账号修正账号文件";
                return false;
            }

            role = currentRole;
            error = null;
            return true;
        }

        /// <summary>
        /// 这个账号的密码是不是还停在出厂值。登录成功后拿它提示"请尽快改密"——
        /// 出厂密码是公开的，机器一旦入网，它就不再是一道门。
        ///
        /// <b>不做缓存</b>：每次现取记录、现算哈希。加缓存就要在"改密 / 重置 / 重载"三处失效，
        /// 漏一处就会对着一个已经改过的密码继续提示"还在用出厂密码"（反过来更糟：
        /// 改回出厂密码后不提示）。十万次哈希换一个不会错，值。
        /// </summary>
        public bool IsUsingDefaultPassword(string? userName)
        {
            var name = (userName ?? string.Empty).Trim();
            if (!NameComparer.Equals(name, DefaultAdminName)) return false;

            // 与 TryValidate 同一口径：锁内复制、锁外算。
            UserSnapshot snapshot;
            lock (_gate)
            {
                var record = FindLocked(name);
                if (record == null) return false;
                snapshot = UserSnapshot.From(record);
            }

            return Verify(DefaultAdminPassword, snapshot);
        }

        /// <summary>
        /// 改密码。<b>必须报旧密码</b>——否则任何一个登录着的人都能改别人的密码，
        /// 而"管理员给自己提权"与"操作员把管理员密码改掉"是同一件事的两面。
        /// 管理员要重置别人密码的场景属于用户管理，不该混进这一条路径。
        /// </summary>
        public bool TryChangePassword(string? userName, string? currentPassword,
                                      string? newPassword, out string? error)
        {
            var name = Normalize(userName);
            var ok = ChangePassword(name, currentPassword, newPassword, out error, out var detail);
            Audit("修改密码", name, ok, detail, error);
            if (ok) NotifyUserChanged(name, "密码已修改");
            return ok;
        }

        /// <inheritdoc cref="TryChangePassword"/>
        private bool ChangePassword(string name, string? currentPassword,
                                    string? newPassword, out string? error, out string? detail)
        {
            detail = null;

            // ① 锁内只复制：盐 / 哈希 / 迭代次数 / 角色出锁之后就是只读的。
            UserSnapshot snapshot;
            lock (_gate)
            {
                var record = FindLocked(name);
                if (record == null)
                {
                    error = "用户名或密码不正确";
                    return false;
                }

                snapshot = UserSnapshot.From(record);
            }

            // ② 旧密码的 PBKDF2 在锁外（十万次）。
            if (!Verify(currentPassword ?? string.Empty, snapshot))
            {
                error = "用户名或密码不正确";
                return false;
            }

            if (!CheckNewPassword(newPassword, out error)) return false;

            // ③ 新密码的盐与哈希同样在锁外算。
            var (salt, hash) = HashPassword(newPassword!);

            // ④ 进写事务提交：先确认"刚才验过的那份哈希"还在。
            //    两个并发改密可能都验过了旧密码（各自的快照一样），谁先提交谁算数，
            //    后到的必须被拦下——否则它是拿"旧密码正确"去覆盖别人的新密码。
            lock (_writeGate)
            {
                string json;
                lock (_gate)
                {
                    var record = FindLocked(name);
                    if (record == null)
                    {
                        error = $"账号「{name}」不存在";
                        return false;
                    }

                    if (!string.Equals(record.Hash, snapshot.Hash, StringComparison.Ordinal))
                    {
                        error = "密码已被他人修改";
                        return false;
                    }

                    ApplyPassword(record, salt, hash);
                    json = JsonConvert.SerializeObject(_users, JsonOptions);
                }

                var ok = Persist(json, out error);
                if (ok) detail = "密码已修改";
                return ok;
            }
        }

        /// <summary>
        /// 重置密码（<b>管理员路径</b>）。<b>不校验旧密码</b>——这条正是它与
        /// <see cref="TryChangePassword"/> 的分界：忘了密码的人，恰恰是想不起旧密码的那一个。
        ///
        /// 因此调用方必须已经把"你是不是管理员"问过了。本类<b>不认识"谁在调用"</b>，
        /// 与 <see cref="TryAddUser"/> / <see cref="TryRemoveUser"/> 同一条口径：
        /// 这里守的是数据不变式（账号存在、新密码合规则），身份的把关在「用户管理」弹窗里。
        /// （唯一例外是审计的<b>署名</b>：那个由宿主注入，见类注释。）
        ///
        /// 两条路径<b>不合并</b>成一个带 <c>bool checkOld</c> 的方法：合并之后，
        /// "到底校验没校验旧密码"变成运行时才知道的事，静态读代码看不出来，
        /// 而这是一条安全属性，必须一眼可见。
        ///
        /// <b>"不校验旧密码"不等于"不比对任何东西"</b>：提交前仍要比对进入时的哈希
        /// （<see cref="ChangePassword"/> 的同一道 CAS）。管理员重置与用户自助改密会并发，
        /// 少了这道比对，更早发起、更晚提交的重置会把用户刚设的新密码覆盖掉，
        /// 而两条审计都记"成功"——现场两个人各以为知道自己现在的密码。
        /// </summary>
        public bool TryResetPassword(string? userName, string? newPassword, out string? error)
        {
            var name = Normalize(userName);
            var ok = ResetPassword(name, newPassword, out error, out var detail);
            Audit("重置密码", name, ok, detail, error);
            if (ok) NotifyUserChanged(name, "密码已重置");
            return ok;
        }

        /// <inheritdoc cref="TryResetPassword"/>
        private bool ResetPassword(string name, string? newPassword, out string? error, out string? detail)
        {
            detail = null;

            // ① 锁内只复制：既确认账号在（不存在的账号不该白算十万次哈希），
            //    也把"进入时的哈希"带出去，当提交前那道比对的锚点。
            UserSnapshot snapshot;
            lock (_gate)
            {
                var record = FindLocked(name);
                if (record == null)
                {
                    error = $"账号「{name}」不存在";
                    return false;
                }

                snapshot = UserSnapshot.From(record);
            }

            if (!CheckNewPassword(newPassword, out error)) return false;

            // 断言专用暂停点（生产路径上恒为 null）：UC17 要验"删号落在重置的快照与提交之间"，
            // 那个窗口只有一次 PBKDF2 那么宽（几十毫秒），靠 Sleep 撞不稳（Windows 计时器粒度
            // 本身就 ~15ms，实测会让重置先提交）。暂停在这里，删号才能确定地先进写事务。
            AfterSnapshotForTest?.Invoke();

            var (salt, hash) = HashPassword(newPassword!);

            lock (_writeGate)
            {
                string json;
                lock (_gate)
                {
                    // 两步之间账号可能被删了（桌面用户管理页）：报同一句话，不把 null 带出去。
                    var record = FindLocked(name);
                    if (record == null)
                    {
                        error = $"账号「{name}」不存在";
                        return false;
                    }

                    // 管理员路径<b>也要</b>做这道比对（与 <see cref="ChangePassword"/> 同一口径）：
                    // 重置与自助改密是同一条时间轴上的两个写者——用户刚改完密码、管理员更早发起
                    // 更晚提交的重置若直接覆盖，两条审计都会记"成功"，而现场只有一个人知道自己
                    // 现在的密码是哪一个（另一个人的新密码静默失效）。
                    if (!string.Equals(record.Hash, snapshot.Hash, StringComparison.Ordinal))
                    {
                        error = "密码已被他人修改";
                        return false;
                    }

                    ApplyPassword(record, salt, hash);
                    json = JsonConvert.SerializeObject(_users, JsonOptions);
                }

                var ok = Persist(json, out error);
                if (ok) detail = "密码已重置（管理员路径，未校验旧密码）";
                return ok;
            }
        }

        /// <summary>
        /// 新建账号。只有管理员该走到这里（这一条由界面把关，见登录框的"用户管理"页）。
        /// </summary>
        public bool TryAddUser(string? userName, string? password, ScadaRole role, out string? error)
        {
            var name = Normalize(userName);
            var ok = AddUser(name, password, role, out error, out var detail);
            Audit("创建账号", name, ok, detail, error);
            return ok;
        }

        /// <inheritdoc cref="TryAddUser"/>
        private bool AddUser(string name, string? password, ScadaRole role,
                             out string? error, out string? detail)
        {
            detail = null;

            if (name.Length == 0)
            {
                error = "用户名不能为空";
                return false;
            }

            if (name.Length > 32)
            {
                error = "用户名不能超过 32 个字符";
                return false;
            }

            // 先看一眼重名：保留"重名优先于密码长度"的既有报错顺序。
            lock (_gate)
            {
                if (FindLocked(name) != null)
                {
                    error = $"账号「{name}」已存在";
                    return false;
                }
            }

            if (!role.IsDefined())
            {
                error = "新账号的角色必须是操作员、工程师或管理员";
                return false;
            }

            if (!CheckNewPassword(password, out error)) return false;

            // 盐与哈希在锁外算（十万次 PBKDF2）。
            var (salt, hash) = HashPassword(password!);

            lock (_writeGate)
            {
                string json;
                lock (_gate)
                {
                    // 重新查重：两次加锁之间世界可能变过（另一次 AddUser / 一次 Load），
                    // 进锁这一刻的结果才是最终事实。
                    if (FindLocked(name) != null)
                    {
                        error = $"账号「{name}」已存在";
                        return false;
                    }

                    _users.Add(new ScadaUserRecord
                    {
                        Name = name,
                        Salt = salt,
                        Hash = hash,
                        Iterations = DefaultIterations,
                        Role = role,
                    });
                    json = JsonConvert.SerializeObject(_users, JsonOptions);
                }

                var ok = Persist(json, out error);

                // 审计里写"建了谁、给了几档权限"：事后再看"这个工程师账号是谁什么时候建的"，
                // 光有名字答不上来最要紧的那一半。
                if (ok) detail = $"角色：{role.DisplayName()}";
                return ok;
            }
        }

        /// <summary>
        /// 删除账号。<b>至少留一个管理员</b>——删掉最后一个管理员，等于把用户管理这条路
        /// 从软件里彻底抹掉，只能删账号文件重来（而那会把所有账号一起清掉）。
        /// 这条不变式属于数据本身，所以守在这里而不是界面上。
        /// </summary>
        public bool TryRemoveUser(string? userName, out string? error)
        {
            var name = Normalize(userName);
            var ok = RemoveUser(name, out error, out var detail);
            Audit("删除账号", name, ok, detail, error);
            if (ok) NotifyUserChanged(name, "账号已删除");
            return ok;
        }

        /// <inheritdoc cref="TryRemoveUser"/>
        private bool RemoveUser(string name, out string? error, out string? detail)
        {
            detail = null;

            lock (_writeGate)
            {
                ScadaRole formerRole;
                string json;

                lock (_gate)
                {
                    var record = FindLocked(name);

                    if (record == null)
                    {
                        error = $"账号「{name}」不存在";
                        return false;
                    }

                    if (record.Role == ScadaRole.Administrator && CountAdministratorsLocked() <= 1)
                    {
                        error = "至少要保留一个管理员账号，否则以后没人能再管理账号";
                        return false;
                    }

                    // 先记下角色：记录马上要从清单里摘掉，摘掉之后"他原来是什么身份"就查不出来了，
                    // 而这恰恰是删号审计里最该留下的一格。
                    formerRole = record.Role;

                    _users.Remove(record);
                    json = JsonConvert.SerializeObject(_users, JsonOptions);
                }

                var ok = Persist(json, out error);
                if (ok) detail = $"原角色：{formerRole.DisplayName()}";
                return ok;
            }
        }

        /// <summary>
        /// 改角色。同样守"至少留一个管理员"：把最后一个管理员降成工程师，
        /// 与删掉他造成的后果完全一样。
        /// </summary>
        public bool TrySetRole(string? userName, ScadaRole role, out string? error)
        {
            var name = Normalize(userName);
            var ok = SetRole(name, role, out error, out var detail);
            Audit("修改角色", name, ok, detail, error);

            // "角色未变"是成功但空操作（SetRole 不写盘），不能因此把用户踢下线。
            // 真正的变更 detail 形如「角色：工程师 → 管理员」。
            if (ok && detail != null && detail.StartsWith("角色：", StringComparison.Ordinal))
                NotifyUserChanged(name, "角色已变更");

            return ok;
        }

        /// <inheritdoc cref="TrySetRole"/>
        private bool SetRole(string name, ScadaRole role, out string? error, out string? detail)
        {
            detail = null;

            lock (_writeGate)
            {
                string json;
                ScadaRole oldRole;

                lock (_gate)
                {
                    var record = FindLocked(name);

                    if (record == null)
                    {
                        error = $"账号「{name}」不存在";
                        return false;
                    }

                    if (!role.IsDefined())
                    {
                        error = "角色必须是操作员、工程师或管理员";
                        return false;
                    }

                    if (record.Role == role)
                    {
                        error = null;
                        detail = $"角色未变（仍是{role.DisplayName()}），未写盘";
                        return true;   // 值没变是空操作：不写盘、不产生一次无意义的文件替换
                    }

                    if (record.Role == ScadaRole.Administrator && CountAdministratorsLocked() <= 1)
                    {
                        error = "至少要保留一个管理员账号，否则以后没人能再管理账号";
                        return false;
                    }

                    oldRole = record.Role;
                    record.Role = role;
                    json = JsonConvert.SerializeObject(_users, JsonOptions);
                }

                var ok = Persist(json, out error);
                if (ok) detail = $"角色：{oldRole.DisplayName()} → {role.DisplayName()}";
                return ok;
            }
        }

        /// <summary>用户名归一化：各处入口都先过这一手，审计里的"对象"才不会带着尾随空格</summary>
        private static string Normalize(string? userName) => (userName ?? string.Empty).Trim();

        /// <summary>
        /// 通知订阅方"这个账号的旧会话必须失效"。
        ///
        /// <b>必须在写事务的锁外调用</b>：订阅方（Web 模块）会去拿会话表/连接表的锁，
        /// 在 <c>_writeGate</c> 里回调等于把两套锁的锁序交给订阅方决定。
        /// 处理器抛异常只记日志，绝不回滚已经成功的账号写操作。
        /// </summary>
        private void NotifyUserChanged(string userName, string reason)
        {
            var handler = UserChanged;
            if (handler == null) return;

            try
            {
                handler(userName, reason);
            }
            catch (Exception ex)
            {
                // 账号已经写成功；通知失败只影响"旧会话没被立刻踢掉"，不能反过来让操作失败。
                System.Diagnostics.Debug.WriteLine(
                    $"[ScadaUserStore] 账号变更通知失败（{userName}）：{ex.GetType().Name}：{ex.Message}");
            }
        }

        /// <summary>
        /// 给一次账号写操作落一条审计。<b>绝不抛、绝不影响返回值</b>——与
        /// "审计写不成不能让操作失败"同一条口径（见 <see cref="ScadaAuditWriter"/> 的类注释）。
        /// 没接审计（<c>_audit == null</c>）时整件事不存在。
        ///
        /// 失败也记：<b>"谁试着改了谁的密码但没成"与"改成了"是两条不同的线索</b>，
        /// 只记成功的那一半，事后问"他到底动过没有"答案会是"没有"。
        /// 失败那一格的说明直接用 <paramref name="error"/>——那是给操作员看的原因，
        /// 与界面上弹的那句话同源，审计与现场口述才对得上。
        ///
        /// <b>只在锁外调</b>：<see cref="ScadaAuditWriter.Append"/> 是同步文件 I/O，
        /// <see cref="_actorProvider"/> 摸的是桌面会话状态——两件事都不该占着账号锁。
        /// </summary>
        private void Audit(string actionText, string subject, bool ok, string? detail, string? error)
            => _audit?.Append(new ScadaAuditEntry(
                _actorProvider?.Invoke(),
                subject,
                AuditEventText,
                actionText,
                ok ? ScadaAuditOutcome.Success : ScadaAuditOutcome.Failed,
                ok ? detail : error));

        /// <summary>
        /// 从磁盘重新加载（外部改了账号文件后手动刷新用）。加载失败会回落到出厂账号，
        /// 原因留在 <see cref="LastLoadError"/> 里。
        ///
        /// <b>与写操作排队</b>：刷新若和一次"改密码"并行，两边会互相盖（一边写盘、一边把内存
        /// 换回磁盘上的旧样子）。所以整段走 <see cref="_writeGate"/>——它不挡登录校验，
        /// 登录只等 <see cref="_gate"/>，而磁盘 I/O 全程不在 <see cref="_gate"/> 里。
        /// </summary>
        public void Load()
        {
            lock (_writeGate)
            {
                LoadCore();
            }
        }

        /// <summary>
        /// <see cref="Load"/> 的本体。<b>调用方必须已持 <see cref="_writeGate"/></b>
        /// （<see cref="Persist"/> 的失败回滚直接调它，不再抢同一把锁）。
        ///
        /// 读文件、反序列化、剔除残缺记录、算出厂账号的哈希全在 <see cref="_gate"/> 之外；
        /// 进锁只做一次"清空 + 填入"，快照式替换——中途登录的线程要么看到旧清单、要么看到新清单，
        /// 不会看到"清了一半"的空表。
        /// </summary>
        private void LoadCore()
        {
            string? loadError = null;
            var accepted = new List<ScadaUserRecord>();

            try
            {
                if (File.Exists(_storePath))
                {
                    var json = File.ReadAllText(_storePath, Encoding.UTF8);
                    var loaded = JsonConvert.DeserializeObject<List<ScadaUserRecord>>(json, JsonOptions);

                    if (loaded == null || loaded.Count == 0)
                    {
                        loadError = "账号文件里没有任何账号，已回落到出厂账号";
                    }
                    else
                    {
                        // 逐条剔除残缺记录（手工编辑、写到一半、别的版本写的字段）：
                        // 留着它们不会让谁多一分权限，但会让"改密码"在这条记录上永远失败，
                        // 而界面又列得出来，现场只会觉得是软件坏了。
                        var seen = new HashSet<string>(NameComparer);
                        foreach (var record in loaded)
                        {
                            if (string.IsNullOrWhiteSpace(record.Name)) continue;
                            if (string.IsNullOrWhiteSpace(record.Salt)) continue;
                            if (string.IsNullOrWhiteSpace(record.Hash)) continue;
                            if (record.Iterations <= 0) continue;
                            if (!seen.Add(record.Name)) continue;   // 文件里有重名（忽略大小写）：只认第一条

                            accepted.Add(record);
                        }

                        if (accepted.Count == 0)
                        {
                            loadError = "账号文件里没有一条可用记录，已回落到出厂账号";
                        }
                    }
                }
                // 首次运行（文件不存在）：不落盘，先在内存里给出厂账号。真建了账号（或改了密码）
                // 才写文件，免得"只是打开过一次软件"就在程序目录里留下一个账号文件。
                // 这里 accepted 为空、loadError 也为空 → 下面补出厂账号，但不算错误。
            }
            catch (Exception ex)
            {
                loadError = $"账号文件读取失败，已回落到出厂账号。原因：{ex.Message}";
                accepted.Clear();
            }

            // 出厂账号的 PBKDF2 也在锁外算：回滚发生在写盘失败之后，别让那一刻的登录线程陪着等。
            var seed = accepted.Count == 0 ? CreateDefaultAdminRecord() : null;

            lock (_gate)
            {
                _users.Clear();
                if (seed != null) _users.Add(seed);
                else _users.AddRange(accepted);

                LastLoadError = loadError;
            }
        }

        /// <summary>
        /// 写盘（原子写：先写临时文件再替换，断电不丢旧账号）。
        ///
        /// <b>锁内只序列化</b>（微秒级内存操作），文件 I/O 在锁外——但整段包在
        /// <see cref="_writeGate"/> 里：两个写操作同时写 <c>.tmp</c> 会互相踩，
        /// 且可能"先改的写在后"（磁盘留旧状态、内存是新状态）。
        /// </summary>
        public bool Save(out string? error)
        {
            lock (_writeGate)
            {
                string json;
                lock (_gate)
                {
                    json = JsonConvert.SerializeObject(_users, JsonOptions);
                }

                return WriteSnapshot(json, out error);
            }
        }

        /// <summary>
        /// 把<b>已经序列化好的</b>快照原子写盘。<b>调用方必须已持 <see cref="_writeGate"/></b>。
        /// 写的是调用方在锁内取的那份 json，不是"此刻的 _users"——两者之间可能又改过，
        /// 而那份改动会有它自己的一次写盘排在后面。
        /// </summary>
        private bool WriteSnapshot(string json, out string? error)
        {
            try
            {
                var dir = Path.GetDirectoryName(_storePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var tmp = _storePath + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);

                if (File.Exists(_storePath)) File.Replace(tmp, _storePath, null);
                else File.Move(tmp, _storePath);

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = $"账号文件保存失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 写盘并处理失败：写不进去就<b>把内存态退回磁盘上的样子</b>。
        ///
        /// 不退回的后果是内存里"改成功了"、磁盘上没改，界面上一切正常，
        /// 直到重启软件——那时现场会说"我明明改过密码，怎么又变回来了"，
        /// 而这时候最早的那次真实报错早就从屏幕上消失了。
        ///
        /// <b>调用方必须已持 <see cref="_writeGate"/></b>：回滚与写操作是同一段事务，
        /// 中间插进一次别人的写盘就回滚到别人身上了。这里直接调 <see cref="LoadCore"/>
        /// （而不是 <see cref="Load"/>），免得在已持锁的线程上再抢同一把锁。
        /// </summary>
        private bool Persist(string json, out string? error)
        {
            if (WriteSnapshot(json, out error)) return true;

            LoadCore();
            return false;
        }

        /// <summary>按名字取记录（忽略大小写）；没有则 <c>null</c>。<b>调用方必须持 <see cref="_gate"/></b></summary>
        private ScadaUserRecord? FindLocked(string name)
            => name.Length == 0 ? null : _users.FirstOrDefault(u => NameComparer.Equals(u.Name, name));

        /// <summary>管理员人数。<b>调用方必须持 <see cref="_gate"/></b>（与"至少留一个管理员"的判定在同一临界区里）</summary>
        private int CountAdministratorsLocked() => _users.Count(u => u.Role == ScadaRole.Administrator);

        /// <summary>
        /// 给一份新密码算盐与哈希（PBKDF2）。<b>纯函数、不碰任何共享状态</b>——
        /// 调用方在锁外调它，算完再进锁把结果写进记录。
        /// </summary>
        private static (string Salt, string Hash) HashPassword(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            return (Convert.ToBase64String(salt),
                    Convert.ToBase64String(Derive(password, salt, DefaultIterations)));
        }

        /// <summary>把算好的盐与哈希写进记录（<see cref="ScadaUserRecord.Iterations"/> 同步到当前值）。<b>调用方必须持 <see cref="_gate"/></b></summary>
        private static void ApplyPassword(ScadaUserRecord record, string salt, string hash)
        {
            record.Salt = salt;
            record.Hash = hash;
            record.Iterations = DefaultIterations;
        }

        /// <summary>
        /// 验密码。用<b>记录里存的迭代次数</b>算，而不是当前默认值——
        /// 否则把 <see cref="DefaultIterations"/> 调大一次，全厂旧账号会集体登不进去。
        ///
        /// 只吃 <see cref="UserSnapshot"/>（锁内复制出来的不可变值），<b>绝不接记录引用</b>：
        /// 这个方法在锁外跑几十毫秒，期间别人可能把记录改了。
        /// </summary>
        private static bool Verify(string password, in UserSnapshot snapshot)
            => Verify(password, snapshot.Salt, snapshot.Hash, snapshot.Iterations);

        /// <summary>验密码的本体（见上面的重载）。参数是"盐 + 哈希 + 迭代次数"这三个不可变值。</summary>
        private static bool Verify(string password, string saltBase64, string hashBase64, int iterations)
        {
            try
            {
                var salt = Convert.FromBase64String(saltBase64);
                var expected = Convert.FromBase64String(hashBase64);
                var actual = Derive(password, salt, iterations);

                // 定长比较：普通的逐字节比较会在第一个不同的字节上提前返回，
                // 泄漏"猜对了几个字节"。本场景没有网络攻击者，但这条写法的代价是零，
                // 没有理由留一个将来会被静态扫描揪出来的点。
                return CryptographicOperations.FixedTimeEquals(expected, actual);
            }
            catch
            {
                // 盐/哈希不是合法 Base64 = 记录坏了。当成"验不过"而不是抛异常：
                // 一条坏记录不该让整个登录框打不开。
                return false;
            }
        }

        /// <summary>PBKDF2-SHA256 派生。参数只有一份实现，改算法时不可能漏改某一处</summary>
        private static byte[] Derive(string password, byte[] salt, int iterations)
            => Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);

        /// <summary>
        /// 新密码的通用校验（改密与新建账号共用，两条路径的规则不会分头演化）。
        /// 实例方法（不是 static）：最小长度现在是软件级配置，要按实例的读取口现取。
        /// </summary>
        private bool CheckNewPassword(string? password, out string? error)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                error = "密码不能为空";
                return false;
            }

            if (password.Length < MinPasswordLength)
            {
                error = $"密码至少 {MinPasswordLength} 个字符";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 造一条出厂管理员记录。<b>不碰共享状态、不持锁</b>（PBKDF2 十万次），
        /// 调用方算完再进锁 <c>Add</c>。
        /// </summary>
        private static ScadaUserRecord CreateDefaultAdminRecord()
        {
            var (salt, hash) = HashPassword(DefaultAdminPassword);
            return new ScadaUserRecord
            {
                Name = DefaultAdminName,
                Salt = salt,
                Hash = hash,
                Iterations = DefaultIterations,
                Role = ScadaRole.Administrator,
            };
        }

        /// <summary>
        /// 锁内取出的一份<b>不可变</b>账号快照：出锁之后 PBKDF2 只认它，不再碰 <see cref="_users"/>。
        ///
        /// 为什么是值类型 + 只带验证要的那几格：<see cref="ScadaUserRecord"/> 是可变的，
        /// 把引用带出锁等于"验到一半别人改了盐"——哈希与盐就不再是一对儿，
        /// 表现是"密码明明对却登不进去"，而且只在并发下偶发。
        /// </summary>
        private readonly record struct UserSnapshot(string Name, string Salt, string Hash, int Iterations, ScadaRole Role)
        {
            public static UserSnapshot From(ScadaUserRecord record)
                => new(record.Name, record.Salt, record.Hash, record.Iterations, record.Role);
        }

        /// <summary>
        /// 落盘用的记录。<b>私有嵌套</b>是刻意的：盐和哈希只在 <see cref="ScadaUserStore"/> 里流转，
        /// 外面拿到的永远是脱敏的 <see cref="ScadaUserInfo"/>。
        /// Newtonsoft 能序列化私有嵌套类型（走反射访问公开成员），不影响文件格式。
        /// </summary>
        private sealed class ScadaUserRecord
        {
            public string Name { get; set; } = string.Empty;

            /// <summary>随机盐（Base64）</summary>
            public string Salt { get; set; } = string.Empty;

            /// <summary>PBKDF2 派生出的哈希（Base64）。<b>不是</b>密码本身，也不可反推</summary>
            public string Hash { get; set; } = string.Empty;

            /// <summary>算这份哈希时用的迭代次数</summary>
            public int Iterations { get; set; }

            /// <summary>角色。存数值——见 <see cref="ScadaRole"/> 的"数值一旦发布不许改"</summary>
            public ScadaRole Role { get; set; }
        }
    }
}
