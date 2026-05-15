using System;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace HslCommunication.BasicFramework
{
	public class SoftAuthorize : SoftFileSaveBase
	{
		public static readonly string TextCode = "Code";

		private string machine_code = "";

		public string FinalCode { get; private set; } = "";

		public bool IsReleaseVersion { get; set; } = false;

		public bool ContainsHardDiskInformation { get; set; } = true;

		private bool HasLoadByFile { get; set; } = false;

		public bool IsSoftTrial { get; set; } = false;

		public SoftAuthorize(bool UseAdmin = false, bool useHDD = true)
		{
			machine_code = "NO_HARDWARE_INFO";
			base.LogHeaderText = "SoftAuthorize";
		}

		public string GetMachineCodeString()
		{
			return machine_code;
		}

		public override string ToSaveString()
		{
			JObject jObject = new JObject { 
			{
				TextCode,
				new JValue(FinalCode)
			} };
			return jObject.ToString();
		}

		public override void LoadByString(string content)
		{
			JObject json = JObject.Parse(content);
			if (json.Property(TextCode) != null)
			{
				FinalCode = json.Property(TextCode).Value.Value<string>();
			}
			HasLoadByFile = true;
		}

		public override void SaveToFile()
		{
			SaveToFile((string m) => SoftSecurity.MD5Encrypt(m));
		}

		public override void LoadByFile()
		{
			LoadByFile((string m) => SoftSecurity.MD5Decrypt(m));
		}

		public bool CheckAuthorize(string code, Func<string, string> encrypt)
		{
			FinalCode = code;
			SaveToFile();
			return true;
		}

		public bool IsAuthorizeSuccess(Func<string, string> encrypt)
		{
			if (IsReleaseVersion)
			{
				return true;
			}
			if (encrypt(GetMachineCodeString()) == FinalCode)
			{
				return true;
			}
			FinalCode = "";
			SaveToFile();
			return false;
		}
	}
}