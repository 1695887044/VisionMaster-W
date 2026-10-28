using System;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 一次登录凭据的<b>只读快照</b>：用户名 + 密码（<c>char[]</c>）。
    ///
    /// 为什么不是 <c>string</c>
    /// ---------
    /// <c>string</c> 一旦生成就无法擦除，只能等 GC；而这里存的是明文密码。
    /// 用 <c>char[]</c> 才能在 <see cref="Dispose"/> 里逐位清零，把"这段明文已经没人要了"
    /// 变成一条可执行的动作，而不是一句注释。密码从界面到这里的唯一一次字符串形态，
    /// 是用户输入框里那一份（见 <c>ScadaLoginViewModel</c>），此后不再新增。
    ///
    /// 谁负责释放
    /// ---------
    /// <see cref="ScadaSessionCredentialVault.Capture"/> 把所有权交给调用方；
    /// 调用方必须 <c>using</c>（方案切换协调器就是这么写的）。
    /// 快照一旦释放，<see cref="TryValidate"/> 会按空密码处理并失败——不会出现
    /// "释放之后还能拿去登录"的悬空凭据。
    /// </summary>
    public sealed class ScadaCredentialSnapshot : IDisposable
    {
        private char[]? _password;

        internal ScadaCredentialSnapshot(string userName, char[] password)
        {
            UserName = userName ?? string.Empty;
            _password = password ?? Array.Empty<char>();
        }

        /// <summary>登录名（账号本里的规范名，大小写以登录时校验通过的那条记录为准）。</summary>
        public string UserName { get; }

        /// <summary>密码是否已经为空（未设置 / 已释放）。</summary>
        public bool IsEmpty => _password is not { Length: > 0 };

        /// <summary>密码视图。只给同程序集（协调器）用；断言宿主经 InternalsVisibleTo 可见。</summary>
        internal ReadOnlySpan<char> Password
            => _password is null ? ReadOnlySpan<char>.Empty : _password.AsSpan();

        /// <summary>
        /// 拿这份凭据去目标账号本里验一次。
        /// <b>验的是目标方案自己的盐与哈希</b>——两个方案的盐不同，不能拿旧哈希直接比。
        /// </summary>
        public bool TryValidate(ScadaUserStore store, out ScadaUserInfo? user, out string? error)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            return store.TryValidate(UserName, Password, out user, out error);
        }

        /// <summary>清零并丢弃明文。可重复调用。</summary>
        public void Dispose()
        {
            var password = _password;
            _password = null;
            if (password != null) Array.Clear(password, 0, password.Length);
        }
    }

    /// <summary>
    /// 本次会话的登录凭据仓：<b>只在内存里</b>保留"刚才用哪个账号密码登录成功"。
    ///
    /// 它解决什么问题
    /// ---------
    /// 方案切换 = 会话边界（旧实现一律登出）。现场希望：<b>两个方案的账号密码一样时，
    /// 切方案不要把人踢下线</b>。账号本按方案隔离、每个账号的盐是随机的，
    /// 所以"密码一样"只能在切换那一刻拿明文去目标账号本重新验一次——
    /// 这要求登录成功时把凭据留到会话结束。
    ///
    /// 三条不可谈判的边界
    /// ---------
    ///   1. <b>绝不落盘</b>：没有文件、没有配置节、没有 <c>ToString</c>、不进日志/审计。
    ///      进程退出即消失；崩溃转储里出现明文的风险由"只在登录后持有、登出立刻清零"压到最小。
    ///   2. <b>任何登出都清零</b>：订阅 <see cref="ScadaAccessPolicy.LoggedOut"/>，
    ///      手动登出 / 空闲超时 / 方案切换 / 强制登出全部覆盖。
    ///      漏掉超时这一条，就会出现"人已经因超时被踢，切个方案又自己登回来"。
    ///   3. <b>切方案时先取走、再登出</b>：<see cref="Capture"/> 返回快照并<b>同时清空仓</b>；
    ///      协调器在调用 <see cref="ScadaAccessPolicy.Logout"/> 之前取走，之后用快照去新账号本重验。
    ///      顺序反了就拿不到凭据（登出已经把它清了）。
    ///
    /// 为什么不用"记住密码"式的持久化
    /// ---------
    /// 那等于在磁盘上放一份可解密/可重放的凭据，且跨重启有效——现场任何一个人拿到机器就能登录。
    /// 本仓的生命周期严格等于"这次登录会话"，这是需求"切方案保持登录"所需的最小权限。
    /// </summary>
    public sealed class ScadaSessionCredentialVault
    {
        private readonly object _gate = new();
        private string _userName = string.Empty;
        private char[]? _password;

        public ScadaSessionCredentialVault(ScadaAccessPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            // 任何登出都让凭据一起失效。订阅不摘：两者都是应用级单例，生命周期相同。
            policy.LoggedOut += _ => Clear();
        }

        /// <summary>仓里是否有一份可用的凭据（已登录且尚未被取走）。</summary>
        public bool HasCredential
        {
            get
            {
                lock (_gate)
                {
                    return _userName.Length > 0 && _password is { Length: > 0 };
                }
            }
        }

        /// <summary>
        /// 登录成功后记住本次凭据（覆盖上一份）。
        /// 用户名或密码为空时按"清空"处理——宁可下次切方案时重新登录，也不留半份凭据。
        /// </summary>
        public void Remember(string? userName, string? password)
        {
            var name = (userName ?? string.Empty).Trim();
            if (name.Length == 0 || string.IsNullOrEmpty(password))
            {
                Clear();
                return;
            }

            Remember(name, password!.AsSpan());
        }

        /// <summary>从快照回填（切方案重验成功后用，保证"连续切两次"仍然保持登录）。</summary>
        internal void Remember(string? userName, ReadOnlySpan<char> password)
        {
            var name = (userName ?? string.Empty).Trim();
            if (name.Length == 0 || password.IsEmpty)
            {
                Clear();
                return;
            }

            var copy = password.ToArray();
            lock (_gate)
            {
                ClearLocked();
                _userName = name;
                _password = copy;
            }
        }

        /// <summary>
        /// 取走当前凭据（返回副本并清空仓）。没有可用凭据时返回 <c>null</c>。
        /// 调用方必须 <c>using</c> 快照；验不过时不要回填，让会话按未登录处理。
        /// </summary>
        public ScadaCredentialSnapshot? Capture()
        {
            lock (_gate)
            {
                if (_userName.Length == 0 || _password is not { Length: > 0 })
                    return null;

                var snapshot = new ScadaCredentialSnapshot(_userName, (char[])_password.Clone());
                ClearLocked();
                return snapshot;
            }
        }

        /// <summary>清零并丢弃凭据。可重复调用（登出事件与协调器都会调）。</summary>
        public void Clear()
        {
            lock (_gate) ClearLocked();
        }

        private void ClearLocked()
        {
            _userName = string.Empty;
            var password = _password;
            _password = null;
            if (password != null) Array.Clear(password, 0, password.Length);
        }
    }
}
