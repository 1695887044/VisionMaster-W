using System;
using System.Text;

namespace HslCommunication.Profinet.IDCard
{
	/// <summary>
	/// 身份证的信息类
	/// </summary>
	public class IdentityCard
	{
		/// <summary>
		/// 名字
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// 性别
		/// </summary>
		public string Sex { get; set; }

		/// <summary>
		/// 身份证号
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// 民族
		/// </summary>
		public string Nation { get; set; }

		/// <summary>
		/// 生日
		/// </summary>
		public DateTime Birthday { get; set; }

		/// <summary>
		/// 地址
		/// </summary>
		public string Address { get; set; }

		/// <summary>
		/// 发证机关
		/// </summary>
		public string Organ { get; set; }

		/// <summary>
		/// 有效期日期的起始日期
		/// </summary>
		public DateTime ValidityStartDate { get; set; }

		/// <summary>
		/// 有效期日期的结束日期
		/// </summary>
		public DateTime ValidityEndDate { get; set; }

		/// <summary>
		/// 头像信息
		/// </summary>
		public byte[] Portrait { get; set; }

		/// <summary>
		/// 返回表示当前对象的字符串
		/// </summary>
		/// <returns>字符串</returns>
		public override string ToString()
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("姓名：" + Name);
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("性别：" + Sex);
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("民族：" + Nation);
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("身份证号：" + Id);
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append($"出身日期：{Birthday.Year}年{Birthday.Month}月{Birthday.Day}日");
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("地址：" + Address);
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append("发证机关：" + Organ);
			stringBuilder.Append(Environment.NewLine);
			stringBuilder.Append($"有效日期：{ValidityStartDate.Year}年{ValidityStartDate.Month}月{ValidityStartDate.Day}日 - {ValidityEndDate.Year}年{ValidityEndDate.Month}月{ValidityEndDate.Day}日");
			stringBuilder.Append(Environment.NewLine);
			return stringBuilder.ToString();
		}
	}
}
