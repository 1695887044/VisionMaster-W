using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VM.Start.Dialogs.Views;
using VM.Start.Services;

namespace VM.Start.Script
{
    //脚本内的方法信息
    class ScriptMethodInfo
    {
        public string Name;
        public string Description;
        public string Category;
        public string DisplyName; // 格式"string key, int value, bool addFlag = false"
    }

    public class ScriptMethods
    {
        public int ProjectID { get; set; } = 0; //脚本所在项目的id /执行run方法的时候 赋值
        public string ModuleName { get; set; } //脚本所在对应的模块名称

        /// <summary>
        /// 弹窗显示
        /// </summary>
        /// <param name="str"></param>
        public void Show(string str)
        {
            MessageView.Ins.MessageBoxShow(str);
        }

        public Object GetObject(string linkStr)
        {
            Project prj = Solution.Ins.GetProjectById(ProjectID);
            var var = prj.GetParamByName(linkStr);
            if (var == null)
            {
                return null;
            }
            object obj = var.Value;
            return obj;
        }

        public double getDouble(string linkStr)
        {
            return Convert.ToDouble(GetObject(linkStr).ToString());
        }

        public int getInt(string linkStr)
        {
            return Convert.ToInt32(GetObject(linkStr).ToString());
        }

        public bool getBool(string linkStr)
        {
            return GetObject(linkStr).ToString().ToLower() == "true" ? true : false;
        }

        public string getString(string linkStr)
        {
            return GetObject(linkStr)?.ToString() ?? "";
        }
    }
}
