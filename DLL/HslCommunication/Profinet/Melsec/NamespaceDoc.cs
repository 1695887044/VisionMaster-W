using System.Runtime.CompilerServices;

namespace HslCommunication.Profinet.Melsec
{
	/// <summary>
	/// 在三菱的PLC通信的MC协议中，分为串行通信的报文和以太网接口的报文。<br />
	/// 在串口通信中，共有以下几种帧，其中1C,2C,3C帧支持格式1，2，3，4，在C帧里支持格式5通信<br />
	/// <list type="number">
	/// <item>4C帧，QnA系列串行通信模块专用协议（Qna扩展帧）</item>
	/// <item>3C帧，QnA系列串行通信模块专用协议（Qna帧）</item>
	/// <item>2C帧，QnA系列串行通信模块专用协议（Qna简易帧）</item>
	/// <item>1C帧，A系列计算机链接模块专用协议</item>
	/// </list>
	/// 在以太网通信中，共有以下几种帧，每种帧支持二进制和ASCII格式
	/// <list type="number">
	/// <item>4E帧，是3E帧上附加了“序列号”。</item>
	/// <item>3E帧，QnA系列以太网接口模块的报文格式，兼容SLMP的报文格式</item>
	/// <item>1E帧，A系列以太网接口模块的报文格式</item>
	/// </list>
	/// 在以太网通信里，HSL主要针对1E帧协议和3E帧协议进行实现，大概说一下怎么选择通信类对象，对于三菱PLC而言，需要事先在PLC侧的网络配置中进行
	/// 相关的配置操作，具体是配置二进制格式还是ASCII格式，然后配置端口，配置TCP还是UDP协议。<br />
	/// <see cref="T:HslCommunication.Profinet.Melsec.MelsecMcNet" />，<see cref="T:HslCommunication.Profinet.Melsec.MelsecMcAsciiNet" />，<see cref="T:HslCommunication.Profinet.Melsec.MelsecMcUdp" />, <see cref="T:HslCommunication.Profinet.Melsec.MelsecMcAsciiUdp" /> 这四个类都是MC协议的Qna兼容3E帧实现，分别
	/// 是TCP二进制，TCP的ASCII，UDP的二进制，UDP的ASCI格式。适用Q系列，L系列，FX5U系列，还有以太网模块QJ71E71。<br />
	/// <see cref="T:HslCommunication.Profinet.Melsec.MelsecA1ENet" />, <see cref="T:HslCommunication.Profinet.Melsec.MelsecA1EAsciiNet" /> 这两个类是MC协议的Qna兼容1E协议实现，
	/// 分别是二进制和ASCII格式的实现，主要适用A系列的PLC，Fx3u，以及有些老的PLC，使用了北辰模块实现的通信也是选择的是 A1E 协议来通信。<br />
	/// <see cref="T:HslCommunication.Profinet.Melsec.MelsecA3CNet" /> 是MC协议的3C帧的实现，主要支持串行通信的模块的实现。<br />
	/// <see cref="T:HslCommunication.Profinet.Melsec.MelsecFxSerial" /> 是FX编程口的协议的实现，测试并不在所有的老型号上都很稳定。具体支持的系列需要参照类的说明，
	/// 当使用GOT触摸屏连接PLC时，需要使用类<see cref="T:HslCommunication.Profinet.Melsec.MelsecFxSerialOverTcp" />并将属性<see cref="P:HslCommunication.Profinet.Melsec.MelsecFxSerialOverTcp.UseGOT" />设置为 <c>True</c>。<br />
	/// <see cref="T:HslCommunication.Profinet.Melsec.MelsecFxLinks" /> 是三菱的计算机链接协议，通常是三菱的485接口，而不是编程口，该协议支持多种格式及是否和校验，实际参数选择需要根据PLC的配置来决定。<br />
	/// <br />
	/// 三菱Q系列PLC带以太网模块，使用MC 二进制协议通讯，在网络中断情况下无法正常连接的情况，解决方案如下：<br />
	/// 1. 生存确认选择确认模式；<br />
	/// 2. 初始设置中将"对象目标生存确认开始间隔定时器"从1200改为12<br />
	/// </summary>
	[CompilerGenerated]
	public class NamespaceDoc
	{
	}
}
