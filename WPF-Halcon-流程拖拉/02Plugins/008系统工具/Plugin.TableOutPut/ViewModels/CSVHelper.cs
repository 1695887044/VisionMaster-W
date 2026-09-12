using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Plugin.TableOutPut.ViewModels
{
    public class CSVHelper
    {

        private static string _fileName;

        public static string fileName { get => _fileName; set => _fileName = value; }

        /// <summary>
        /// 写入CSV
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="dt">要写入的datatable</param>
        public static void WriteCSV(DataTable dt, bool isAppend = false)
        {
            FileStream fs;
            StreamWriter sw;

            bool exit = false;

            FileInfo Info = new FileInfo(fileName);
            exit = Info.Exists;

            string data = null;

            if ((isAppend) && (exit))
            {
                fs = new FileStream(fileName, FileMode.Append, FileAccess.Write);
                sw = new StreamWriter(fs, Encoding.UTF8);
            }

            else
            {
                fs = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.Write);
                sw = new StreamWriter(fs, Encoding.UTF8);

                //写出列名称
                for (int i = 0; i < dt.Columns.Count; i++)
                {
                    data += dt.Columns[i].ColumnName.ToString();
                    if (i < dt.Columns.Count - 1)
                    {
                        data += ",";//中间用，隔开
                    }
                }
                sw.WriteLine(data);

            }





            //写出各行数据
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                data = null;
                for (int j = 0; j < dt.Columns.Count; j++)
                {
                    data += dt.Rows[i][j].ToString();
                    if (j < dt.Columns.Count - 1)
                    {
                        data += ",";//中间用，隔开
                    }
                }
                if (data.Split(',')[0] != "")
                {
                    sw.WriteLine(data);
                }
            }
            sw.Close();
            fs.Close();
        }


        /// <summary>
        /// 读取CSV文件
        /// </summary>
        /// <param name="fileName">文件路径</param>
        public static DataTable ReadCSV()
        {
            if (File.Exists(fileName) == false)
                return null;
            DataTable dt = new DataTable();
            FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            StreamReader sr = new StreamReader(fs, Encoding.UTF8);

            //记录每次读取的一行记录
            string strLine = null;
            //记录每行记录中的各字段内容
            string[] arrayLine = null;
            //分隔符
            string[] separators = { "," };
            //判断，若是第一次，建立表头
            bool isFirst = true;

            //逐行读取CSV文件
            while ((strLine = sr.ReadLine()) != null)
            {
                if (strLine.Split(',')[0] == "")
                    break;
                strLine = strLine.Trim();//去除头尾空格
                arrayLine = strLine.Split(separators, StringSplitOptions.RemoveEmptyEntries);//分隔字符串，返回数组
                int dtColumns = arrayLine.Length;//列的个数

                if (isFirst)  //建立表头
                {
                    for (int i = 0; i < dtColumns; i++)
                    {
                        dt.Columns.Add(arrayLine[i]);//每一列名称
                    }
                    isFirst = false;
                }
                else   //表内容
                {
                    DataRow dataRow = dt.NewRow();//新建一行
                    for (int j = 0; j < dtColumns; j++)
                    {

                        dataRow[j] = arrayLine[j];
                    }
                    dt.Rows.Add(dataRow);//添加一行
                }
            }
            sr.Close();
            fs.Close();

            return dt;
        }



        public static DataTable GetDgvToTable(DataGridView dgv)
        {
            DataTable dt = new DataTable();

            // 列强制转换
            for (int count = 0; count < dgv.Columns.Count; count++)
            {
                DataColumn dc = new DataColumn(dgv.Columns[count].Name.ToString());
                dt.Columns.Add(dc);
            }

            // 循环行
            for (int count = 0; count < dgv.Rows.Count; count++)
            {
                DataRow dr = dt.NewRow();
                for (int countsub = 0; countsub < dgv.Columns.Count; countsub++)
                {
                    dr[countsub] = Convert.ToString(dgv.Rows[count].Cells[countsub].Value);
                }
                dt.Rows.Add(dr);
            }
            return dt;
        }


        ///// <summary>
        ///// 写入CSV
        ///// </summary>
        ///// <param name="fileName">文件名</param>
        ///// <param name="dt">要写入的datatable</param>
        //public static void WriteTraceCSV()
        //{
        //    string rootPath = MesTempPara.Instance.TraceDataRootPath;

        //    if (Directory.Exists(rootPath) == false)
        //    {
        //        Directory.CreateDirectory(rootPath);
        //    }

        //    string path = rootPath + "//" + System.DateTime.Now.ToString("yyyyMMdd") + ".csv";
        //    bool isWriteTitle = false;
        //    if (File.Exists(path) == false)
        //    {
        //        isWriteTitle = true;
        //    }
        //    else
        //    {
        //        isWriteTitle = false;
        //    }

        //    //FileStream fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write);
        //    StreamWriter sw = new StreamWriter(path, true,Encoding.UTF8);
        //    StringBuilder sb = new StringBuilder();
        //    if (isWriteTitle) //写表头
        //    {
        //        sb.Append("物料B/C,");
        //        sb.Append("日期,");
        //        sb.Append("点胶机排胶参数,");
        //        sb.Append("胶水码,");
        //        sb.Append("胶水有效期,");
        //        sb.Append("胶水出冰箱时间,");
        //        sb.Append("首件胶重,");
        //        sb.Append("胶水已点数量,");
        //        sb.Append("物料清洗时间,");
        //        sb.Append("组立件治具B/C,");
        //        sb.Append("组立件清洗时间,");
        //        sb.Append("点胶开始时间,");
        //        sb.Append("组装开始时间,");
        //        sb.Append("OpenTime,");
        //        sb.Append("SPC_K_L1,");
        //        sb.Append("SPC_K_L2,");
        //        sb.Append("SPC_K_L3,");
        //        sb.Append("SPC_K_L4,");
        //        sb.Append("SPC_I_L,");
        //        sb.Append("SPC_L_L5,");
        //        sb.Append("SPC_L_L6,");
        //        sb.Append("SPC_L_L7,");
        //        sb.Append("SPC_L_L8,");
        //        sb.Append("SPC_J_L,");

        //        sb.Append("SPC_K_R1,");
        //        sb.Append("SPC_K_R2,");
        //        sb.Append("SPC_K_R3,");
        //        sb.Append("SPC_K_R4,");
        //        sb.Append("SPC_I_R,");
        //        sb.Append("SPC_L_R5,");
        //        sb.Append("SPC_L_R6,");
        //        sb.Append("SPC_L_R7,");
        //        sb.Append("SPC_L_R8,");
        //        sb.Append("SPC_J_R,");

        //        sb.Append("胶路检测结果,");
        //        sb.Append("点胶机机台地址,");
        //        sb.Append("压合治具工位,");
        //        sb.Append("压合到位检测结果");

        //        sw.WriteLine(sb);
        //    }


        //    sb.Clear();
        //    sb.Append(MesTempPara.Instance.物料BC);sb.Append(",");
        //    sb.Append(MesTempPara.Instance.日期); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.点胶机排胶参数); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.胶水码); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.胶水有效期); sb.Append(",");  
        //    sb.Append(MesTempPara.Instance.胶水出冰箱时间); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.首件胶重); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.胶水已点数量); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.物料清洗时间); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.组立件治具BC); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.组立件清洗时间); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.点胶开始时间); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.组装开始时间); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.OpenTime); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_K_L1); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_K_L2); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_K_L3); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_K_L4); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_I_L); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_L_L5); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_L_L6); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_L_L7); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_L_L8); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_J_L); sb.Append(",");


        //    sb.Append(MesTempPara.Instance.SPC_K_R1); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_K_R2); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_K_R3); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_K_R4); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_I_R); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_L_R5); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_L_R6); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_L_R7); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_L_R8); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.SPC_J_R); sb.Append(",");


        //    sb.Append(MesTempPara.Instance.胶路检测结果); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.点胶机机台地址); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.压合治具工位); sb.Append(",");
        //    sb.Append(MesTempPara.Instance.压合到位检测结果);

        //    sw.WriteLine(sb);

        //    sw.Close();
        //   // fs.Close();
        //}



        /// <summary>
        /// 读取CSV文件
        /// </summary>
        /// <param name="fileName">文件路径</param>
        public static Dictionary<string, string> ReadIOCSV(string FileName)
        {
            if (File.Exists(FileName) == false)
                return null;
            DataTable dt = new DataTable();
            FileStream fs = new FileStream(FileName, FileMode.Open, FileAccess.Read);
            StreamReader sr = new StreamReader(fs, Encoding.GetEncoding("gb2312"));

            //记录每次读取的一行记录
            string strLine = null;
            //记录每行记录中的各字段内容
            string[] arrayLine = null;

            Dictionary<string, string> t = new Dictionary<string, string>();


            //分隔符
            string[] separators = { "," };
            //判断，若是第一次，建立表头
            bool isFirst = true;

            //逐行读取CSV文件
            while ((strLine = sr.ReadLine()) != null)
            {
                if (strLine.Split(',')[0] == "")
                    break;
                strLine = strLine.Trim();//去除头尾空格
                arrayLine = strLine.Split(separators, StringSplitOptions.RemoveEmptyEntries);//分隔字符串，返回数组
                t.Add(arrayLine[0], arrayLine[1]);

            }
            sr.Close();
            fs.Close();

            return t;
        }
    }
}
