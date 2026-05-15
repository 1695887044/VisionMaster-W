using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using HslCommunication.BasicFramework;

namespace HslCommunication.Core.Security
{
	/// <summary>
	/// RSA加密解密算法的辅助方法，可以用PEM格式的密钥创建公钥，或是私钥对象，然后用来加解密操作。
	/// </summary>
	public class RSAHelper
	{
		private const string privateKeyHead = "-----BEGIN RSA PRIVATE KEY-----";

		private const string privateKeyEnd = "-----END RSA PRIVATE KEY-----";

		private const string publicKeyHead = "-----BEGIN PUBLIC KEY-----";

		private const string publicKeyEnd = "-----END PUBLIC KEY-----";

		private static readonly byte[] SeqOID = new byte[15]
		{
			48, 13, 6, 9, 42, 134, 72, 134, 247, 13,
			1, 1, 1, 5, 0
		};

		/// <summary>
		/// 使用 PEM 格式基于base64编码的私钥来创建一个 RSA 算法加密解密的对象，可以直接用于加密解密操作<br />
		/// Use the PEM format based on the base64-encoded private key to create an RSA algorithm encryption and decryption object, 
		/// which can be directly used for encryption and decryption operations
		/// </summary>
		/// <param name="privateKeyString">私钥</param>
		/// <returns>RSA 算法的加密解密对象</returns>
		public static RSACryptoServiceProvider CreateRsaProviderFromPrivateKey(string privateKeyString)
		{
			privateKeyString = privateKeyString.Trim();
			if (privateKeyString.StartsWith("-----BEGIN RSA PRIVATE KEY-----"))
			{
				privateKeyString = privateKeyString.Replace("-----BEGIN RSA PRIVATE KEY-----", string.Empty);
			}
			if (privateKeyString.EndsWith("-----END RSA PRIVATE KEY-----"))
			{
				privateKeyString = privateKeyString.Replace("-----END RSA PRIVATE KEY-----", string.Empty);
			}
			byte[] privateKey = Convert.FromBase64String(privateKeyString);
			return CreateRsaProviderFromPrivateKey(privateKey);
		}

		/// <summary>
		/// 使用原始的私钥数据（PEM格式）来创建一个 RSA 算法加密解密的对象，可以直接用于加密解密操作<br />
		/// Use the original private key data (PEM format) to create an RSA algorithm encryption and decryption object, 
		/// which can be directly used for encryption and decryption operations
		/// </summary>
		/// <param name="privateKey">原始的私钥数据</param>
		/// <returns>RSA 算法的加密解密对象</returns>
		public static RSACryptoServiceProvider CreateRsaProviderFromPrivateKey(byte[] privateKey)
		{
			RSACryptoServiceProvider rSACryptoServiceProvider = new RSACryptoServiceProvider();
			RSAParameters parameters = default(RSAParameters);
			using (BinaryReader binaryReader = new BinaryReader(new MemoryStream(privateKey)))
			{
				byte b = 0;
				ushort num = 0;
				switch (binaryReader.ReadUInt16())
				{
				case 33072:
					binaryReader.ReadByte();
					break;
				case 33328:
					binaryReader.ReadInt16();
					break;
				default:
					throw new Exception("Unexpected value read binr.ReadUInt16()");
				}
				num = binaryReader.ReadUInt16();
				if (num != 258)
				{
					throw new Exception("Unexpected version");
				}
				if (binaryReader.ReadByte() != 0)
				{
					throw new Exception("Unexpected value read binr.ReadByte()");
				}
				parameters.Modulus = binaryReader.ReadBytes(GetIntegerSize(binaryReader));
				parameters.Exponent = binaryReader.ReadBytes(GetIntegerSize(binaryReader));
				parameters.D = binaryReader.ReadBytes(GetIntegerSize(binaryReader));
				parameters.P = binaryReader.ReadBytes(GetIntegerSize(binaryReader));
				parameters.Q = binaryReader.ReadBytes(GetIntegerSize(binaryReader));
				parameters.DP = binaryReader.ReadBytes(GetIntegerSize(binaryReader));
				parameters.DQ = binaryReader.ReadBytes(GetIntegerSize(binaryReader));
				parameters.InverseQ = binaryReader.ReadBytes(GetIntegerSize(binaryReader));
			}
			rSACryptoServiceProvider.ImportParameters(parameters);
			return rSACryptoServiceProvider;
		}

