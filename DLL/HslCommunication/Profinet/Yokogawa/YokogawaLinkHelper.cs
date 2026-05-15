namespace HslCommunication.Profinet.Yokogawa
{
	/// <summary>
	/// 横河PLC的通信辅助类。
	/// </summary>
	public class YokogawaLinkHelper
	{
		/// <summary>
		/// 获取横河PLC的错误的具体描述信息
		/// </summary>
		/// <param name="code">错误码</param>
		/// <returns>错误的描述信息</returns>
		public static string GetErrorMsg(byte code)
		{
			return code switch
			{
				1 => StringResources.Language.YokogawaLinkError01, 
				2 => StringResources.Language.YokogawaLinkError02, 
				3 => StringResources.Language.YokogawaLinkError03, 
				4 => StringResources.Language.YokogawaLinkError04, 
				5 => StringResources.Language.YokogawaLinkError05, 
				6 => StringResources.Language.YokogawaLinkError06, 
				7 => StringResources.Language.YokogawaLinkError07, 
				8 => StringResources.Language.YokogawaLinkError08, 
				65 => StringResources.Language.YokogawaLinkError41, 
				66 => StringResources.Language.YokogawaLinkError42, 
				67 => StringResources.Language.YokogawaLinkError43, 
				68 => StringResources.Language.YokogawaLinkError44, 
				81 => StringResources.Language.YokogawaLinkError51, 
				82 => StringResources.Language.YokogawaLinkError52, 
				241 => StringResources.Language.YokogawaLinkErrorF1, 
				_ => StringResources.Language.UnknownError, 
			};
		}
	}
}
