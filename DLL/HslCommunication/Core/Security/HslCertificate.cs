using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HslCommunication.Core.Security
{
	/// <summary>
	/// 基于RSA加密模型的证书，支持自定义的颁发证书，以及校验证书合法性<br />
	/// Certificates based on RSA encryption model support custom issuance certificates and verify certificate legitimacy
	/// </summary>
	/// <remarks>
	/// 证书可以用于接口的权限认证，不需要修改接口源代码或是配置文件，颁发证书就可以修改用户的权限，而且只要保密好私钥，那么证书本身就无法伪造，具有极高的安全性，具体用法参考示例代码。<br />
	/// The certificate can be used for the permission authentication of the interface, no need to modify the interface source code or configuration file, 
	/// the issuance of the certificate can modify the user's permissions, and as long as the private key is kept secret, then the certificate itself cannot be forged, 
	/// with extremely high security, the specific usage refer to the sample code.
	/// </remarks>
	/// <example>
	/// 证书这部分的功能主要分为，制作证书，以及校验证书，至于为什么不使用<see cref="T:System.Security.Cryptography.X509Certificates.X509Certificate2" />证书的形式，
	/// 因为这种证书都是要授信机构颁发的，自己颁发的证书验签不了，所以在本库里提供一个用于自己颁发，自己验签的证书。<br />
	/// 假设我们有一些API接口需要使用证书来控制权限，有调用时间检验的，或是按接口名称校验的，或是按调用次数来校验的，接口见下面的代码。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\HslCertificateSample.cs" region="Example1" title="接口的权限控制示例" />
	/// 当然我们还可以自己颁发证书，注意，这时候的私钥就非常有用了，私钥丢了，就发不了证书了。如果要重新生成公私钥，那么之前发出去的证书都失效了。
	/// <code lang="cs" source="HslCommunication_Net45.Test\Documentation\Samples\Core\HslCertificateSample.cs" region="Example2" title="颁发证书的例子" />
	/// </example>
	public class HslCertificate
	{
		private RSACryptoServiceProvider privateRsa;

		private RSACryptoServiceProvider publicRsa;

		/// <summary>
		/// 证书的颁发者
		/// </summary>
		public string From { get; set; }

		/// <summary>
		/// 证书的持有者
		/// </summary>
		public string To { get; set; }

		/// <summary>
		/// 证书有效的起始时间
		/// </summary>
		public DateTime NotBefore { get; set; }

		/// <summary>
		/// 证书有效的截止时间
		/// </summary>
		public DateTime NotAfter { get; set; }

		/// <summary>
		/// 证书的公钥信息
		/// </summary>
		public byte[] PublicKey { get; set; }

		/// <summary>
		/// 发证日期
		/// </summary>
		public DateTime CreateTime { get; set; }

		/// <summary>
		/// 获取或设置当前证书的关键字，可以用来给证书做分类
		/// </summary>
		public string KeyWord { get; set; }

		/// <summary>
		/// 获取或设置当前证书的唯一编号信息
		/// </summary>
		public string UniqueID { get; set; }

		/// <summary>
		/// 有效小时数，小于等于0 表示无期限
		/// </summary>
		public int EffectiveHours { get; set; }

		/// <summary>
		/// 证书的其他描述信息
		/// </summary>
		public Dictionary<string, string> Descriptions { get; set; }

		/// <summary>
		/// 实例化一个默认的对象<br />
		/// Instantiate a default object
		/// </summary>
		public HslCertificate()
		{
			CreateTime = DateTime.Now;
		}

		/// <summary>
		/// 使用指定的公钥，私钥来实例化一个的对象<br />
		/// An object is instantiated using the specified public key and private key
		/// </summary>
		/// <param name="pubKey">公钥的对象</param>
		/// <param name="priKey">私钥的对象</param>
		public HslCertificate(RSACryptoServiceProvider pubKey, RSACryptoServiceProvider priKey)
			: this()
		{
			publicRsa = pubKey;
			privateRsa = priKey;
			PublicKey = RSAHelper.GetPublicKeyFromRSA(pubKey);
		}

		/// <summary>
		/// 使用指定的公钥，私钥来实例化一个的对象<br />
		/// An object is instantiated using the specified public key and private key
		/// </summary>
		/// <param name="pubKey">公钥的二进制数据</param>
		/// <param name="priKey">私钥的二进制数据</param>
		public HslCertificate(byte[] pubKey, byte[] priKey)
			: this()
		{
			if (pubKey != null)
			{
				publicRsa = RSAHelper.CreateRsaProviderFromPublicKey(pubKey);
			}
			if (priKey != null)
			{
				privateRsa = RSAHelper.CreateRsaProviderFromPrivateKey(priKey);
			}
			PublicKey = pubKey;
		}

		/// <summary>
		/// 从文件的二进制数据中加载相关的参数
		/// </summary>
		/// <param name="hslCertificate">证书信息</param>
		public void LoadFrom(byte[] hslCertificate)
		{
			int index = 4;
			PublicKey = ExtraBytes(hslCertificate, ref index);
			From = ExtraString(hslCertificate, ref index);
			To = ExtraString(hslCertificate, ref index);
			NotBefore = ExtraDateTime(hslCertificate, ref index);
			NotAfter = ExtraDateTime(hslCertificate, ref index);
			CreateTime = ExtraDateTime(hslCertificate, ref index);
			KeyWord = ExtraString(hslCertificate, ref index);
			UniqueID = ExtraString(hslCertificate, ref index);
			EffectiveHours = BitConverter.ToInt32(hslCertificate, index);
			index += 4;
			int num = ExtraShort(hslCertificate, ref index);
			Descriptions = new Dictionary<string, string>();
			for (int i = 0; i < num; i++)
			{
				string key = ExtraString(hslCertificate, ref index);
				string value = ExtraString(hslCertificate, ref index);
				Descriptions.Add(key, value);
			}
		}

		private void AddDateTime(MemoryStream ms, DateTime data)
		{
			byte[] bytes = BitConverter.GetBytes(data.Ticks);
			AddBytes(ms, bytes);
		}

		private void AddBytes(MemoryStream ms, ushort data)
		{
			byte[] bytes = BitConverter.GetBytes(data);
			ms.Write(bytes);
		}

		private void AddString(MemoryStream ms, string data)
		{
			byte[] data2 = (string.IsNullOrEmpty(data) ? null : Encoding.UTF8.GetBytes(data));
			AddBytes(ms, data2);
		}

		private void AddBytes(MemoryStream ms, byte[] data)
		{
			int num = ((data != null) ? data.Length : 0);
			ms.Write(BitConverter.GetBytes((short)num), 0, 2);
			if (data != null && num > 0)
			{
				ms.Write(data, 0, data.Length);
			}
		}

		private DateTime ExtraDateTime(byte[] buffer, ref int index)
		{
			byte[] value = ExtraBytes(buffer, ref index);
			return new DateTime(BitConverter.ToInt64(value, 0));
		}

		private ushort ExtraShort(byte[] buffer, ref int index)
		{
			ushort result = BitConverter.ToUInt16(buffer, index);
			index += 2;
			return result;
		}

		private byte[] ExtraBytes(byte[] buffer, ref int index)
		{
			int num = BitConverter.ToUInt16(buffer, index);
			index += 2;
			if (num > 0)
			{
				byte[] result = buffer.SelectMiddle(index, num);
				index += num;
				return result;
			}
			return new byte[0];
		}

		private string ExtraString(byte[] buffer, ref int index)
		{
			byte[] array = ExtraBytes(buffer, ref index);
			if (array == null || array.Length == 0)
			{
				return string.Empty;
			}
			return Encoding.UTF8.GetString(array);
		}

		/// <summary>
		/// 获取当前证书的原始字节信息，可以存储到文件中，必须提供私钥信息，否则无法进行签名的操作<br />
		/// Gets the raw byte information of the current certificate, which can be stored in a file, and the private key information must be provided, otherwise the signing operation cannot be performed
		/// </summary>
		/// <returns>原始字节数据</returns>
		public byte[] GetSaveBytes()
		{
			MemoryStream memoryStream = new MemoryStream();
			AddBytes(memoryStream, 0);
			AddBytes(memoryStream, 0);
			AddBytes(memoryStream, PublicKey);
			AddString(memoryStream, From);
			AddString(memoryStream, To);
			AddDateTime(memoryStream, NotBefore);
			AddDateTime(memoryStream, NotAfter);
			AddDateTime(memoryStream, CreateTime);
			AddString(memoryStream, KeyWord);
			AddString(memoryStream, UniqueID);
			memoryStream.Write(BitConverter.GetBytes(EffectiveHours));
			if (Descriptions == null)
			{
				AddBytes(memoryStream, 0);
			}
			else
			{
				AddBytes(memoryStream, (ushort)Descriptions.Count);
				foreach (KeyValuePair<string, string> description in Descriptions)
				{
					AddString(memoryStream, description.Key);
					AddString(memoryStream, description.Value);
				}
			}
			byte[] array = memoryStream.ToArray();
			byte[] data = privateRsa.SignData(array, 4, array.Length - 4, new SHA1CryptoServiceProvider());
			int value = array.Length - 4;
			AddBytes(memoryStream, data);
			array = memoryStream.ToArray();
			array[0] = BitConverter.GetBytes(value)[0];
			array[1] = BitConverter.GetBytes(value)[1];
			return array;
		}

		/// <summary>
		/// 使用给定的公钥，校验当前的证书是否合法的，如果公钥为 null，则直接校验证书本身是否合法。<br />
		/// Use the given public key to verify whether the current certificate is valid, and if the public key is null, directly verify whether the certificate itself is valid.
		/// </summary>
		/// <param name="publicKey">公钥信息，如果不为空，则校验公钥是否一致</param>
		/// <param name="hslCertificate">证书信息</param>
		/// <returns>是否合法</returns>
		public static bool VerifyCer(byte[] publicKey, byte[] hslCertificate)
		{
			if (hslCertificate == null)
			{
				return false;
			}
			int num = BitConverter.ToUInt16(hslCertificate, 4);
			if (publicKey != null)
			{
				if (publicKey.Length != num)
				{
					return false;
				}
				for (int i = 0; i < publicKey.Length; i++)
				{
					if (publicKey[i] != hslCertificate[i + 6])
					{
						return false;
					}
				}
			}
			int num2 = BitConverter.ToUInt16(hslCertificate, 0);
			int length = BitConverter.ToUInt16(hslCertificate, num2 + 4);
			RSACryptoServiceProvider rSACryptoServiceProvider = RSAHelper.CreateRsaProviderFromPublicKey(hslCertificate.SelectMiddle(6, num));
			return rSACryptoServiceProvider.VerifyData(hslCertificate.SelectMiddle(4, num2), new SHA1CryptoServiceProvider(), hslCertificate.SelectMiddle(num2 + 6, length));
		}

		/// <summary>
		/// 从证书的原始字节创建一个<see cref="T:HslCommunication.Core.Security.HslCertificate" />对象，方便浏览证书的基本信息。<br />
		/// Create a <see cref="T:HslCommunication.Core.Security.HslCertificate" /> object from the original bytes of the certificate to facilitate browsing the basic information of the certificate.
		/// </summary>
		/// <param name="hslCertificate">证书信息</param>
		/// <param name="pubKey">公钥对象</param>
		/// <param name="priKey">私钥对象</param>
		/// <returns>证书的可描述对象</returns>
		public static HslCertificate CreateFrom(byte[] hslCertificate, byte[] pubKey = null, byte[] priKey = null)
		{
			HslCertificate hslCertificate2 = new HslCertificate(pubKey, priKey);
			hslCertificate2.LoadFrom(hslCertificate);
			return hslCertificate2;
		}
	}
}
