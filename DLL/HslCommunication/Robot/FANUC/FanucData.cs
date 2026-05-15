using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HslCommunication.BasicFramework;
using HslCommunication.Core;

namespace HslCommunication.Robot.FANUC
{
	/// <summary>
	/// Fanuc机器人的所有的数据信息
	/// </summary>
	public class FanucData
	{
		private bool isIni = false;

		public FanucAlarm[] AlarmList { get; set; }

		public FanucAlarm AlarmCurrent { get; set; }

		public FanucAlarm AlarmPassword { get; set; }

		public FanucPose CurrentPose { get; set; }

		public FanucPose CurrentPoseUF { get; set; }

		public FanucPose CurrentPose2 { get; set; }

		public FanucPose CurrentPose3 { get; set; }

		public FanucPose CurrentPose4 { get; set; }

		public FanucPose CurrentPose5 { get; set; }

		public FanucTask Task { get; set; }

		public FanucTask TaskIgnoreMacro { get; set; }

		public FanucTask TaskIgnoreKarel { get; set; }

		public FanucTask TaskIgnoreMacroKarel { get; set; }

		public FanucPose[] PosRegGP1 { get; set; }

		public FanucPose[] PosRegGP2 { get; set; }

		public FanucPose[] PosRegGP3 { get; set; }

		public FanucPose[] PosRegGP4 { get; set; }

		public FanucPose[] PosRegGP5 { get; set; }

		public int FAST_CLOCK { get; set; }

		public int Timer10_TIMER_VAL { get; set; }

		public float MOR_GRP_CURRENT_ANG { get; set; }

		public float DUTY_TEMP { get; set; }

		public string TIMER10_COMMENT { get; set; }

		public string TIMER2_COMMENT { get; set; }

		public FanucPose MNUTOOL1_1 { get; set; }

		public string HTTPKCL_CMDS { get; set; }

		public int[] NumReg1 { get; set; }

		public float[] NumReg2 { get; set; }

		public FanucPose[] DataPosRegMG { get; set; }

		public string[] DIComment { get; set; }

		public string[] DOComment { get; set; }

		public string[] RIComment { get; set; }

		public string[] ROComment { get; set; }

		public string[] UIComment { get; set; }

		public string[] UOComment { get; set; }

		public string[] SIComment { get; set; }

		public string[] SOComment { get; set; }

		public string[] WIComment { get; set; }

		public string[] WOComment { get; set; }

		public string[] WSIComment { get; set; }

		public string[] AIComment { get; set; }

		public string[] AOComment { get; set; }

		public string[] GIComment { get; set; }

		public string[] GOComment { get; set; }

		public string[] STRREGComment { get; set; }

		public string[] STRREG_COMMENT_Comment { get; set; }

