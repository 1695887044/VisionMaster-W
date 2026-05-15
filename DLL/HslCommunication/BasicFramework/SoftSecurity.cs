using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HslCommunication.BasicFramework
{
	/// <summary>
	/// 字符串加密解密相关的自定义类
	/// </summary>
	public static class SoftSecurity
	{
		/// <summary>
		/// 加密数据，采用DES对称加密的方式
		/// </summary>
		/// <param name="pToEncrypt">待加密的数据</param>
		/// <returns>加密后的数据</returns>
		internal static string MD5Encrypt(string pToEncrypt)
		{
			return MD5Encrypt(pToEncrypt, "zxcvBNMM");
		}

		/// <summary>
		/// 加密数据，采用DES对称加密的方式
		/// </summary>
		/// <param name="pToEncrypt">待加密的数据</param>
		/// <param name="Password">密钥，长度为8，英文或数字</param>
		/// <returns>加密后的数据</returns>
		public static string MD5Encrypt(string pToEncrypt, string Password)
		{
			DESCryptoServiceProvider dESCryptoServiceProvider = new DESCryptoServiceProvider();
			byte[] bytes = Encoding.Default.GetBytes(pToEncrypt);
			dESCryptoServiceProvider.Key = Encoding.ASCII.GetBytes(Password);
			dESCryptoServiceProvider.IV = Encoding.ASCII.GetBytes(Password);
			MemoryStream memoryStream = new MemoryStream();
			CryptoStream cryptoStream = new CryptoStream(memoryStream, dESCryptoServiceProvider.CreateEncryptor(), CryptoStreamMode.Write);
			cryptoStream.Write(bytes, 0, bytes.Length);
			cryptoStream.FlushFinalBlock();
			StringBuilder stringBuilder = new StringBuilder();
			byte[] array = memoryStream.ToArray();
			foreach (byte b in array)
			{
				stringBuilder.AppendFormat("{0:X2}", b);
			}
			stringBuilder.ToString();
			return stringBuilder.ToString();
		}

		/// <summary>
		/// 解密过程，使用的是DES对称的加密
		/// </summary>
		/// <param name="pToDecrypt">等待解密的字符</param>
		/// <returns>返回原密码，如果解密失败，返回‘解密失败’</returns>
		internal static string MD5Decrypt(string pToDecrypt)
		{
			return MD5Decrypt(pToDecrypt, "zxcvBNMM");
		}

		/// <summary>
		/// 解密过程，使用的是DES对称的加密
		/// </summary>
		/// <param name="pToDecrypt">等待解密的字符</param>
		/// <param name="password">密钥，长度为8，英文或数字</param>
		/// <returns>返回原密码，如果解密失败，返回‘解密失败’</returns>
		public static string MD5Decrypt(string pToDecrypt, string password)
		{
			if (pToDecrypt == "")
			{
				return pToDecrypt;
			}
			DESCryptoServiceProvider dESCryptoServiceProvider = new DESCryptoServiceProvider();
			byte[] array = new byte[pToDecrypt.Length / 2];
			for (int i = 0; i < pToDecrypt.Length / 2; i++)
			{
				int num = Convert.ToInt32(pToDecrypt.Substring(i * 2, 2), 16);
				array[i] = (byte)num;
			}
			dESCryptoServiceProvider.Key = Encoding.ASCII.GetBytes(password);
			dESCryptoServiceProvider.IV = Encoding.ASCII.GetBytes(password);
			MemoryStream memoryStream = new MemoryStream();
			CryptoStream cryptoStream = new CryptoStream(memoryStream, dESCryptoServiceProvider.CreateDecryptor(), CryptoStreamMode.Write);
			cryptoStream.Write(array, 0, array.Length);
			cryptoStream.FlushFinalBlock();
			cryptoStream.Dispose();
			return Encoding.Default.GetString(memoryStream.ToArray());
		}
	}
}
