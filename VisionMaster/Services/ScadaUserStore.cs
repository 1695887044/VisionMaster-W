using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
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
    /// 线程：与 <see cref="ScadaAccessPolicy"/> 同口径，只在 UI 线程上读写（登录框、用户管理页），
    /// 故不加锁。真要挪到后台线程，得先把 <see cref="Save"/> 的原子写与内存态一起罩住
    /// （审计落盘端同样不加锁，见 <see cref="ScadaAuditWriter"/> 的线程注释——两者必须在同一条线上）。
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
    public sealed class ScadaUserStore
    {
        /// <summary>出厂管理员的名字</summary>
        public const string DefaultAdminName = "admin";

        /// <summary>
        /// 出厂管理员的密码。故意做得短且好念——它只在"第一次开机"这段时间里存在，
        /// 登录后会被提示改掉（见 <see cref="IsUsingDefaultPassword"/>）。
        /// </summary>
        public const string DefaultAdminPassword = "admin123";

        /// <summary>密码最短长度。四位数够拦住"空密码"和"手滑一个字符"，又不至于让现场骂人</summary>
        public const int MinPasswordLength = 4;

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
        private readonly ScadaAuditWriter? _audit;
        private readonly Func<string?>? _actorProvider;

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
        public ScadaUserStore(string? storePath = null,
                              ScadaAuditWriter? audit = null,
                              Func<string?>? actorProvider = null)
        {
            _storePath = storePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScadaUsers.json");
            _audit = audit;
            _actorProvider = actorProvider;
            Load();
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
            => _users
                .OrderBy(u => u.Name, NameComparer)
                .Select(u => new ScadaUserInfo(u.Name, u.Role))
                .ToArray();

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

            var record = Find(name);
            if (record == null || !Verify(password, record))
            {
                error = "用户名或密码不正确";
                return false;
            }

            // 密码已经对了，才轮到检查角色：反过来的话，一个非法角色值会以
            // "登录失败"的形式表现出来，而真正的原因（文件里的角色数字不认识）被吞掉了。
            if (!record.Role.IsDefined())
            {
                error = $"账号「{record.Name}」的角色值非法（{(int)record.Role}），请用管理员账号修正账号文件";
                return false;
            }

            role = record.Role;
            error = null;
            return true;
        }

        /// <summary>
        /// 这个账号的密码是不是还停在出厂值。登录成功后拿它提示"请尽快改密"——
        /// 出厂密码是公开的，机器一旦入网，它就不再是一道门。
        /// </summary>
        public bool IsUsingDefaultPassword(string? userName)
        {
            var name = (userName ?? string.Empty).Trim();
            if (!NameComparer.Equals(name, DefaultAdminName)) return false;

            var record = Find(name);
            return record != null && Verify(DefaultAdminPassword, record);
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
            return ok;
        }

        /// <inheritdoc cref="TryChangePassword"/>
        private bool ChangePassword(string name, string? currentPassword,
                                    string? newPassword, out string? error, out string? detail)
        {
            detail = null;

            var record = Find(name);
            if (record == null || !Verify(currentPassword ?? string.Empty, record))
            {
                error = "用户名或密码不正确";
                return false;
            }

            if (!CheckNewPassword(newPassword, out error)) return false;

            SetPassword(record, newPassword!);
            var ok = Persist(out error);
            if (ok) detail = "密码已修改";
            return ok;
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
        /// </summary>
        public bool TryResetPassword(string? userName, string? newPassword, out string? error)
        {
            var name = Normalize(userName);
            var ok = ResetPassword(name, newPassword, out error, out var detail);
            Audit("重置密码", name, ok, detail, error);
            return ok;
        }

        /// <inheritdoc cref="TryResetPassword"/>
        private bool ResetPassword(string name, string? newPassword, out string? error, out string? detail)
        {
            detail = null;

            var record = Find(name);

            if (record == null)
            {
                error = $"账号「{name}」不存在";
                return false;
            }

            if (!CheckNewPassword(newPassword, out error)) return false;

            SetPassword(record, newPassword!);
            var ok = Persist(out error);
            if (ok) detail = "密码已重置（管理员路径，未校验旧密码）";
            return ok;
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

            if (Find(name) != null)
            {
                error = $"账号「{name}」已存在";
                return false;
            }

            if (!role.IsDefined())
            {
                error = "新账号的角色必须是操作员、工程师或管理员";
                return false;
            }

            if (!CheckNewPassword(password, out error)) return false;

            var record = new ScadaUserRecord { Name = name, Role = role };
            SetPassword(record, password!);
            _users.Add(record);

            var ok = Persist(out error);

            // 审计里写"建了谁、给了几档权限"：事后再看"这个工程师账号是谁什么时候建的"，
            // 光有名字答不上来最要紧的那一半。
            if (ok) detail = $"角色：{role.DisplayName()}";
            return ok;
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
            return ok;
        }

        /// <inheritdoc cref="TryRemoveUser"/>
        private bool RemoveUser(string name, out string? error, out string? detail)
        {
            detail = null;

            var record = Find(name);

            if (record == null)
            {
                error = $"账号「{name}」不存在";
                return false;
            }

            if (record.Role == ScadaRole.Administrator && CountAdministrators() <= 1)
            {
                error = "至少要保留一个管理员账号，否则以后没人能再管理账号";
                return false;
            }

            // 先记下角色：记录马上要从清单里摘掉，摘掉之后"他原来是什么身份"就查不出来了，
            // 而这恰恰是删号审计里最该留下的一格。
            var formerRole = record.Role;

            _users.Remove(record);
            var ok = Persist(out error);
            if (ok) detail = $"原角色：{formerRole.DisplayName()}";
            return ok;
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
            return ok;
        }

        /// <inheritdoc cref="TrySetRole"/>
        private bool SetRole(string name, ScadaRole role, out string? error, out string? detail)
        {
            detail = null;

            var record = Find(name);

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

            if (record.Role == ScadaRole.Administrator && CountAdministrators() <= 1)
            {
                error = "至少要保留一个管理员账号，否则以后没人能再管理账号";
                return false;
            }

            var oldRole = record.Role;
            record.Role = role;
            var ok = Persist(out error);
            if (ok) detail = $"角色：{oldRole.DisplayName()} → {role.DisplayName()}";
            return ok;
        }

        /// <summary>用户名归一化：各处入口都先过这一手，审计里的"对象"才不会带着尾随空格</summary>
        private static string Normalize(string? userName) => (userName ?? string.Empty).Trim();

        /// <summary>
        /// 给一次账号写操作落一条审计。<b>绝不抛、绝不影响返回值</b>——与
        /// "审计写不成不能让操作失败"同一条口径（见 <see cref="ScadaAuditWriter"/> 的类注释）。
        /// 没接审计（<c>_audit == null</c>）时整件事不存在。
        ///
        /// 失败也记：<b>"谁试着改了谁的密码但没成"与"改成了"是两条不同的线索</b>，
        /// 只记成功的那一半，事后问"他到底动过没有"答案会是"没有"。
        /// 失败那一格的说明直接用 <paramref name="error"/>——那是给操作员看的原因，
        /// 与界面上弹的那句话同源，审计与现场口述才对得上。
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
        /// </summary>
        public void Load()
        {
            LastLoadError = null;
            _users.Clear();

            try
            {
                if (!File.Exists(_storePath))
                {
                    // 首次运行：不落盘，先在内存里给出厂账号。真建了账号（或改了密码）才写文件，
                    // 免得"只是打开过一次软件"就在程序目录里留下一个账号文件。
                    SeedDefaultAdmin();
                    return;
                }

                var json = File.ReadAllText(_storePath, Encoding.UTF8);
                var loaded = JsonConvert.DeserializeObject<List<ScadaUserRecord>>(json, JsonOptions);

                if (loaded == null || loaded.Count == 0)
                {
                    LastLoadError = "账号文件里没有任何账号，已回落到出厂账号";
                    SeedDefaultAdmin();
                    return;
                }

                // 逐条剔除残缺记录（手工编辑、写到一半、别的版本写的字段）：
                // 留着它们不会让谁多一分权限，但会让"改密码"在这条记录上永远失败，
                // 而界面又列得出来，现场只会觉得是软件坏了。
                foreach (var record in loaded)
                {
                    if (string.IsNullOrWhiteSpace(record.Name)) continue;
                    if (string.IsNullOrWhiteSpace(record.Salt)) continue;
                    if (string.IsNullOrWhiteSpace(record.Hash)) continue;
                    if (record.Iterations <= 0) continue;

                    if (Find(record.Name) != null) continue;   // 文件里有重名（忽略大小写）：只认第一条

                    _users.Add(record);
                }

                if (_users.Count == 0)
                {
                    LastLoadError = "账号文件里没有一条可用记录，已回落到出厂账号";
                    SeedDefaultAdmin();
                }
            }
            catch (Exception ex)
            {
                LastLoadError = $"账号文件读取失败，已回落到出厂账号。原因：{ex.Message}";
                _users.Clear();
                SeedDefaultAdmin();
            }
        }

        /// <summary>写盘（原子写：先写临时文件再替换，断电不丢旧账号）</summary>
        public bool Save(out string? error)
        {
            try
            {
                var dir = Path.GetDirectoryName(_storePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(_users, JsonOptions);
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
        /// </summary>
        private bool Persist(out string? error)
        {
            if (Save(out error)) return true;

            Load();
            return false;
        }

        /// <summary>按名字取记录（忽略大小写）；没有则 <c>null</c></summary>
        private ScadaUserRecord? Find(string name)
            => name.Length == 0 ? null : _users.FirstOrDefault(u => NameComparer.Equals(u.Name, name));

        private int CountAdministrators() => _users.Count(u => u.Role == ScadaRole.Administrator);

        /// <summary>给记录换一份新密码：新盐、新哈希，<see cref="ScadaUserRecord.Iterations"/> 同步到当前值</summary>
        private static void SetPassword(ScadaUserRecord record, string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            record.Salt = Convert.ToBase64String(salt);
            record.Hash = Convert.ToBase64String(Derive(password, salt, DefaultIterations));
            record.Iterations = DefaultIterations;
        }

        /// <summary>
        /// 验密码。用<b>记录里存的迭代次数</b>算，而不是当前默认值——
        /// 否则把 <see cref="DefaultIterations"/> 调大一次，全厂旧账号会集体登不进去。
        /// </summary>
        private static bool Verify(string password, ScadaUserRecord record)
        {
            try
            {
                var salt = Convert.FromBase64String(record.Salt);
                var expected = Convert.FromBase64String(record.Hash);
                var actual = Derive(password, salt, record.Iterations);

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

        /// <summary>新密码的通用校验（改密与新建账号共用，两条路径的规则不会分头演化）</summary>
        private static bool CheckNewPassword(string? password, out string? error)
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

        private void SeedDefaultAdmin()
        {
            var record = new ScadaUserRecord { Name = DefaultAdminName, Role = ScadaRole.Administrator };
            SetPassword(record, DefaultAdminPassword);
            _users.Add(record);
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