		private static int GetIntegerSize(BinaryReader binr)
		{
			byte b = 0;
			byte b2 = 0;
			byte b3 = 0;
			int num = 0;
			b = binr.ReadByte();
			if (b != 2)
			{
				return 0;
			}
			b = binr.ReadByte();
			switch (b)
			{
			case 129:
				num = binr.ReadByte();
				break;
			case 130:
			{
				b3 = binr.ReadByte();
				b2 = binr.ReadByte();
				byte[] value = new byte[4] { b2, b3, 0, 0 };
				num = BitConverter.ToInt32(value, 0);
				break;
			}
			default:
				num = b;
				break;
			}
			while (binr.ReadByte() == 0)
			{
				num--;
			}
			binr.BaseStream.Seek(-1L, SeekOrigin.Current);
			return num;
		}

		private static byte[] PackKeyHead(byte[] content)
		{
			if (content.Length < 256)
			{
				byte[] array = SoftBasic.SpliceArray<byte>("30 81 00".ToHexBytes(), content);
				array[2] = BitConverter.GetBytes(array.Length - 3)[0];
				return array;
			}
			byte[] array2 = SoftBasic.SpliceArray<byte>("30 82 00 00".ToHexBytes(), content);
			array2[2] = BitConverter.GetBytes(array2.Length - 4)[1];
			array2[3] = BitConverter.GetBytes(array2.Length - 4)[0];
			return array2;
		}

		/// <summary>
		/// 从RSA的算法对象里，获取到PEM格式的原始私钥数据，如果需要存储，或是显示，只需要 Convert.ToBase64String 方法<br />
		/// Obtain the original private key data in PEM format from the RSA algorithm object. If you need to store or display it, 
		/// you only need the Convert.ToBase64String method
		/// </summary>
		/// <param name="rsa">RSA 算法的加密解密对象</param>
		/// <returns>原始的私钥数据</returns>
		public static byte[] GetPrivateKeyFromRSA(RSACryptoServiceProvider rsa)
		{
			RSAParameters rSAParameters = rsa.ExportParameters(includePrivateParameters: true);
			byte[] modulus = rSAParameters.Modulus;
			byte[] exponent = rSAParameters.Exponent;
			byte[] d = rSAParameters.D;
			byte[] p = rSAParameters.P;
			byte[] q = rSAParameters.Q;
			byte[] dP = rSAParameters.DP;
			byte[] dQ = rSAParameters.DQ;
			byte[] inverseQ = rSAParameters.InverseQ;
			MemoryStream memoryStream = new MemoryStream();
			memoryStream.Write(new byte[3] { 2, 1, 0 }, 0, 3);
			WriteByteStream(memoryStream, modulus);
			WriteByteStream(memoryStream, exponent);
			WriteByteStream(memoryStream, d);
			WriteByteStream(memoryStream, p);
			WriteByteStream(memoryStream, q);
			WriteByteStream(memoryStream, dP);
			WriteByteStream(memoryStream, dQ);
			WriteByteStream(memoryStream, inverseQ);
			return PackKeyHead(memoryStream.ToArray());
		}

