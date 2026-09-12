using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Common.Engine
{

    public class ScriptTemplate
    {
        //原始代码 不要做任何修改,包括一个空格和空白行!!!!   
        public static string s_RawScript =
            @"using VM.Start.Dialogs.Views;
using VM.Start.Common.Enums;
using VM.Start.Common.Provide;
using VM.Start.Common;
using VM.Start.Core;
using VM.Start.ViewModels;
using VM.Start.Models;
using System;
using System.Windows;
using System.Collections.Generic;
using VM.Start.Events;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sample.CSS;
using HalconDotNet;
using System.Xml;

    public class MyScript: ModuleBase
    {
        public override bool ExeModule()
        {
            if( System.Diagnostics.Debugger.IsAttached)
            {
                System.Diagnostics.Debugger.Break();//断点会在这里,前面可自行打断点
            }
            var messageView = MessageView.Ins;
            messageView.MessageBoxShow(""确认保存数据吗?"", eMsgType.Warn, MessageBoxButton.OKCancel);
            if (messageView.DialogResult == false)
            {
                Logger.AddLog(""数据保存失败！"", eMsgType.Warn, isDispGrowl: true);
            }
            else
            {
                Logger.AddLog(""数据保存成功！"", eMsgType.Success, isDispGrowl: true);
            }
            return true;
        }
        public void Init(ModuleParam moduleParam)
        {
            ModuleParam = moduleParam;
        }
}

";


    }
}
