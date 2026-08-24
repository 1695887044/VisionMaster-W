namespace HslCommunication.Profinet.Keyence
{
	/// <summary>
	/// Keyence PLC的数据类型，此处包含了几个常用的类型
	/// </summary>
	public class KeyenceDataType
	{
		/// <summary>
		/// X输入继电器
		/// </summary>
		public static readonly KeyenceDataType X = new KeyenceDataType(156, 1, "X*", 16);

		/// <summary>
		/// Y输出继电器
		/// </summary>
		public static readonly KeyenceDataType Y = new KeyenceDataType(157, 1, "Y*", 16);

		/// <summary>
		/// 链接继电器
		/// </summary>
		public static readonly KeyenceDataType B = new KeyenceDataType(160, 1, "B*", 16);

		/// <summary>
		/// 内部辅助继电器
		/// </summary>
		public static readonly KeyenceDataType M = new KeyenceDataType(144, 1, "M*", 10);

		/// <summary>
		/// 锁存继电器
		/// </summary>
		public static readonly KeyenceDataType L = new KeyenceDataType(146, 1, "L*", 10);

		/// <summary>
		/// 控制继电器
		/// </summary>
		public static readonly KeyenceDataType SM = new KeyenceDataType(145, 1, "SM", 10);

		/// <summary>
		/// 控制存储器
		/// </summary>
		public static readonly KeyenceDataType SD = new KeyenceDataType(169, 0, "SD", 10);

		/// <summary>
		/// 数据存储器
		/// </summary>
		public static readonly KeyenceDataType D = new KeyenceDataType(168, 0, "D*", 10);

		/// <summary>
		/// 文件寄存器
		/// </summary>
		public static readonly KeyenceDataType R = new KeyenceDataType(175, 0, "R*", 10);

		/// <summary>
		/// 文件寄存器
		/// </summary>
		public static readonly KeyenceDataType ZR = new KeyenceDataType(176, 0, "ZR", 16);

		/// <summary>
		/// 链路寄存器
		/// </summary>
		public static readonly KeyenceDataType W = new KeyenceDataType(180, 0, "W*", 16);

		/// <summary>
		/// 计时器（当前值）
		/// </summary>
		public static readonly KeyenceDataType TN = new KeyenceDataType(194, 0, "TN", 10);

		/// <summary>
		/// 计时器（接点）
		/// </summary>
		public static readonly KeyenceDataType TS = new KeyenceDataType(193, 1, "TS", 10);

		/// <summary>
		/// 计数器（当前值）
		/// </summary>
		public static readonly KeyenceDataType CN = new KeyenceDataType(197, 0, "CN", 10);

		/// <summary>
		/// 计数器（接点）
		/// </summary>
		public static readonly KeyenceDataType CS = new KeyenceDataType(196, 1, "CS", 10);

		/// <summary>
		/// 类型的代号值
		/// </summary>
		public byte DataCode { get; private set; } = 0;


		/// <summary>
		/// 数据的类型，0代表按字，1代表按位
		/// </summary>
		public byte DataType { get; private set; } = 0;


		/// <summary>
		/// 当以ASCII格式通讯时的类型描述
		/// </summary>
		public string AsciiCode { get; private set; }

		/// <summary>
		/// 指示地址是10进制，还是16进制的
		/// </summary>
		public int FromBase { get; private set; }

		/// <summary>
		/// 如果您清楚类型代号，可以根据值进行扩展
		/// </summary>
		/// <param name="code">数据类型的代号</param>
		/// <param name="type">0或1，默认为0</param>
		/// <param name="asciiCode">ASCII格式的类型信息</param>
		/// <param name="fromBase">指示地址的多少进制的，10或是16</param>
		public KeyenceDataType(byte code, byte type, string asciiCode, int fromBase)
		{
			DataCode = code;
			AsciiCode = asciiCode;
			FromBase = fromBase;
			if (type < 2)
			{
				DataType = type;
			}
		}
	}
}
