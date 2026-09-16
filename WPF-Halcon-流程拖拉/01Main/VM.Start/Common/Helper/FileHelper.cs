using System;
using System.IO;
using System.Text;
using VM.Start.Common.Provide;

namespace VM.Start.Common.Helper
{
    public class FileHelper
    {
        /// <summary>
        /// 写入string数组到txt文件中
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="fileName">文件名称</param>
        /// <param name="Strs">字符串</param>
        public static void WriteTxtToFile(string path, string fileName, string[] strs)
        {
            try
            {
                string fileFullName = Path.Combine(path,fileName);
                if (!File.Exists(fileFullName)) return;
                using (StreamWriter sw = new StreamWriter(fileFullName))
                {
                    foreach (string s in strs)
                    {
                        sw.WriteLine(s);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.GetExceptionMsg(ex);
            }
        }
        public static string ReadContentFromTxt(string fileFullName)
        {
            string str = "";
            try
            {
                if (!File.Exists(fileFullName)) return str;
                using (StreamReader sr = new StreamReader(fileFullName, Encoding.Default))
                {
                    str = sr.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                Logger.GetExceptionMsg(ex);
            }
            return str;
        }
    }
}
