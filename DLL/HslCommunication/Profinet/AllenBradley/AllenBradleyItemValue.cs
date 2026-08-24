using System;
using System.Xml.Linq;

namespace HslCommunication.Profinet.AllenBradley
{
	/// <summary>
	/// AB PLC的标签节点数据信息
	/// </summary>
	public class AllenBradleyItemValue
	{
		/// <summary>
		/// 当前标签的名称信息
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// 真实的数组缓存
		/// </summary>
		public byte[] Buffer { get; set; }

		/// <summary>
		/// 是否是数组的数据
		/// </summary>
		public bool IsArray { get; set; }

		/// <summary>
		/// 单个单位的数据长度信息
		/// </summary>
		public int TypeLength { get; set; } = 1;


		/// <summary>
		/// 数据类型信息
		/// </summary>
		public ushort TypeCode { get; set; } = 193;


		/// <summary>
		/// 实例化一个默认的对象
		/// </summary>
		public AllenBradleyItemValue()
		{
		}

		/// <summary>
		/// 指定XML元素的资源来实例化对象信息
		/// </summary>
		/// <param name="element">XML元素信息</param>
		public AllenBradleyItemValue(XElement element)
		{
			LoadByXml(element);
		}

		/// <summary>
		/// 将值转换为同等描述的序列化字符串信息
		/// </summary>
		/// <returns>Xml元素</returns>
		public XElement ToXml()
		{
			XElement xElement = new XElement("AllenBradleyItemValue");
			xElement.SetAttributeValue("Name", Name);
			xElement.SetAttributeValue("TypeCode", TypeCode);
			xElement.SetAttributeValue("IsArray", IsArray);
			xElement.SetAttributeValue("TypeLength", TypeLength);
			if (Buffer != null)
			{
				xElement.SetAttributeValue("Buffer", Buffer.ToHexString());
			}
			return xElement;
		}

		private T GetXmlValue<T>(XElement element, string name, T defaultValue, Func<string, T> trans)
		{
			XAttribute xAttribute = element.Attribute(name);
			if (xAttribute == null)
			{
				return defaultValue;
			}
			try
			{
				return trans(xAttribute.Value);
			}
			catch
			{
				return defaultValue;
			}
		}

		/// <summary>
		/// 从xml元素加载当前的节点数据信息
		/// </summary>
		/// <param name="element">元素信息</param>
		public void LoadByXml(XElement element)
		{
			if (element.Name == "AllenBradleyItemValue")
			{
				Name = GetXmlValue(element, "Name", Name, (string m) => m);
				TypeCode = GetXmlValue(element, "TypeCode", TypeCode, ushort.Parse);
				IsArray = GetXmlValue(element, "IsArray", IsArray, bool.Parse);
				TypeLength = GetXmlValue(element, "TypeLength", TypeLength, int.Parse);
				Buffer = GetXmlValue(element, "Buffer", "", (string m) => m).ToHexBytes();
			}
		}
	}
}
