using System;
using System.Text;
using HslCommunication.BasicFramework;
using HslCommunication.Core.Net;
using HslCommunication.Profinet.AllenBradley;

namespace HslCommunication.Profinet.Omron
{
	/// <summary>
	/// 欧姆龙的CIP虚拟服务器
	/// </summary>
	public class OmronCipServer : AllenBradleyServer
	{
		/// <inheritdoc />
		public override void AddTagValue(string key, string value, int maxLength)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(value);
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = false,
				Buffer = SoftBasic.ArrayExpandToLength(SoftBasic.SpliceArray<byte>(BitConverter.GetBytes((ushort)bytes.Length), Encoding.UTF8.GetBytes(value)), maxLength),
				TypeLength = maxLength,
				TypeCode = 208
			});
		}

		/// <inheritdoc />
		public override void AddTagValue(string key, string[] value, int maxLength)
		{
			byte[] array = new byte[maxLength * value.Length];
			for (int i = 0; i < value.Length; i++)
			{
				byte[] bytes = Encoding.UTF8.GetBytes(value[i]);
				BitConverter.GetBytes((ushort)bytes.Length).CopyTo(array, maxLength * i);
				bytes.CopyTo(array, maxLength * i + 2);
			}
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = true,
				Buffer = array,
				TypeLength = maxLength,
				TypeCode = 208
			});
		}

		/// <inheritdoc />
		public override OperateResult<string> ReadString(string address, ushort length, Encoding encoding)
		{
			OperateResult<byte[]> operateResult = Read(address, length);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult);
			}
			if (operateResult.Content.Length >= 2)
			{
				int count = BitConverter.ToUInt16(operateResult.Content, 0);
				return OperateResult.CreateSuccessResult(encoding.GetString(operateResult.Content, 2, count));
			}
			return OperateResult.CreateSuccessResult(encoding.GetString(operateResult.Content));
		}

		/// <inheritdoc />
		public override OperateResult Write(string address, string value, Encoding encoding)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, value, 1024);
				return OperateResult.CreateSuccessResult();
			}
			bool flag = false;
			int addressIndex = GetAddressIndex(ref address);
			simpleHybird.Enter();
			if (abValues.ContainsKey(address))
			{
				flag = true;
				AllenBradleyItemValue allenBradleyItemValue = abValues[address];
				byte[] buffer = allenBradleyItemValue.Buffer;
				if (buffer != null && buffer.Length >= 2)
				{
					byte[] bytes = encoding.GetBytes(value);
					BitConverter.GetBytes((ushort)bytes.Length).CopyTo(allenBradleyItemValue.Buffer, addressIndex * allenBradleyItemValue.TypeLength);
					if (bytes.Length != 0)
					{
						Array.Copy(bytes, 0, allenBradleyItemValue.Buffer, 2 + addressIndex * allenBradleyItemValue.TypeLength, Math.Min(bytes.Length, allenBradleyItemValue.Buffer.Length - 2));
					}
				}
			}
			simpleHybird.Leave();
			if (!flag)
			{
				return new OperateResult<bool>(StringResources.Language.AllenBradley04);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		protected override byte[] ReadByCommand(PipeSession session, byte[] cipCore)
		{
			if (session.Tag is string)
			{
				return base.ReadByCommand(session, cipCore);
			}
			byte[] pathCommand = base.ByteTransform.TransByte(cipCore, 2, cipCore[1] * 2);
			string address = AllenBradleyHelper.ParseRequestPathCommand(pathCommand);
			OperateResult<byte[], ushort> operateResult = ReadWithType(address, -1);
			AllenBradleyItemValue addressItemValue = GetAddressItemValue(address);
			byte[] array = ((addressItemValue == null || addressItemValue.TypeCode != 193 || !addressItemValue.IsArray) ? AllenBradleyHelper.PackCommandResponse(operateResult.Content1, isRead: true) : AllenBradleyHelper.PackCommandResponse(SoftBasic.ByteToBoolArray(operateResult.Content1, operateResult.Content1.Length, 1).ToByteArray(), isRead: true));
			if (array.Length > 6)
			{
				BitConverter.GetBytes(operateResult.Content2).CopyTo(array, 4);
			}
			return array;
		}

		/// <inheritdoc />
		protected override byte[] WriteByMessage(byte[] cipCore)
		{
			if (!base.EnableWrite)
			{
				return AllenBradleyHelper.PackCommandResponse(null, isRead: false);
			}
			byte[] array = base.ByteTransform.TransByte(cipCore, 2, cipCore[1] * 2);
			string address = AllenBradleyHelper.ParseRequestPathCommand(array);
			ushort num = BitConverter.ToUInt16(cipCore, 2 + array.Length);
			ushort num2 = BitConverter.ToUInt16(cipCore, 4 + array.Length);
			byte[] array2 = base.ByteTransform.TransByte(cipCore, 6 + array.Length, cipCore.Length - 6 - array.Length);
			AllenBradleyItemValue addressItemValue = GetAddressItemValue(address);
			if (addressItemValue != null && addressItemValue.TypeCode == 193 && addressItemValue.IsArray)
			{
				if (Write(address, array2.SelectBegin(addressItemValue.Buffer.Length)).IsSuccess)
				{
					return AllenBradleyHelper.PackCommandResponse(new byte[0], isRead: false);
				}
				return AllenBradleyHelper.PackCommandResponse(null, isRead: false);
			}
			if (num == 193 && num2 == 1)
			{
				bool value = false;
				if (array2.Length == 2 && array2[0] == byte.MaxValue && array2[1] == byte.MaxValue)
				{
					value = true;
				}
				if (Write(address, value).IsSuccess)
				{
					return AllenBradleyHelper.PackCommandResponse(new byte[0], isRead: false);
				}
				return AllenBradleyHelper.PackCommandResponse(null, isRead: false);
			}
			if (Write(address, array2).IsSuccess)
			{
				return AllenBradleyHelper.PackCommandResponse(new byte[0], isRead: false);
			}
			return AllenBradleyHelper.PackCommandResponse(null, isRead: false);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"OmronCipServer[{base.Port}]";
		}
	}
}