		/// <summary>
		/// 从原始的数据内容加载数据
		/// </summary>
		/// <param name="content">原始的内容</param>
		/// <param name="encoding">编码信息</param>
		public void LoadByContent(byte[] content, Encoding encoding)
		{
			IByteTransform byteTransform = new RegularByteTransform();
			string[] fanucCmds = FanucHelper.GetFanucCmds();
			int[] array = new int[fanucCmds.Length - 1];
			int[] array2 = new int[fanucCmds.Length - 1];
			for (int i = 1; i < fanucCmds.Length; i++)
			{
				MatchCollection matchCollection = Regex.Matches(fanucCmds[i], "[0-9]+");
				array[i - 1] = (int.Parse(matchCollection[0].Value) - 1) * 2;
				array2[i - 1] = int.Parse(matchCollection[1].Value) * 2;
			}
			AlarmList = GetFanucAlarmArray(byteTransform, content, array[0], 5, encoding);
			AlarmCurrent = FanucAlarm.PraseFrom(byteTransform, content, array[1], encoding);
			AlarmPassword = FanucAlarm.PraseFrom(byteTransform, content, array[2], encoding);
			CurrentPose = FanucPose.ParseFrom(byteTransform, content, array[3]);
			CurrentPoseUF = FanucPose.ParseFrom(byteTransform, content, array[4]);
			CurrentPose2 = FanucPose.ParseFrom(byteTransform, content, array[5]);
			CurrentPose3 = FanucPose.ParseFrom(byteTransform, content, array[6]);
			CurrentPose4 = FanucPose.ParseFrom(byteTransform, content, array[7]);
			CurrentPose5 = FanucPose.ParseFrom(byteTransform, content, array[8]);
			Task = FanucTask.ParseFrom(byteTransform, content, array[9], encoding);
			TaskIgnoreMacro = FanucTask.ParseFrom(byteTransform, content, array[10], encoding);
			TaskIgnoreKarel = FanucTask.ParseFrom(byteTransform, content, array[11], encoding);
			TaskIgnoreMacroKarel = FanucTask.ParseFrom(byteTransform, content, array[12], encoding);
			PosRegGP1 = GetFanucPoseArray(byteTransform, content, array[13], 10, encoding);
			PosRegGP2 = GetFanucPoseArray(byteTransform, content, array[14], 4, encoding);
			PosRegGP3 = GetFanucPoseArray(byteTransform, content, array[15], 10, encoding);
			PosRegGP4 = GetFanucPoseArray(byteTransform, content, array[16], 10, encoding);
			PosRegGP5 = GetFanucPoseArray(byteTransform, content, array[17], 10, encoding);
			FAST_CLOCK = BitConverter.ToInt32(content, array[18]);
			Timer10_TIMER_VAL = BitConverter.ToInt32(content, array[19]);
			MOR_GRP_CURRENT_ANG = BitConverter.ToSingle(content, array[20]);
			DUTY_TEMP = BitConverter.ToSingle(content, array[21]);
			TIMER10_COMMENT = encoding.GetString(content, array[22], 80).Trim(default(char));
			TIMER2_COMMENT = encoding.GetString(content, array[23], 80).Trim(default(char));
			MNUTOOL1_1 = FanucPose.ParseFrom(byteTransform, content, array[24]);
			HTTPKCL_CMDS = encoding.GetString(content, array[25], 80).Trim(default(char));
			NumReg1 = byteTransform.TransInt32(content, array[26], 5);
			NumReg2 = byteTransform.TransSingle(content, array[27], 5);
			DataPosRegMG = new FanucPose[10];
			for (int j = 0; j < DataPosRegMG.Length; j++)
			{
				DataPosRegMG[j] = new FanucPose();
				DataPosRegMG[j].Xyzwpr = byteTransform.TransSingle(content, array[29] + j * 50, 9);
				DataPosRegMG[j].Config = FanucPose.TransConfigStringArray(byteTransform.TransInt16(content, array[29] + 36 + j * 50, 7));
				DataPosRegMG[j].Joint = byteTransform.TransSingle(content, array[30] + j * 36, 9);
			}
			DIComment = GetStringArray(content, array[31], 80, 3, encoding);
			DOComment = GetStringArray(content, array[32], 80, 3, encoding);
			RIComment = GetStringArray(content, array[33], 80, 3, encoding);
			ROComment = GetStringArray(content, array[34], 80, 3, encoding);
			UIComment = GetStringArray(content, array[35], 80, 3, encoding);
			UOComment = GetStringArray(content, array[36], 80, 3, encoding);
			SIComment = GetStringArray(content, array[37], 80, 3, encoding);
			SOComment = GetStringArray(content, array[38], 80, 3, encoding);
			WIComment = GetStringArray(content, array[39], 80, 3, encoding);
			WOComment = GetStringArray(content, array[40], 80, 3, encoding);
			WSIComment = GetStringArray(content, array[41], 80, 3, encoding);
			AIComment = GetStringArray(content, array[42], 80, 3, encoding);
			AOComment = GetStringArray(content, array[43], 80, 3, encoding);
			GIComment = GetStringArray(content, array[44], 80, 3, encoding);
			GOComment = GetStringArray(content, array[45], 80, 3, encoding);
			STRREGComment = GetStringArray(content, array[46], 80, 3, encoding);
			STRREG_COMMENT_Comment = GetStringArray(content, array[47], 80, 3, encoding);
			isIni = true;
		}

