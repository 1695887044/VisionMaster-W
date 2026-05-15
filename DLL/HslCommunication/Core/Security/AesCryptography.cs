using System;
using System.Security.Cryptography;
using System.Text;

namespace HslCommunication.Core.Security
{
	/// <summary>
	/// 实例化一个AES加密解密的对象，默认 <see cref="F:System.Security.Cryptography.CipherMode.ECB" /> 模式的对象
	/// </summary>
	public class AesCryptography : ICryptography
	{
		private ICryptoTransform encryptTransform;

		private ICryptoTransform decryptTransform;

		private RijndaelManaged rijndael;

		private string key;

		/// <inheritdoc cref="P:HslCommunication.Core.Security.ICryptography.Key" />
		public string Key => key;

		/// <summary>
		/// 使用指定的密钥实例化一个AES加密解密的对象，密钥由32位数字或字母组成，例如 12345678123456781234567812345678
		/// </summary>
		/// <param name="key">密钥</param>
		/// <param name="mode">加密的模式，默认为 <see cref="F:System.Security.Cryptography.CipherMode.ECB" /></param>
		public AesCryptography(string key, CipherMode mode = CipherMode.ECB)
		{
			this.key = key;
			rijndael = new RijndaelManaged
			{
				Key = Encoding.UTF8.GetBytes(key),
				Mode = mode,
				Padding = PaddingMode.PKCS7
			};
			encryptTransform = rijndael.CreateEncryptor();
			decryptTransform = rijndael.CreateDecryptor();
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Security.ICryptography.Encrypt(System.Byte[])" />
		public byte[] Encrypt(byte[] data)
		{
			if (data == null)
			{
				return null;
			}
			return encryptTransform.TransformFinalBlock(data, 0, data.Length);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Security.ICryptography.Decrypt(System.Byte[])" />
		public byte[] Decrypt(byte[] data)
		{
			if (data == null)
			{
				return null;
			}
			return decryptTransform.TransformFinalBlock(data, 0, data.Length);
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Security.ICryptography.Encrypt(System.String)" />
		public string Encrypt(string data)
		{
			byte[] data2 = (string.IsNullOrEmpty(data) ? new byte[0] : Encoding.UTF8.GetBytes(data));
			return Convert.ToBase64String(Encrypt(data2));
		}

		/// <inheritdoc cref="M:HslCommunication.Core.Security.ICryptography.Decrypt(System.String)" />
		public string Decrypt(string data)
		{
			return Encoding.UTF8.GetString(Decrypt(Convert.FromBase64String(data)));
		}
	}
}
