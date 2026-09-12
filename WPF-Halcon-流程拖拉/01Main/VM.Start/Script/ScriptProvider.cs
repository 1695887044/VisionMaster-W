using ICSharpCode.CodeCompletion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using VM.Start.Dialogs.Views;
using VM.Start.Common.Enums;
using VM.Start.Common.Provide;
using VM.Start.Common;
using VM.Start.Events;
using HalconDotNet;
using System.Xml;

namespace VM.Start.Script
{
    /// <summary>
    /// This is a simple script provider that adds a few using statements to the C# scripts (.csx files)
    /// </summary>
    public class ScriptProvider : ICSharpScriptProvider
    {
        //需要支持提示的DLL在此处加载,同时也要把命名空间加上
        public static Assembly[] GetRelativeAssemblies()
        {
            return new Assembly[]
            {
                typeof(object).Assembly, //mscorlib
                typeof(Uri).Assembly, //System.dll
                typeof(Enumerable).Assembly, //System.Core.dll
                typeof(MessageBox).Assembly,
                typeof(Logger).Assembly,
                typeof(MessageView).Assembly,
                typeof(CommonMethods).Assembly,
                typeof(SoftwareExitEvent).Assembly,
                typeof(HalconDotNet.HalconAPI).Assembly,
                typeof(XmlDocument).Assembly,
            };
        }

        //不需要再添加
        public string GetUsing()
        {
            //会自动拼接 加在代码片段前,我们现在使用完整的cs,不需要使用
            return "";
        }

        public string GetVars()
        {
            return ""; //针对.csx的 我们现在使用完整的cs,不需要使用
        }

        public string GetNamespace() => null;
    }
}
