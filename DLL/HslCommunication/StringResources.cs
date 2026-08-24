using System.Globalization;
using HslCommunication.Language;

namespace HslCommunication
{
	/// <summary>
	/// 系统的字符串资源及多语言管理中心<br />
	/// System string resource and multi-language management Center
	/// </summary>
	public static class StringResources
	{
		/// <summary>
		/// 获取或设置系统的语言选项<br />
		/// Gets or sets the language options for the system
		/// </summary>
		public static DefaultLanguage Language;

		static StringResources()
		{
			Language = new DefaultLanguage();
			if (CultureInfo.CurrentCulture.ToString().StartsWith("zh"))
			{
				SetLanguageChinese();
			}
			else
			{
				SeteLanguageEnglish();
			}
		}

		/// <summary>
		/// 将语言设置为中文<br />
		/// Set the language to Chinese
		/// </summary>
		public static void SetLanguageChinese()
		{
			Language = new DefaultLanguage();
		}

		/// <summary>
		/// 将语言设置为英文<br />
		/// Set the language to English
		/// </summary>
		public static void SeteLanguageEnglish()
		{
			Language = new English();
		}
	}
}
