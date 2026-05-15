using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using HslCommunication.BasicFramework;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.IMessage;
using HslCommunication.Core.Net;
using HslCommunication.Core.Pipe;
using HslCommunication.Reflection;

namespace HslCommunication.Profinet.AllenBradley
{
	/// <summary>
	/// AB PLC的虚拟服务器，仅支持和HSL组件的完美通信，可以手动添加一些节点。<br />
	/// AB PLC's virtual server only supports perfect communication with HSL components. You can manually add some nodes.
	/// </summary>
	/// <remarks>
	/// 本AB的虚拟PLC仅限商业授权用户使用，感谢支持。
	/// </remarks>
	public class AllenBradleyServer : DeviceServer
	{
		private const int DataPoolLength = 65536;

		/// <summary>
		/// 所有标签的词典信息
		/// </summary>
		protected Dictionary<string, AllenBradleyItemValue> abValues;

		/// <summary>
		/// 当前词典的锁
		/// </summary>
		protected SimpleHybirdLock simpleHybird;

		private bool createTagWithWrite = false;

		private int value_6b_f68f = 123456;

		/// <summary>
		/// 获取或设置当前的服务器的数据字节排序情况<br />
		/// Gets or sets the data byte ordering of the current server
		/// </summary>
		public DataFormat DataFormat
		{
			get
			{
				return base.ByteTransform.DataFormat;
			}
			set
			{
				base.ByteTransform.DataFormat = value;
			}
		}

		/// <summary>
		/// 获取或设置当调用写入方法的时候，如果标签不存在，是否创建新的标签信息<br />
		/// Gets or sets whether to create a new tag information if the tag does not exist when the write method is called
		/// </summary>
		public bool CreateTagWithWrite
		{
			get
			{
				return createTagWithWrite;
			}
			set
			{
				createTagWithWrite = value;
			}
		}

		/// <summary>
		/// 实例化一个AB PLC协议的服务器<br />
		/// Instantiate an AB PLC protocol server
		/// </summary>
		public AllenBradleyServer()
		{
			base.WordLength = 2;
			base.ByteTransform = new RegularByteTransform();
			base.Port = 44818;
			simpleHybird = new SimpleHybirdLock();
			abValues = new Dictionary<string, AllenBradleyItemValue>();
		}

		/// <summary>
		/// 向服务器新增一个新的Tag值<br />
		/// Add a new tag value to the server
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">标签值</param>
		public void AddTagValue(string key, AllenBradleyItemValue value)
		{
			simpleHybird.Enter();
			if (abValues.ContainsKey(key))
			{
				abValues[key] = value;
			}
			else
			{
				abValues.Add(key, value);
			}
			simpleHybird.Leave();
		}

