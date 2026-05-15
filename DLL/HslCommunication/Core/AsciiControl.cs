namespace HslCommunication.Core
{
	/// <summary>
	/// 控制型的ascii资源信息<br />
	/// Controlled ascii resource information
	/// </summary>
	public class AsciiControl
	{
		/// <summary>
		/// 空字符
		/// </summary>
		public const byte NUL = 0;

		/// <summary>
		/// 标题开始<br />
		/// start of headling
		/// </summary>
		public const byte SOH = 1;

		/// <summary>
		/// 正文开始<br />
		/// start of text
		/// </summary>
		public const byte STX = 2;

		/// <summary>
		/// 正文结束<br />
		/// end of text
		/// </summary>
		public const byte ETX = 3;

		/// <summary>
		/// 传输结束<br />
		/// end of transmission
		/// </summary>
		public const byte EOT = 4;

		/// <summary>
		/// 请求<br />
		/// enquiry
		/// </summary>
		public const byte ENQ = 5;

		/// <summary>
		/// 接到通知<br />
		/// acknowledge
		/// </summary>
		public const byte ACK = 6;

		/// <summary>
		/// 响铃<br />
		/// bell
		/// </summary>
		public const byte BEL = 7;

		/// <summary>
		/// 退格<br />
		/// backspace
		/// </summary>
		public const byte BS = 8;

		/// <summary>
		/// 水平制表符<br />
		/// horizontal tab
		/// </summary>
		public const byte HT = 9;

		/// <summary>
		/// 换行符<br />
		/// NL line feed, new line
		/// </summary>
		public const byte LF = 10;

		/// <summary>
		/// 垂直制表符<br />
		/// vertical tab
		/// </summary>
		public const byte VT = 11;

		/// <summary>
		/// 换页键<br />
		/// NP form feed, new page
		/// </summary>
		public const byte FF = 12;

		/// <summary>
		/// 回车键<br />
		/// carriage return
		/// </summary>
		public const byte CR = 13;

		/// <summary>
		/// 不用切换<br />
		/// shift out
		/// </summary>
		public const byte SO = 14;

		/// <summary>
		/// 启用切换<br />
		/// shift in
		/// </summary>
		public const byte SI = 15;

		/// <summary>
		/// 数据链路定义<br />
		/// data link escape
		/// </summary>
		public const byte DLE = 16;

		/// <summary>
		/// 设备控制1<br />
		/// device control 1
		/// </summary>
		public const byte DC1 = 17;

		/// <summary>
		/// 设备控制2<br />
		/// device control 2
		/// </summary>
		public const byte DC2 = 18;

		/// <summary>
		/// 设备控制3<br />
		/// device control 3
		/// </summary>
		public const byte DC3 = 19;

		/// <summary>
		/// 设备控制4<br />
		/// device control 4
		/// </summary>
		public const byte DC4 = 20;

		/// <summary>
		/// 拒绝接收<br />
		/// negative acknowledge
		/// </summary>
		public const byte NAK = 21;

		/// <summary>
		/// 同步空闲<br />
		/// synchronous idle
		/// </summary>
		public const byte SYN = 22;

		/// <summary>
		/// 传输块结束<br />
		/// end of trans. block
		/// </summary>
		public const byte ETB = 23;

		/// <summary>
		/// 取消<br />
		/// cancel
		/// </summary>
		public const byte CAN = 24;

		/// <summary>
		/// 介质中断<br />
		/// end of medium
		/// </summary>
		public const byte EM = 25;

		/// <summary>
		/// 替补<br />
		/// substitute
		/// </summary>
		public const byte SUB = 26;

		/// <summary>
		/// 溢出<br />
		/// escape
		/// </summary>
		public const byte ESC = 27;

		/// <summary>
		/// 文件分隔符<br />
		/// file separator
		/// </summary>
		public const byte FS = 28;

		/// <summary>
		/// 分组符<br />
		/// group separator
		/// </summary>
		public const byte GS = 29;

		/// <summary>
		/// 记录分离符<br />
		/// record separator
		/// </summary>
		public const byte RS = 30;

		/// <summary>
		/// 单元分隔符<br />
		/// unit separator
		/// </summary>
		public const byte US = 31;
	}
}
