using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// 加密辅助类
    /// 提供流程加密和解密功能
    /// </summary>
    public static class EncryptionHelper
    {
        private const int KeySize = 256;
        private const int BlockSize = 128;
        private const int Iterations = 1000;
        private const string DefaultSalt = "VisionMasterSalt2024";

        /// <summary>
        /// 生成随机密钥
        /// </summary>
        /// <returns>Base64编码的密钥</returns>
        public static string GenerateKey()
        {
            using var aes = Aes.Create();
            aes.KeySize = KeySize;
            aes.GenerateKey();
            return Convert.ToBase64String(aes.Key);
        }

        /// <summary>
        /// 加密字符串
        /// </summary>
        /// <param name="plainText">明文</param>
        /// <param name="key">Base64编码的密钥</param>
        /// <returns>加密后的Base64字符串</returns>
        public static string Encrypt(string plainText, string key)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            byte[] salt = Encoding.UTF8.GetBytes(DefaultSalt);
            byte[] keyBytes = Convert.FromBase64String(key);

            using var aes = Aes.Create();
            aes.KeySize = KeySize;
            aes.BlockSize = BlockSize;
            aes.Key = keyBytes;
            aes.IV = GenerateRandomIV();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

            using var msEncrypt = new MemoryStream();
            msEncrypt.Write(aes.IV, 0, aes.IV.Length);

            using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            {
                csEncrypt.Write(plainBytes, 0, plainBytes.Length);
                csEncrypt.FlushFinalBlock();
            }

            return Convert.ToBase64String(msEncrypt.ToArray());
        }

        /// <summary>
        /// 解密字符串
        /// </summary>
        /// <param name="encryptedText">加密的Base64字符串</param>
        /// <param name="key">Base64编码的密钥</param>
        /// <returns>解密后的明文</returns>
        public static string Decrypt(string encryptedText, string key)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return string.Empty;

            byte[] salt = Encoding.UTF8.GetBytes(DefaultSalt);
            byte[] keyBytes = Convert.FromBase64String(key);
            byte[] encryptedBytes = Convert.FromBase64String(encryptedText);

            using var aes = Aes.Create();
            aes.KeySize = KeySize;
            aes.BlockSize = BlockSize;
            aes.Key = keyBytes;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var msDecrypt = new MemoryStream(encryptedBytes);
            
            byte[] iv = new byte[aes.IV.Length];
            msDecrypt.Read(iv, 0, iv.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            
            using var msPlain = new MemoryStream();
            csDecrypt.CopyTo(msPlain);
            
            return Encoding.UTF8.GetString(msPlain.ToArray());
        }

        /// <summary>
        /// 生成随机初始化向量
        /// </summary>
        private static byte[] GenerateRandomIV()
        {
            using var aes = Aes.Create();
            aes.GenerateIV();
            return aes.IV;
        }

        /// <summary>
        /// 加密文件内容
        /// </summary>
        public static string EncryptFile(string filePath, string key)
        {
            string content = File.ReadAllText(filePath);
            return Encrypt(content, key);
        }

        /// <summary>
        /// 解密文件内容
        /// </summary>
        public static string DecryptFile(string encryptedContent, string key)
        {
            return Decrypt(encryptedContent, key);
        }
    }
}