		/// <summary>
		/// 向服务器新增一个新的bool类型的Tag值，并赋予初始化的值<br />
		/// Add a new tag value of type bool to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, bool value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = false,
				Buffer = ((!value) ? new byte[2] : new byte[2] { 255, 255 }),
				TypeLength = 2,
				TypeCode = 193
			});
		}

		/// <summary>
		/// 向服务器新增一个新的bool数组类型的Tag值，并赋予初始化的值<br />
		/// Add a new tag value of type bool array to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, bool[] value)
		{
			if (value == null)
			{
				value = new bool[0];
			}
			byte[] array = new byte[value.Length];
			for (int i = 0; i < value.Length; i++)
			{
				array[i] = (byte)(value[i] ? 1u : 0u);
			}
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = true,
				Buffer = array,
				TypeLength = 1,
				TypeCode = 193
			});
		}

		/// <summary>
		/// 向服务器新增一个新的type类型的Tag值，并赋予初始化的值<br />
		/// Add a new type tag value to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, byte value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = false,
				Buffer = new byte[1] { value },
				TypeLength = 1,
				TypeCode = 194
			});
		}

		/// <summary>
		/// 向服务器新增一个新的short类型的Tag值，并赋予初始化的值<br />
		/// Add a new short tag value to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, short value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = false,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 2,
				TypeCode = 195
			});
		}

		/// <summary>
		/// 向服务器新增一个新的short数组的Tag值，并赋予初始化的值<br />
		/// Add a new short array Tag value to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, short[] value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = true,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 2,
				TypeCode = 195
			});
		}

		/// <summary>
		/// 向服务器新增一个新的ushort类型的Tag值，并赋予初始化的值<br />
		/// Add a new tag value of ushort type to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, ushort value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = false,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 2,
				TypeCode = 199
			});
		}

		/// <summary>
		/// 向服务器新增一个新的ushort数组的Tag值，并赋予初始化的值<br />
		/// Add a new ushort array Tag value to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, ushort[] value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = true,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 2,
				TypeCode = 199
			});
		}

		/// <summary>
		/// 向服务器新增一个新的int类型的Tag值，并赋予初始化的值<br />
		/// Add a new Tag value of type int to the server and assign the initialized value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, int value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = false,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 4,
				TypeCode = 196
			});
		}

		/// <summary>
		/// 向服务器新增一个新的int数组的Tag值，并赋予初始化的值<br />
		/// Add a new Tag value of the int array to the server and assign the initialized value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, int[] value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = true,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 4,
				TypeCode = 196
			});
		}

		/// <summary>
		/// 向服务器新增一个新的uint类型的Tag值，并赋予初始化的值<br />
		/// Add a new uint tag value to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, uint value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = false,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 4,
				TypeCode = 200
			});
		}

		/// <summary>
		/// 向服务器新增一个新的uint数组的Tag值，并赋予初始化的值<br />
		/// Add a new uint array Tag value to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, uint[] value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = true,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 4,
				TypeCode = 200
			});
		}

		/// <summary>
		/// 向服务器新增一个新的long类型的Tag值，并赋予初始化的值<br />
		/// Add a new Tag value of type long to the server and assign the initialized value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, long value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = false,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 8,
				TypeCode = 197
			});
		}

		/// <summary>
		/// 向服务器新增一个新的long数组的Tag值，并赋予初始化的值<br />
		/// Add a new Long array Tag value to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, long[] value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = true,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 8,
				TypeCode = 197
			});
		}

		/// <summary>
		/// 向服务器新增一个新的ulong类型的Tag值，并赋予初始化的值<br />
		/// Add a new Ulong type Tag value to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, ulong value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = false,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 8,
				TypeCode = 201
			});
		}

		/// <summary>
		/// 向服务器新增一个新的ulong数组的Tag值，并赋予初始化的值<br />
		/// Add a new Ulong array Tag value to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, ulong[] value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = true,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 8,
				TypeCode = 201
			});
		}

		/// <summary>
		/// 向服务器新增一个新的float类型的Tag值，并赋予初始化的值<br />
		/// Add a new tag value of type float to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, float value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = false,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 4,
				TypeCode = 202
			});
		}

		/// <summary>
		/// 向服务器新增一个新的float数组的Tag值，并赋予初始化的值<br />
		/// Add a new Tag value of the float array to the server and assign the initialized value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, float[] value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = true,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 4,
				TypeCode = 202
			});
		}

		/// <summary>
		/// 向服务器新增一个新的double类型的Tag值，并赋予初始化的值<br />
		/// Add a new tag value of type double to the server and assign the initialized value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, double value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = false,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 8,
				TypeCode = 203
			});
		}

		/// <summary>
		/// 向服务器新增一个新的double数组的Tag值，并赋予初始化的值<br />
		/// Add a new double array Tag value to the server and assign the initialized value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		public void AddTagValue(string key, double[] value)
		{
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = true,
				Buffer = base.ByteTransform.TransByte(value),
				TypeLength = 8,
				TypeCode = 203
			});
		}

		/// <summary>
		/// 向服务器新增一个新的string类型的Tag值，并赋予初始化的值<br />
		/// Add a new Tag value of string type to the server and assign the initial value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		/// <param name="maxLength">字符串的最长值</param>
		public virtual void AddTagValue(string key, string value, int maxLength)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(value);
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = false,
				Buffer = SoftBasic.ArrayExpandToLength(SoftBasic.SpliceArray<byte>(new byte[2], BitConverter.GetBytes(bytes.Length), Encoding.UTF8.GetBytes(value)), maxLength),
				TypeLength = maxLength,
				TypeCode = 209
			});
		}

		/// <summary>
		/// 向服务器新增一个新的string数组的Tag值，并赋予初始化的值<br />
		/// Add a new String array Tag value to the server and assign the initialized value
		/// </summary>
		/// <param name="key">Tag名称</param>
		/// <param name="value">值信息</param>
		/// <param name="maxLength">字符串的最长值</param>
		public virtual void AddTagValue(string key, string[] value, int maxLength)
		{
			byte[] array = new byte[maxLength * value.Length];
			for (int i = 0; i < value.Length; i++)
			{
				byte[] bytes = Encoding.UTF8.GetBytes(value[i]);
				BitConverter.GetBytes(bytes.Length).CopyTo(array, maxLength * i + 2);
				bytes.CopyTo(array, maxLength * i + 6);
			}
			AddTagValue(key, new AllenBradleyItemValue
			{
				IsArray = true,
				Buffer = array,
				TypeLength = maxLength,
				TypeCode = 209
			});
		}

		/// <inheritdoc />
		[HslMqttApi("ReadByteArray", "")]
		public override OperateResult<byte[]> Read(string address, ushort length)
		{
			return ReadWithType(address, length).Then((byte[] m, ushort n) => OperateResult.CreateSuccessResult(m));
		}

		/// <summary>
		/// 获取当前的索引信息
		/// </summary>
		/// <param name="address">数据标签信息</param>
		/// <returns>索引信息</returns>
		protected int GetAddressIndex(ref string address)
		{
			Match match = Regex.Match(address, "\\[[0-9]+\\]$");
			if (match.Success)
			{
				address = address.Substring(0, match.Index);
				return Convert.ToInt32(match.Value.Substring(1, match.Length - 2));
			}
			return 0;
		}

		/// <inheritdoc />
		protected OperateResult<byte[], ushort> ReadWithType(string address, int length)
		{
			int addressIndex = GetAddressIndex(ref address);
			ushort value = 0;
			byte[] array = null;
			simpleHybird.Enter();
			if (abValues.ContainsKey(address))
			{
				AllenBradleyItemValue allenBradleyItemValue = abValues[address];
				value = allenBradleyItemValue.TypeCode;
				if (!allenBradleyItemValue.IsArray)
				{
					array = new byte[allenBradleyItemValue.Buffer.Length];
					allenBradleyItemValue.Buffer.CopyTo(array, 0);
				}
				else if (length < 0)
				{
					array = new byte[allenBradleyItemValue.Buffer.Length];
					Array.Copy(allenBradleyItemValue.Buffer, 0, array, 0, allenBradleyItemValue.Buffer.Length);
				}
				else if (addressIndex * allenBradleyItemValue.TypeLength + length * allenBradleyItemValue.TypeLength <= allenBradleyItemValue.Buffer.Length)
				{
					array = new byte[length * allenBradleyItemValue.TypeLength];
					Array.Copy(allenBradleyItemValue.Buffer, addressIndex * allenBradleyItemValue.TypeLength, array, 0, array.Length);
				}
			}
			simpleHybird.Leave();
			if (array == null)
			{
				return new OperateResult<byte[], ushort>(StringResources.Language.AllenBradley04);
			}
			return OperateResult.CreateSuccessResult(array, value);
		}

		/// <summary>
		/// 根据地址获取标签信息
		/// </summary>
		/// <param name="address">地址信息</param>
		/// <returns>ab-plc的点位标签信息</returns>
		protected AllenBradleyItemValue GetAddressItemValue(string address)
		{
			int addressIndex = GetAddressIndex(ref address);
			AllenBradleyItemValue result = null;
			simpleHybird.Enter();
			if (abValues.ContainsKey(address))
			{
				result = abValues[address];
			}
			simpleHybird.Leave();
			return result;
		}

		/// <inheritdoc />
		[HslMqttApi("WriteByteArray", "")]
		public override OperateResult Write(string address, byte[] value)
		{
			return Write(address, value, 0);
		}

		private OperateResult Write(string address, byte[] value, int offset)
		{
			int addressIndex = GetAddressIndex(ref address);
			bool flag = false;
			simpleHybird.Enter();
			if (abValues.ContainsKey(address))
			{
				AllenBradleyItemValue allenBradleyItemValue = abValues[address];
				if (!allenBradleyItemValue.IsArray)
				{
					if (allenBradleyItemValue.Buffer.Length == value.Length)
					{
						allenBradleyItemValue.Buffer = value;
						flag = true;
					}
					else if (allenBradleyItemValue.Buffer.Length > value.Length)
					{
						value.CopyTo(allenBradleyItemValue.Buffer, 0);
						flag = true;
					}
				}
				else if (addressIndex * allenBradleyItemValue.TypeLength + value.Length <= allenBradleyItemValue.Buffer.Length)
				{
					Array.Copy(value, 0, allenBradleyItemValue.Buffer, addressIndex * allenBradleyItemValue.TypeLength + offset, value.Length);
					flag = true;
				}
			}
			simpleHybird.Leave();
			if (!flag)
			{
				return new OperateResult(StringResources.Language.AllenBradley04);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyNet.ReadByte(System.String)" />
		[HslMqttApi("ReadByte", "")]
		public OperateResult<byte> ReadByte(string address)
		{
			return ByteTransformHelper.GetResultFromArray(Read(address, 1));
		}

		/// <inheritdoc cref="M:HslCommunication.Profinet.AllenBradley.AllenBradleyNet.Write(System.String,System.Byte)" />
		[HslMqttApi("WriteByte", "")]
		public OperateResult Write(string address, byte value)
		{
			return Write(address, new byte[1] { value });
		}

		/// <inheritdoc />
		[HslMqttApi("ReadBoolArray", "")]
		public override OperateResult<bool[]> ReadBool(string address, ushort length)
		{
			int addressIndex = GetAddressIndex(ref address);
			bool flag = false;
			bool[] array = null;
			simpleHybird.Enter();
			if (abValues.ContainsKey(address))
			{
				flag = true;
				AllenBradleyItemValue allenBradleyItemValue = abValues[address];
				if (!allenBradleyItemValue.IsArray)
				{
					array = ((allenBradleyItemValue.TypeCode != 193) ? allenBradleyItemValue.Buffer.ToBoolArray() : ((allenBradleyItemValue.Buffer[0] != byte.MaxValue || allenBradleyItemValue.Buffer[1] != byte.MaxValue) ? new bool[1] : new bool[1] { true }));
				}
				else if (addressIndex * allenBradleyItemValue.TypeLength + length * allenBradleyItemValue.TypeLength <= allenBradleyItemValue.Buffer.Length)
				{
					array = new bool[length * allenBradleyItemValue.TypeLength];
					for (int i = 0; i < array.Length; i++)
					{
						array[i] = allenBradleyItemValue.Buffer[addressIndex * allenBradleyItemValue.TypeLength + i] != 0;
					}
				}
			}
			simpleHybird.Leave();
			if (!flag)
			{
				return new OperateResult<bool[]>(StringResources.Language.AllenBradley04);
			}
			return OperateResult.CreateSuccessResult(array);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteBoolArray", "")]
		public override OperateResult Write(string address, bool[] value)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, value);
				return OperateResult.CreateSuccessResult();
			}
			int addressIndex = GetAddressIndex(ref address);
			bool flag = false;
			simpleHybird.Enter();
			if (abValues.ContainsKey(address))
			{
				flag = true;
				AllenBradleyItemValue allenBradleyItemValue = abValues[address];
				if (!allenBradleyItemValue.IsArray)
				{
					if (value[0])
					{
						byte[] buffer = allenBradleyItemValue.Buffer;
						if (buffer != null && buffer.Length != 0)
						{
							allenBradleyItemValue.Buffer[0] = byte.MaxValue;
						}
						byte[] buffer2 = allenBradleyItemValue.Buffer;
						if (buffer2 != null && buffer2.Length > 1)
						{
							allenBradleyItemValue.Buffer[1] = byte.MaxValue;
						}
					}
					else
					{
						byte[] buffer3 = allenBradleyItemValue.Buffer;
						if (buffer3 != null && buffer3.Length != 0)
						{
							allenBradleyItemValue.Buffer[0] = 0;
						}
						byte[] buffer4 = allenBradleyItemValue.Buffer;
						if (buffer4 != null && buffer4.Length > 1)
						{
							allenBradleyItemValue.Buffer[1] = 0;
						}
					}
				}
				else if (addressIndex * allenBradleyItemValue.TypeLength + value.Length * allenBradleyItemValue.TypeLength <= allenBradleyItemValue.Buffer.Length)
				{
					for (int i = 0; i < value.Length; i++)
					{
						allenBradleyItemValue.Buffer[addressIndex * allenBradleyItemValue.TypeLength + i] = (byte)(value[i] ? 1 : 0);
					}
				}
			}
			simpleHybird.Leave();
			if (!flag)
			{
				return new OperateResult(StringResources.Language.AllenBradley04);
			}
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		public override OperateResult<string> ReadString(string address, ushort length, Encoding encoding)
		{
			OperateResult<byte[]> operateResult = Read(address, length);
			if (!operateResult.IsSuccess)
			{
				return OperateResult.CreateFailedResult<string>(operateResult);
			}
			if (operateResult.Content.Length >= 6)
			{
				int count = BitConverter.ToInt32(operateResult.Content, 2);
				return OperateResult.CreateSuccessResult(encoding.GetString(operateResult.Content, 6, count));
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
				if (buffer != null && buffer.Length >= 6)
				{
					byte[] bytes = encoding.GetBytes(value);
					BitConverter.GetBytes(bytes.Length).CopyTo(allenBradleyItemValue.Buffer, 2 + addressIndex * allenBradleyItemValue.TypeLength);
					if (bytes.Length != 0)
					{
						Array.Copy(bytes, 0, allenBradleyItemValue.Buffer, 6 + addressIndex * allenBradleyItemValue.TypeLength, Math.Min(bytes.Length, allenBradleyItemValue.Buffer.Length - 6));
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
		protected override INetMessage GetNewNetMessage()
		{
			return new AllenBradleyMessage();
		}

		/// <inheritdoc />
		protected override OperateResult ThreadPoolLoginAfterClientCheck(PipeSession session, IPEndPoint endPoint)
		{
			session.Tag = new CipSessionTag();
			CommunicationPipe communication = session.Communication;
			OperateResult<byte[]> operateResult = communication.ReceiveMessage(new AllenBradleyMessage(), null, useActivePush: false, null, delegate(byte[] m)
			{
				LogRevcMessage(m, session);
			});
			if (!operateResult.IsSuccess)
			{
				return operateResult;
			}
			if (operateResult.Content[0] == 4)
			{
				byte[] array = AllenBradleyHelper.PackRequestHeader(4, 0u, "01 00 00 01 14 00 01 00 20 01 43 6F 6D 6D 75 6E 69 63 61 74 69 6F 6E 73 00 00".ToHexBytes());
				LogSendMessage(array, session);
				OperateResult operateResult2 = communication.Send(array);
				if (!operateResult2.IsSuccess)
				{
					return operateResult2;
				}
				operateResult = communication.ReceiveMessage(new AllenBradleyMessage(), null, useActivePush: false, null, delegate(byte[] m)
				{
					LogRevcMessage(m, session);
				});
				if (!operateResult.IsSuccess)
				{
					return operateResult;
				}
			}
			byte[] buffer = new byte[4];
			HslHelper.HslRandom.NextBytes(buffer);
			byte[] array2 = AllenBradleyHelper.PackRequestHeader(101, base.ByteTransform.TransUInt32(buffer, 0), (operateResult.Content.Length >= 24) ? operateResult.Content.RemoveBegin(24) : new byte[0]);
			LogSendMessage(array2, session);
			OperateResult operateResult3 = communication.Send(array2);
			if (!operateResult3.IsSuccess)
			{
				return operateResult3;
			}
			session.SessionID = base.ByteTransform.TransUInt32(buffer, 0).ToString();
			return OperateResult.CreateSuccessResult();
		}

		/// <inheritdoc />
		protected override OperateResult<byte[]> ReadFromCoreServer(PipeSession session, byte[] receive)
		{
			string text = base.ByteTransform.TransUInt32(receive, 4).ToString();
			if (text != session.SessionID)
			{
				base.LogNet?.WriteDebug(ToString(), "SessionID 不一致的请求，要求ID：" + session.SessionID + " 实际ID：" + text);
				return OperateResult.CreateSuccessResult(AllenBradleyHelper.PackRequestHeader(102, 100u, base.ByteTransform.TransUInt32(receive, 4), new byte[0]));
			}
			byte[] array = ReadFromCipCore(session, receive);
			if (array == null)
			{
				return OperateResult.CreateSuccessResult(AllenBradleyHelper.PackRequestHeader(111, 1u, BitConverter.ToUInt32(receive, 4), new byte[0]));
			}
			if (array.Length >= 8)
			{
				Array.Copy(receive, 4, array, 4, 4);
			}
			return OperateResult.CreateSuccessResult(array);
		}

		/// <summary>
		/// 当收到CIP协议的报文的时候应该触发的方法，允许继承重写，来实现自定义的返回，或是数据监听。
		/// </summary>
		/// <param name="session">当前客户端的会话信息</param>
		/// <param name="cipAll">CIP报文数据</param>
		/// <returns>返回的报文信息</returns>
		protected virtual byte[] ReadFromCipCore(PipeSession session, byte[] cipAll)
		{
			CipSessionTag cipSessionTag = session.Tag as CipSessionTag;
			uint session2 = BitConverter.ToUInt32(cipAll, 4);
			if (BitConverter.ToInt16(cipAll, 0) == 102)
			{
				return AllenBradleyHelper.PackRequestHeader(102, session2, new byte[0]);
			}
			byte[] array = SoftBasic.ArrayRemoveBegin(cipAll, 24);
			if (array.Length == 22)
			{
				return AllenBradleyHelper.PackRequestHeader(102, session2, AllenBradleyHelper.PackCommandSpecificData(new byte[4], AllenBradleyHelper.PackCommandSingleService("810000002f000c006706020630005be104010a4e4a3530312d31353030".ToHexBytes(), 178, isConnected: false, 0)));
			}
			if (array[12] == 178 && array[16] == 91)
			{
				cipSessionTag.IsConnectedCIP = true;
				cipSessionTag.TOConnectID = array.SelectMiddle(28, 4);
				return AllenBradleyHelper.PackRequestHeader(102, session2, AllenBradleyHelper.PackCommandSpecificData(new byte[4], AllenBradleyHelper.PackCommandSingleService("db000000c109415f0100fe8002001b0558bcbf0280841e0080841e000000".ToHexBytes(), 178, isConnected: false, 0)));
			}
			if (cipAll[0] == 111 && array[12] == 178 && array[16] == 84)
			{
				cipSessionTag.IsConnectedCIP = true;
				byte[] array2 = "d4 00 00 00 02 6f 4c 02 01 47 4c 02 02 00 01 00 f0 78 07 01 e0 70 72 00 e0 70 72 00 00 00".ToHexBytes();
				byte[] bytes = BitConverter.GetBytes(HslHelper.HslRandom.Next());
				bytes.CopyTo(array2, 4);
				cipSessionTag.TOConnectID = array.SelectMiddle(28, 4);
				Array.Copy(array, 28, array2, 8, 6);
				return AllenBradleyHelper.PackRequestHeader(111, session2, AllenBradleyHelper.PackCommandSpecificData(new byte[4], AllenBradleyHelper.PackCommandSingleService(array2, 178, isConnected: false, 0)), cipAll.SelectMiddle(12, 8));
			}
			if (cipAll[0] == 111 && array[12] == 178 && array[16] == 78)
			{
				return AllenBradleyHelper.PackRequestHeader(111, session2, AllenBradleyHelper.PackCommandSpecificData(GetConnectAddressItem(cipSessionTag), AllenBradleyHelper.PackCommandSingleService("CE 00 00 00 02 6f 4c 02 01 47 4c 02 02 00 01 00 f0 78 07 01 e0 70 72 00 e0 70 72 00 00 00".ToHexBytes(), 178, isConnected: false, 0)), cipAll.SelectMiddle(12, 8));
			}
			if (array[26] == 10 && array[27] == 2 && array[28] == 32 && array[29] == 2 && array[30] == 36 && array[31] == 1)
			{
				return AllenBradleyHelper.PackRequestHeader(111, 1u, session2, new byte[0]);
			}
			if (cipSessionTag.IsConnectedCIP)
			{
				byte[] array3 = base.ByteTransform.TransByte(array, 22, BitConverter.ToInt16(array, 18) - 2);
				if (array3[0] == 76)
				{
					return AllenBradleyHelper.PackRequestHeader(112, session2, AllenBradleyHelper.PackCommandSpecificData(GetConnectAddressItem(cipSessionTag), AllenBradleyHelper.PackCommandSingleService(ReadByCommand(session, array3), 177, isConnected: true, BitConverter.ToUInt16(array, 20))));
				}
				if (array3[0] == 77)
				{
					byte[] array4 = AllenBradleyHelper.PackCommandSingleService(WriteByMessage(array3), 177, isConnected: true, BitConverter.ToUInt16(array, 20));
					array4[2] = 0;
					array4[3] = 0;
					return AllenBradleyHelper.PackRequestHeader(102, session2, AllenBradleyHelper.PackCommandSpecificData(GetConnectAddressItem(cipSessionTag), array4));
				}
				return AllenBradleyHelper.PackRequestHeader(111, 1u, session2, new byte[0]);
			}
			byte[] array5 = base.ByteTransform.TransByte(array, 26, BitConverter.ToInt16(array, 24));
			if (array5[0] == 76 || array5[0] == 82)
			{
				return AllenBradleyHelper.PackRequestHeader(102, session2, AllenBradleyHelper.PackCommandSpecificData(new byte[4], AllenBradleyHelper.PackCommandSingleService(ReadByCommand(session, array5), 178, isConnected: false, 0)));
			}
			if (array5[0] == 77)
			{
				return AllenBradleyHelper.PackRequestHeader(102, session2, AllenBradleyHelper.PackCommandSpecificData(new byte[4], AllenBradleyHelper.PackCommandSingleService(WriteByMessage(array5), 178, isConnected: false, 0)));
			}
			if (array5[0] == 85)
			{
				return AllenBradleyHelper.PackRequestHeader(111, session2, AllenBradleyHelper.PackCommandSpecificData(new byte[4], AllenBradleyHelper.PackCommandSingleService(ReadList(array5), 178, isConnected: false, 0)));
			}
			return AllenBradleyHelper.PackRequestHeader(111, 1u, session2, new byte[0]);
		}

		private byte[] GetConnectAddressItem(CipSessionTag tag)
		{
			byte[] array = new byte[8] { 161, 0, 4, 0, 65, 1, 25, 7 };
			tag.TOConnectID.CopyTo(array, 4);
			return array;
		}

		private byte[] ReadList(byte[] cipCore)
		{
			if (cipCore[1] == 14 && cipCore[2] == 145)
			{
				return SoftBasic.HexStringToBytes("\r\nD5 00 00 00\r\nfa 1b 00 00 02 00 42 41 c1 00 00 00 00 00 00 00\r\n00 00 00 00 00 00 20 24 00 00 13 00 52 6f 75 74\r\n69 6e 65 3a 4d 61 69 6e 52 6f 75 74 69 6e 65 6d\r\n10 00 00 00 00 00 00 00 00 00 00 00 00 82 25 00\r\n00 13 00 5f 5f 6c 30 31 44 38 34 31 38 46 32 46\r\n38 31 46 31 46 30 c4 00 00 00 00 00 00 00 00 00\r\n00 00 00 00 e9 2b 00 00 09 00 5f 5f 53 4c 34 39\r\n31 30 39 c4 20 04 00 00 00 00 00 00 00 00 00 00\r\n00 68 31 00 00 13 00 5f 5f 6c 30 31 44 38 35 46\r\n41 34 32 46 38 31 43 32 35 33 c4 00 00 00 00 00\r\n00 00 00 00 00 00 00 00 3d 38 00 00 13 00 5f 5f\r\n6c 30 31 44 38 30 31 44 46 32 46 38 31 38 44 45\r\n38 c4 00 00 00 00 00 00 00 00 00 00 00 00 00 24\r\n59 00 00 0d 00 52 6f 75 74 69 6e 65 3a 54 49 4d\r\n45 52 6d 10 00 00 00 00 00 00 00 00 00 00 00 00\r\n4b 7c 00 00 13 00 5f 5f 6c 30 31 44 38 36 44 43\r\n35 32 46 38 31 42 44 34 38 c4 00 00 00 00 00 00\r\n00 00 00 00 00 00 00 97 8a 00 00 06 00 53 43 4c\r\n5f 30 31 8a 8f 00 00 00 00 00 00 00 00 00 00 00\r\n00 23 b7 00 00 02 00 41 42 c1 00 00 00 00 00 00\r\n00 00 00 00 00 00 00 d5 bf 00 00 0d 00 52 6f 75\r\n74 69 6e 65 3a 43 4f 55 4e 54 6d 10 00 00 00 00\r\n00 00 00 00 00 00 00 00 1e da 00 00 0e 00 52 6f\r\n75 74 69 6e 65 3a 61 6e 61 6c 6f 67 6d 10 00 00\r\n00 00 00 00 00 00 00 00 00 00\r\n");
			}
			return BitConverter.ToUInt16(cipCore, 6) switch
			{
				0 => SoftBasic.HexStringToBytes("\r\nd5 00 06 00\r\n40 07 00 00 03 00 4f 55 54 c4 00 00 00 00 00 00\r\n00 00 00 00 00 00 00 e7 0e 00 00 0d 00 54 69 6d\r\n69 6e 67 5f 41 63 74 69 76 65 c1 00 00 00 00 00\r\n00 00 00 00 00 00 00 00 d1 18 00 00 03 00 5a 58\r\n43 c1 00 00 00 00 00 00 00 00 00 00 00 00 00 fa\r\n1b 00 00 13 00 50 72 6f 67 72 61 6d 3a 4d 61 69\r\n6e 50 72 6f 67 72 61 6d 68 10 00 00 00 00 00 00\r\n00 00 00 00 00 00 3f 20 00 00 04 00 54 45 53 54\r\nce 8f 00 00 00 00 00 00 00 00 00 00 00 00 20 24\r\n00 00 09 00 4d 61 70 3a 4c 6f 63 61 6c 69 10 00\r\n00 00 00 00 00 00 00 00 00 00 00 82 25 00 00 12\r\n00 43 78 6e 3a 46 75 73 65 64 3a 33 32 39 32 64\r\n66 32 33 7e 10 00 00 00 00 00 00 00 00 00 00 00\r\n00 eb 2a 00 00 02 00 49 4e c4 00 00 00 00 00 00\r\n00 00 00 00 00 00 00 e9 2b 00 00 09 00 4c 6f 63\r\n61 6c 3a 32 3a 43 23 82 00 00 00 00 00 00 00 00\r\n00 00 00 00 68 31 00 00 09 00 4d 61 70 3a 4f 56\r\n31 36 45 69 10 00 00 00 00 00 00 00 00 00 00 00\r\n00 b4 35 00 00 06 00 54 49 4d 45 52 31 83 8f 00\r\n00 00 00 00 00 00 00 00 00 00 00 3d 38 00 00 09\r\n00 4c 6f 63 61 6c 3a 33 3a 4f d5 8a 00 00 00 00\r\n00 00 00 00 00 00 00 00 2e 3f 00 00 08 00 49 6e\r\n52 61 77 4d 61 78 c4 00 00 00 00 00 00 00 00 00\r\n00 00 00 00 a5 41 00 00 0f 00 53 65 6c 65 63 74\r\n6f 72 5f 53 77 69 74 63 68 c1 03 00 00 00 00 00\r\n00 00 00 00 00 00 00 73 4a 00 00 07 00 49 4e 54\r\n54 45 53 54 c4 20 01 00 00 00 00 00 00 00 00 00\r\n00 00 ec 50 00 00 09 00 4c 6f 63 61 6c 3a 33 3a\r\n43 24 86 00 00 00 00 00 00 00 00 00 00 00 00 24\r\n59 00 00 08 00 4d 61 70 3a 49 56 33 32 69 10 00\r\n00 00 00 00 00 00 00 00 00 00 00\r\n"), 
				22821 => SoftBasic.HexStringToBytes("\r\nd5 00 00 00\r\n36 71 00 00 07 00 49 6e 45 75 4d 61 78 c4 00 00\r\n00 00 00 00 00 00 00 00 00 00 00 4b 7c 00 00 09\r\n00 4c 6f 63 61 6c 3a 33 3a 49 fa 8e 00 00 00 00\r\n00 00 00 00 00 00 00 00 c3 81 00 00 05 00 43 4f\r\n55 4e 54 82 8f 00 00 00 00 00 00 00 00 00 00 00\r\n00 97 8a 00 00 09 00 4c 6f 63 61 6c 3a 32 3a 49\r\n20 89 00 00 00 00 00 00 00 00 00 00 00 00 b0 9b\r\n00 00 08 00 70 65 69 66 61 6e 67 73 97 8d 00 00\r\n00 00 00 00 00 00 00 00 00 00 0a b4 00 00 09 00\r\n52 54 4f 5f 52 65 73 65 74 c1 00 00 00 00 00 00\r\n00 00 00 00 00 00 00 a1 b5 00 00 07 00 49 6e 45\r\n75 4d 69 6e c4 00 00 00 00 00 00 00 00 00 00 00\r\n00 00 23 b7 00 00 0d 00 54 61 73 6b 3a 4d 61 69\r\n6e 54 61 73 6b 70 10 00 00 00 00 00 00 00 00 00\r\n00 00 00 d5 bf 00 00 08 00 4d 61 70 3a 4c 49 4e\r\n4b 69 10 00 00 00 00 00 00 00 00 00 00 00 00 9b\r\nc1 00 00 08 00 49 6e 52 61 77 4d 69 6e c4 00 00\r\n00 00 00 00 00 00 00 00 00 00 00 26 c2 00 00 03\r\n00 41 42 43 c1 00 00 00 00 00 00 00 00 00 00 00\r\n00 00 1e da 00 00 1a 00 43 78 6e 3a 53 74 61 6e\r\n64 61 72 64 49 6e 70 75 74 3a 32 34 66 36 36 38\r\n30 36 7e 10 00 00 00 00 00 00 00 00 00 00 00 00\r\n92 e7 00 00 0e 00 53 65 6c 65 63 74 6f 72 53 77\r\n69 74 63 68 c1 02 00 00 00 00 00 00 00 00 00 00\r\n00 00\r\n\r\n"), 
				_ => null, 
			};
		}

		/// <summary>
		/// 根据CIP的核心读取到相关实际数据信息
		/// </summary>
		/// <param name="session">当前的会话信息</param>
		/// <param name="cipCore">CIP核心报文</param>
		/// <returns>实际的报文信息</returns>
		protected virtual byte[] ReadByCommand(PipeSession session, byte[] cipCore)
		{
			OperateResult<byte[], ushort> operateResult = null;
			byte[] array = base.ByteTransform.TransByte(cipCore, 2, cipCore[1] * 2);
			OperateResult<int, int> operateResult2 = AllenBradleyHelper.ParseRequestPathSymbolInstanceAddressing(array);
			if (operateResult2.IsSuccess)
			{
				if (operateResult2.Content1 == 107 && operateResult2.Content2 == 63119)
				{
					operateResult = OperateResult.CreateSuccessResult(BitConverter.GetBytes(value_6b_f68f), (ushort)196);
				}
			}
			else
			{
				string address = AllenBradleyHelper.ParseRequestPathCommand(array);
				ushort length = BitConverter.ToUInt16(cipCore, 2 + array.Length);
				operateResult = ReadWithType(address, length);
			}
			if (operateResult == null)
			{
				operateResult = new OperateResult<byte[], ushort>(StringResources.Language.AllenBradley04);
			}
			byte[] array2 = AllenBradleyHelper.PackCommandResponse(operateResult.Content1, isRead: true);
			if (array2.Length > 6)
			{
				BitConverter.GetBytes(operateResult.Content2).CopyTo(array2, 4);
			}
			return array2;
		}

		/// <summary>
		/// 根据CIP和核心写入相关的地址数据信息
		/// </summary>
		/// <param name="cipCore">CIP核心报文</param>
		/// <returns>实际的报文信息</returns>
		protected virtual byte[] WriteByMessage(byte[] cipCore)
		{
			if (!base.EnableWrite)
			{
				return AllenBradleyHelper.PackCommandResponse(null, isRead: false);
			}
			byte[] array = base.ByteTransform.TransByte(cipCore, 2, cipCore[1] * 2);
			OperateResult<int, int> operateResult = AllenBradleyHelper.ParseRequestPathSymbolInstanceAddressing(array);
			if (operateResult.IsSuccess)
			{
				byte[] array2 = base.ByteTransform.TransByte(cipCore, 6 + array.Length, cipCore.Length - 6 - array.Length);
				if (operateResult.Content1 == 107 && operateResult.Content2 == 63119)
				{
					if (array2.Length >= 4)
					{
						value_6b_f68f = BitConverter.ToInt32(array2, 0);
					}
					return AllenBradleyHelper.PackCommandResponse(new byte[0], isRead: false);
				}
				return AllenBradleyHelper.PackCommandResponse(null, isRead: false);
			}
			string text = AllenBradleyHelper.ParseRequestPathCommand(array);
			if (text.EndsWith(".LEN"))
			{
				return AllenBradleyHelper.PackCommandResponse(new byte[0], isRead: false);
			}
			if (text.EndsWith(".DATA[0]"))
			{
				text = text.Replace(".DATA[0]", "");
				byte[] bytes = base.ByteTransform.TransByte(cipCore, 6 + array.Length, cipCore.Length - 6 - array.Length);
				if (Write(text, Encoding.UTF8.GetString(bytes).TrimEnd(default(char)), Encoding.UTF8).IsSuccess)
				{
					return AllenBradleyHelper.PackCommandResponse(new byte[0], isRead: false);
				}
				return AllenBradleyHelper.PackCommandResponse(null, isRead: false);
			}
			ushort num = BitConverter.ToUInt16(cipCore, 2 + array.Length);
			ushort num2 = BitConverter.ToUInt16(cipCore, 4 + array.Length);
			if (cipCore[0] == 83)
			{
				int offset = BitConverter.ToInt32(cipCore, 6 + array.Length);
				byte[] value = base.ByteTransform.TransByte(cipCore, 10 + array.Length, cipCore.Length - 10 - array.Length);
				if (Write(text, value, offset).IsSuccess)
				{
					return AllenBradleyHelper.PackCommandResponse(new byte[0], isRead: false);
				}
				return AllenBradleyHelper.PackCommandResponse(null, isRead: false);
			}
			byte[] array3 = base.ByteTransform.TransByte(cipCore, 6 + array.Length, cipCore.Length - 6 - array.Length);
			if (num == 193 && num2 == 1)
			{
				bool value2 = false;
				if (array3.Length == 2 && array3[0] == byte.MaxValue && array3[1] == byte.MaxValue)
				{
					value2 = true;
				}
				if (Write(text, value2).IsSuccess)
				{
					return AllenBradleyHelper.PackCommandResponse(new byte[0], isRead: false);
				}
				return AllenBradleyHelper.PackCommandResponse(null, isRead: false);
			}
			if (Write(text, array3).IsSuccess)
			{
				return AllenBradleyHelper.PackCommandResponse(new byte[0], isRead: false);
			}
			return AllenBradleyHelper.PackCommandResponse(null, isRead: false);
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				simpleHybird.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <inheritdoc />
		[HslMqttApi("ReadInt16Array", "")]
		public override OperateResult<short[]> ReadInt16(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, length), (byte[] m) => base.ByteTransform.TransInt16(m, 0, length));
		}

		/// <inheritdoc />
		[HslMqttApi("ReadUInt16Array", "")]
		public override OperateResult<ushort[]> ReadUInt16(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, length), (byte[] m) => base.ByteTransform.TransUInt16(m, 0, length));
		}

		/// <inheritdoc />
		[HslMqttApi("ReadInt32Array", "")]
		public override OperateResult<int[]> ReadInt32(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, length), (byte[] m) => base.ByteTransform.TransInt32(m, 0, length));
		}

		/// <inheritdoc />
		[HslMqttApi("ReadUInt32Array", "")]
		public override OperateResult<uint[]> ReadUInt32(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, length), (byte[] m) => base.ByteTransform.TransUInt32(m, 0, length));
		}

		/// <inheritdoc />
		[HslMqttApi("ReadInt64Array", "")]
		public override OperateResult<long[]> ReadInt64(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, length), (byte[] m) => base.ByteTransform.TransInt64(m, 0, length));
		}

		/// <inheritdoc />
		[HslMqttApi("ReadUInt64Array", "")]
		public override OperateResult<ulong[]> ReadUInt64(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, length), (byte[] m) => base.ByteTransform.TransUInt64(m, 0, length));
		}

		/// <inheritdoc />
		[HslMqttApi("ReadFloatArray", "")]
		public override OperateResult<float[]> ReadFloat(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, length), (byte[] m) => base.ByteTransform.TransSingle(m, 0, length));
		}

		/// <inheritdoc />
		[HslMqttApi("ReadDoubleArray", "")]
		public override OperateResult<double[]> ReadDouble(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(Read(address, length), (byte[] m) => base.ByteTransform.TransDouble(m, 0, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<short[]>> ReadInt16Async(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, length), (byte[] m) => base.ByteTransform.TransInt16(m, 0, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<ushort[]>> ReadUInt16Async(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, length), (byte[] m) => base.ByteTransform.TransUInt16(m, 0, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<int[]>> ReadInt32Async(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, length), (byte[] m) => base.ByteTransform.TransInt32(m, 0, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<uint[]>> ReadUInt32Async(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, length), (byte[] m) => base.ByteTransform.TransUInt32(m, 0, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<long[]>> ReadInt64Async(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, length), (byte[] m) => base.ByteTransform.TransInt64(m, 0, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<ulong[]>> ReadUInt64Async(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, length), (byte[] m) => base.ByteTransform.TransUInt64(m, 0, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<float[]>> ReadFloatAsync(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, length), (byte[] m) => base.ByteTransform.TransSingle(m, 0, length));
		}

		/// <inheritdoc />
		public override async Task<OperateResult<double[]>> ReadDoubleAsync(string address, ushort length)
		{
			return ByteTransformHelper.GetResultFromBytes(await ReadAsync(address, length), (byte[] m) => base.ByteTransform.TransDouble(m, 0, length));
		}

		/// <summary>
		/// 是否需要创建节点信息
		/// </summary>
		/// <param name="address">标签地址</param>
		/// <returns>是否包含值</returns>
		protected internal bool IsNeedCreateTag(string address)
		{
			if (!CreateTagWithWrite)
			{
				return false;
			}
			if (Regex.IsMatch(address, "\\[[0-9]+\\]$"))
			{
				return false;
			}
			GetAddressIndex(ref address);
			simpleHybird.Enter();
			bool result = !abValues.ContainsKey(address);
			simpleHybird.Leave();
			return result;
		}

		/// <inheritdoc />
		[HslMqttApi("WriteBool", "")]
		public override OperateResult Write(string address, bool value)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, value);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, value);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteInt16", "")]
		public override OperateResult Write(string address, short value)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, value);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, value);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteInt16Array", "")]
		public override OperateResult Write(string address, short[] values)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, values);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, values);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteUInt16", "")]
		public override OperateResult Write(string address, ushort value)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, value);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, value);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteUInt16Array", "")]
		public override OperateResult Write(string address, ushort[] values)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, values);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, values);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteInt32", "")]
		public override OperateResult Write(string address, int value)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, value);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, value);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteInt32Array", "")]
		public override OperateResult Write(string address, int[] values)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, values);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, values);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteUInt32", "")]
		public override OperateResult Write(string address, uint value)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, value);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, value);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteUInt32Array", "")]
		public override OperateResult Write(string address, uint[] values)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, values);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, values);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteFloat", "")]
		public override OperateResult Write(string address, float value)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, value);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, value);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteFloatArray", "")]
		public override OperateResult Write(string address, float[] values)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, values);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, values);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteInt64", "")]
		public override OperateResult Write(string address, long value)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, value);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, value);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteInt64Array", "")]
		public override OperateResult Write(string address, long[] values)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, values);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, values);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteUInt64", "")]
		public override OperateResult Write(string address, ulong value)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, value);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, value);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteUInt64Array", "")]
		public override OperateResult Write(string address, ulong[] values)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, values);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, values);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteDouble", "")]
		public override OperateResult Write(string address, double value)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, value);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, value);
		}

		/// <inheritdoc />
		[HslMqttApi("WriteDoubleArray", "")]
		public override OperateResult Write(string address, double[] values)
		{
			if (IsNeedCreateTag(address))
			{
				AddTagValue(address, values);
				return OperateResult.CreateSuccessResult();
			}
			return base.Write(address, values);
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, bool value)
		{
			return await Task.Run(() => Write(address, value));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, short value)
		{
			return await Task.Run(() => Write(address, value));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, short[] values)
		{
			return await Task.Run(() => Write(address, values));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, ushort value)
		{
			return await Task.Run(() => Write(address, value));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, ushort[] values)
		{
			return await Task.Run(() => Write(address, values));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, int value)
		{
			return await Task.Run(() => Write(address, value));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, int[] values)
		{
			return await Task.Run(() => Write(address, values));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, uint value)
		{
			return await Task.Run(() => Write(address, value));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, uint[] values)
		{
			return await Task.Run(() => Write(address, values));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, float value)
		{
			return await Task.Run(() => Write(address, value));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, float[] values)
		{
			return await Task.Run(() => Write(address, values));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, long value)
		{
			return await Task.Run(() => Write(address, value));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, long[] values)
		{
			return await Task.Run(() => Write(address, values));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, ulong value)
		{
			return await Task.Run(() => Write(address, value));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, ulong[] values)
		{
			return await Task.Run(() => Write(address, values));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, double value)
		{
			return await Task.Run(() => Write(address, value));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, double[] values)
		{
			return await Task.Run(() => Write(address, values));
		}

		/// <inheritdoc />
		public override async Task<OperateResult> WriteAsync(string address, bool[] value)
		{
			return await Task.Run(() => Write(address, value));
		}

		/// <inheritdoc />
		protected override byte[] SaveToBytes()
		{
			XElement xElement = new XElement("Tags");
			foreach (KeyValuePair<string, AllenBradleyItemValue> abValue in abValues)
			{
				abValue.Value.Name = abValue.Key;
				xElement.Add(abValue.Value.ToXml());
			}
			return Encoding.UTF8.GetBytes(xElement.ToString());
		}

		/// <inheritdoc />
		protected override void LoadFromBytes(byte[] content)
		{
			abValues.Clear();
			XElement xElement = XElement.Parse(Encoding.UTF8.GetString(content));
			foreach (XElement item in xElement.Elements())
			{
				if (item.Name == "AllenBradleyItemValue")
				{
					AllenBradleyItemValue allenBradleyItemValue = new AllenBradleyItemValue(item);
					abValues.Add(allenBradleyItemValue.Name, allenBradleyItemValue);
				}
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"AllenBradleyServer[{base.Port}]";
		}
	}
}