		/// <inheritdoc />
		public override string ToString()
		{
			if (!isIni)
			{
				return "NULL";
			}
			StringBuilder stringBuilder = new StringBuilder();
			AppendStringBuilder(stringBuilder, "AlarmList", AlarmList.Select((FanucAlarm m) => m.ToString()).ToArray());
			AppendStringBuilder(stringBuilder, "AlarmCurrent", AlarmCurrent.ToString());
			AppendStringBuilder(stringBuilder, "AlarmPassword", AlarmPassword.ToString());
			AppendStringBuilder(stringBuilder, "CurrentPose", CurrentPose.ToString());
			AppendStringBuilder(stringBuilder, "CurrentPoseUF", CurrentPoseUF.ToString());
			AppendStringBuilder(stringBuilder, "CurrentPose2", CurrentPose2.ToString());
			AppendStringBuilder(stringBuilder, "CurrentPose3", CurrentPose3.ToString());
			AppendStringBuilder(stringBuilder, "CurrentPose4", CurrentPose4.ToString());
			AppendStringBuilder(stringBuilder, "CurrentPose5", CurrentPose5.ToString());
			AppendStringBuilder(stringBuilder, "Task", Task.ToString());
			AppendStringBuilder(stringBuilder, "TaskIgnoreMacro", TaskIgnoreMacro.ToString());
			AppendStringBuilder(stringBuilder, "TaskIgnoreKarel", TaskIgnoreKarel.ToString());
			AppendStringBuilder(stringBuilder, "TaskIgnoreMacroKarel", TaskIgnoreMacroKarel.ToString());
			AppendStringBuilder(stringBuilder, "PosRegGP1", PosRegGP1.Select((FanucPose m) => m.ToString()).ToArray());
			AppendStringBuilder(stringBuilder, "PosRegGP2", PosRegGP2.Select((FanucPose m) => m.ToString()).ToArray());
			AppendStringBuilder(stringBuilder, "PosRegGP3", PosRegGP3.Select((FanucPose m) => m.ToString()).ToArray());
			AppendStringBuilder(stringBuilder, "PosRegGP4", PosRegGP4.Select((FanucPose m) => m.ToString()).ToArray());
			AppendStringBuilder(stringBuilder, "PosRegGP5", PosRegGP5.Select((FanucPose m) => m.ToString()).ToArray());
			AppendStringBuilder(stringBuilder, "FAST_CLOCK", FAST_CLOCK.ToString());
			AppendStringBuilder(stringBuilder, "Timer10_TIMER_VAL", Timer10_TIMER_VAL.ToString());
			AppendStringBuilder(stringBuilder, "MOR_GRP_CURRENT_ANG", MOR_GRP_CURRENT_ANG.ToString());
			AppendStringBuilder(stringBuilder, "DUTY_TEMP", DUTY_TEMP.ToString());
			AppendStringBuilder(stringBuilder, "TIMER10_COMMENT", TIMER10_COMMENT.ToString());
			AppendStringBuilder(stringBuilder, "TIMER2_COMMENT", TIMER2_COMMENT.ToString());
			AppendStringBuilder(stringBuilder, "MNUTOOL1_1", MNUTOOL1_1.ToString());
			AppendStringBuilder(stringBuilder, "HTTPKCL_CMDS", HTTPKCL_CMDS.ToString());
			AppendStringBuilder(stringBuilder, "NumReg1", SoftBasic.ArrayFormat(NumReg1));
			AppendStringBuilder(stringBuilder, "NumReg2", SoftBasic.ArrayFormat(NumReg2));
			AppendStringBuilder(stringBuilder, "DataPosRegMG", DataPosRegMG.Select((FanucPose m) => m.ToString()).ToArray());
			AppendStringBuilder(stringBuilder, "DIComment", SoftBasic.ArrayFormat(DIComment));
			AppendStringBuilder(stringBuilder, "DOComment", SoftBasic.ArrayFormat(DOComment));
			AppendStringBuilder(stringBuilder, "RIComment", SoftBasic.ArrayFormat(RIComment));
			AppendStringBuilder(stringBuilder, "ROComment", SoftBasic.ArrayFormat(ROComment));
			AppendStringBuilder(stringBuilder, "UIComment", SoftBasic.ArrayFormat(UIComment));
			AppendStringBuilder(stringBuilder, "UOComment", SoftBasic.ArrayFormat(UOComment));
			AppendStringBuilder(stringBuilder, "SIComment", SoftBasic.ArrayFormat(SIComment));
			AppendStringBuilder(stringBuilder, "SOComment", SoftBasic.ArrayFormat(SOComment));
			AppendStringBuilder(stringBuilder, "WIComment", SoftBasic.ArrayFormat(WIComment));
			AppendStringBuilder(stringBuilder, "WOComment", SoftBasic.ArrayFormat(WOComment));
			AppendStringBuilder(stringBuilder, "WSIComment", SoftBasic.ArrayFormat(WSIComment));
			AppendStringBuilder(stringBuilder, "AIComment", SoftBasic.ArrayFormat(AIComment));
			AppendStringBuilder(stringBuilder, "AOComment", SoftBasic.ArrayFormat(AOComment));
			AppendStringBuilder(stringBuilder, "GIComment", SoftBasic.ArrayFormat(GIComment));
			AppendStringBuilder(stringBuilder, "GOComment", SoftBasic.ArrayFormat(GOComment));
			AppendStringBuilder(stringBuilder, "STRREGComment", SoftBasic.ArrayFormat(STRREGComment));
			AppendStringBuilder(stringBuilder, "STRREG_COMMENT_Comment", SoftBasic.ArrayFormat(STRREG_COMMENT_Comment));
			return stringBuilder.ToString();
		}