		private static void WriteByteStream(MemoryStream ms, byte[] data)
		{
			bool flag = data[0] > 127;
			int num = (flag ? (data.Length + 1) : data.Length);
			ms.WriteByte(2);
			if (num < 128)
			{
				ms.WriteByte((byte)num);
			}
			else if (num < 256)
			{
				ms.WriteByte(129);
				ms.WriteByte((byte)num);
			}
			else
			{
				ms.WriteByte(130);
				ms.WriteByte(BitConverter.GetBytes(num)[1]);
				ms.WriteByte(BitConverter.GetBytes(num)[0]);
			}
			if (flag)
			{
				ms.WriteByte(0);
			}
			ms.Write(data, 0, data.Length);
		}

		/// <summary>
		/// 从RSA的算法对象里，获取到PEM格式的原始公钥数据，如果需要存储，或是显示，只需要 Convert.ToBase64String 方法<br />
		/// Obtain the original public key data in PEM format from the RSA algorithm object. If you need to store or display it, 
		/// you only need the Convert.ToBase64String method
		/// </summary>
		/// <param name="rsa">RSA 算法的加密解密对象</param>
		/// <returns>原始的公钥数据</returns>
		public static byte[] GetPublicKeyFromRSA(RSACryptoServiceProvider rsa)
		{
			RSAParameters rSAParameters = rsa.ExportParameters(includePrivateParameters: false);
			byte[] modulus = rSAParameters.Modulus;
			byte[] exponent = rSAParameters.Exponent;
			MemoryStream memoryStream = new MemoryStream();
			WriteByteStream(memoryStream, modulus);
			WriteByteStream(memoryStream, exponent);
			byte[] array = PackKeyHead(SoftBasic.SpliceArray<byte>(new byte[1], PackKeyHead(memoryStream.ToArray())));
			array[0] = 3;
			return PackKeyHead(SoftBasic.SpliceArray<byte>(SeqOID, array));
		}

		/// <summary>
		/// PEM 格式基于base64编码的公钥来创建一个 RSA 算法加密解密的对象，可以直接用于加密或是验证签名操作<br />
		/// Use the original public key data (PEM format) to create an RSA algorithm encryption and decryption object, 
		/// which can be directly used for encryption or signature verification
		/// </summary>
		/// <param name="publicKeyString">公钥</param>
		/// <returns>RSA 算法的加密解密对象</returns>
		public static RSACryptoServiceProvider CreateRsaProviderFromPublicKey(string publicKeyString)
		{
			publicKeyString = publicKeyString.Trim();
			if (publicKeyString.StartsWith("-----BEGIN PUBLIC KEY-----"))
			{
				publicKeyString = publicKeyString.Replace("-----BEGIN PUBLIC KEY-----", string.Empty);
			}
			if (publicKeyString.EndsWith("-----END PUBLIC KEY-----"))
			{
				publicKeyString = publicKeyString.Replace("-----END PUBLIC KEY-----", string.Empty);
			}
			return CreateRsaProviderFromPublicKey(Convert.FromBase64String(publicKeyString));
		}

		/// <summary>
		/// 对原始字节的数据进行加密，不限制长度，因为RSA本身限制了117字节，所以此处进行数据切割加密。<br />
		/// Encrypt the original byte data without limiting the length, because RSA itself limits 117 bytes, so the data is cut and encrypted here.
		/// </summary>
		/// <param name="provider">RSA公钥对象</param>
		/// <param name="data">等待加密的原始数据</param>
		/// <returns>加密之后的结果信息</returns>
		public static byte[] EncryptLargeDataByRSA(RSACryptoServiceProvider provider, byte[] data)
		{
			MemoryStream memoryStream = new MemoryStream();
			List<byte[]> list = SoftBasic.ArraySplitByLength(data, 110);
			for (int i = 0; i < list.Count; i++)
			{
				byte[] array = provider.Encrypt(list[i], fOAEP: false);
				memoryStream.Write(array, 0, array.Length);
			}
			return memoryStream.ToArray();
		}

