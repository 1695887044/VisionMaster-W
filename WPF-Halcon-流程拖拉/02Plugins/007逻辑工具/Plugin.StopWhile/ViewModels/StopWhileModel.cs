using System;
using System.ComponentModel;
using VM.Start.Attributes;
using VM.Start.Common.Enums;
using VM.Start.Core;

namespace Plugin.StopWhile.ViewModels
{
    [Category("逻辑工具")]
    [DisplayName("停止循环")]
    [ModuleImageName("StopWhile")]
    [Serializable]
    public class StopWhileModel : ModuleBase
    {
        public override bool ExeModule()
        {
            ChangeModuleRunStatus(eRunStatus.OK);
            return true;
        }
    }
}