		/// <summary>
		/// 从字节数组解析出fanuc的数据信息
		/// </summary>
		/// <param name="content">原始的字节数组</param>
		/// <param name="encoding">字符串数据解析的编码信息</param>
		/// <returns>fanuc数据</returns>
		public static OperateResult<FanucData> PraseFrom(byte[] content, Encoding encoding)
		{
			FanucData fanucData = new FanucData();
			fanucData.LoadByContent(content, encoding);
			return OperateResult.CreateSuccessResult(fanucData);
		}

		private static void AppendStringBuilder(StringBuilder sb, string name, string value)
		{
			AppendStringBuilder(sb, name, new string[1] { value });
		}

		private static void AppendStringBuilder(StringBuilder sb, string name, string[] values)
		{
			sb.Append(name);
			sb.Append(":");
			if (values.Length > 1)
			{
				sb.Append(Environment.NewLine);
			}
			for (int i = 0; i < values.Length; i++)
			{
				sb.Append(values[i]);
				sb.Append(Environment.NewLine);
			}
			if (values.Length > 1)
			{
				sb.Append(Environment.NewLine);
			}
		}

		private static string[] GetStringArray(byte[] content, int index, int length, int arraySize, Encoding encoding)
		{
			string[] array = new string[arraySize];
			for (int i = 0; i < arraySize; i++)
			{
				array[i] = encoding.GetString(content, index + length * i, length).TrimEnd(default(char));
			}
			return array;
		}

		private static FanucPose[] GetFanucPoseArray(IByteTransform byteTransform, byte[] content, int index, int arraySize, Encoding encoding)
		{
			FanucPose[] array = new FanucPose[arraySize];
			for (int i = 0; i < arraySize; i++)
			{
				array[i] = FanucPose.ParseFrom(byteTransform, content, index + i * 100);
			}
			return array;
		}

		private static FanucAlarm[] GetFanucAlarmArray(IByteTransform byteTransform, byte[] content, int index, int arraySize, Encoding encoding)
		{
			FanucAlarm[] array = new FanucAlarm[arraySize];
			for (int i = 0; i < arraySize; i++)
			{
				array[i] = FanucAlarm.PraseFrom(byteTransform, content, index + 200 * i, encoding);
			}
			return array;
		}
	}
}