		/// <summary>
		/// 对超过117字节限制的加密数据进行解密，解密出原始的字节数据，因为RSA本身限制了117字节，所以此处进行数据切割解密。<br />
		/// Decrypt the encrypted data that exceeds the 117-byte limit and decrypt the original byte data, because RSA itself limits 117 bytes, so the data is cut and decrypted here.
		/// </summary>
		/// <param name="provider">RSA私钥对象</param>
		/// <param name="data">等待解密的数据</param>
		/// <returns>解密之后的结果数据</returns>
		public static byte[] DecryptLargeDataByRSA(RSACryptoServiceProvider provider, byte[] data)
		{
			MemoryStream memoryStream = new MemoryStream();
			List<byte[]> list = SoftBasic.ArraySplitByLength(data, 128);
			for (int i = 0; i < list.Count; i++)
			{
				byte[] array = provider.Decrypt(list[i], fOAEP: false);
				memoryStream.Write(array, 0, array.Length);
			}
			return memoryStream.ToArray();
		}

		/// <summary>
		/// 使用原始的公钥数据（PEM格式）来创建一个 RSA 算法加密解密的对象，可以直接用于加密或是验证签名操作<br />
		/// Use the original public key data (PEM format) to create an RSA algorithm encryption and decryption object, 
		/// which can be directly used for encryption or signature verification
		/// </summary>
		/// <param name="publicKey">公钥</param>
		/// <returns>RSA 算法的加密解密对象</returns>
		public static RSACryptoServiceProvider CreateRsaProviderFromPublicKey(byte[] publicKey)
		{
			byte[] array = new byte[15];
			int num = publicKey.Length;
			using MemoryStream input = new MemoryStream(publicKey);
			using BinaryReader binaryReader = new BinaryReader(input);
			byte b = 0;
			ushort num2 = 0;
			switch (binaryReader.ReadUInt16())
			{
			case 33072:
				binaryReader.ReadByte();
				break;
			case 33328:
				binaryReader.ReadInt16();
				break;
			default:
				return null;
			}
			array = binaryReader.ReadBytes(15);
			if (!CompareBytearrays(array, SeqOID))
			{
				return null;
			}
			switch (binaryReader.ReadUInt16())
			{
			case 33027:
				binaryReader.ReadByte();
				break;
			case 33283:
				binaryReader.ReadInt16();
				break;
			default:
				return null;
			}
			if (binaryReader.ReadByte() != 0)
			{
				return null;
			}
			switch (binaryReader.ReadUInt16())
			{
			case 33072:
				binaryReader.ReadByte();
				break;
			case 33328:
				binaryReader.ReadInt16();
				break;
			default:
				return null;
			}
			num2 = binaryReader.ReadUInt16();
			byte b2 = 0;
			byte b3 = 0;
			switch (num2)
			{
			case 33026:
				b2 = binaryReader.ReadByte();
				break;
			case 33282:
				b3 = binaryReader.ReadByte();
				b2 = binaryReader.ReadByte();
				break;
			default:
				return null;
			}
			byte[] value = new byte[4] { b2, b3, 0, 0 };
			int num3 = BitConverter.ToInt32(value, 0);
			if (binaryReader.PeekChar() == 0)
			{
				binaryReader.ReadByte();
				num3--;
			}
			byte[] modulus = binaryReader.ReadBytes(num3);
			if (binaryReader.ReadByte() != 2)
			{
				return null;
			}
			int count = binaryReader.ReadByte();
			byte[] exponent = binaryReader.ReadBytes(count);
			RSACryptoServiceProvider rSACryptoServiceProvider = new RSACryptoServiceProvider();
			RSAParameters parameters = default(RSAParameters);
			parameters.Modulus = modulus;
			parameters.Exponent = exponent;
			rSACryptoServiceProvider.ImportParameters(parameters);
			return rSACryptoServiceProvider;
		}

		private static bool CompareBytearrays(byte[] a, byte[] b)
		{
			if (a.Length != b.Length)
			{
				return false;
			}
			int num = 0;
			foreach (byte b2 in a)
			{
				if (b2 != b[num])
				{
					return false;
				}
				num++;
			}
			return true;
		}
	}
}
