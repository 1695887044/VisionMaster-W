using System;
using System.Security.Cryptography;
using System.Text;

namespace Core.Database
{
    /// <summary>
    /// 数据库密码的 DPAPI 封装（只用于 MySQL/MariaDB 的 SQL 认证）。
    ///
    /// 口径：
    ///   · 密文只存在宿主 `databases.json` 的 <c>PasswordCipher</c> 字段里，`.vms` 永不出现；
    ///   · 用 <see cref="DataProtectionScope.LocalMachine"/>：工业机通常以固定账号运行，
    ///     CurrentUser 会在"换服务账号/换登录用户"后解不开；代价是同机任何本地用户
    ///     只要能读安装目录就能解密——所以安装目录的 ACL 才是真正的边界，文档里写清；
    ///   · 附加 entropy 常量：避免同一台机器上别的程序拿同样的 DPAPI 调用直接解开。
    /// </summary>
    internal static class DbCredentialProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VisionMaster.Database.v1");

        public static string Protect(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;

            var bytes = Encoding.UTF8.GetBytes(plainText);
            var cipher = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(cipher);
        }

        public static string Unprotect(string? cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return string.Empty;

            try
            {
                var cipher = Convert.FromBase64String(cipherText);
                var bytes = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.LocalMachine);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "数据库密码无法解密（配置可能来自另一台机器或另一个 Windows 账号），"
                    + "请到「硬件管理 → 数据库」重新输入密码。", ex);
            }
        }
    }
}
