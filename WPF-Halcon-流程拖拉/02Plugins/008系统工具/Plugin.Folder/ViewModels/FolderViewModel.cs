using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Core;
using VM.Start.Attributes;

namespace Plugin.GrabImage.ViewModels
{
    [Category("系统工具")]
    [DisplayName("文件夹")]
    [ModuleImageName("Folder")]
    [Serializable]
    public class FolderViewModel : ModuleBase
    {
        public override bool ExeModule()
        {
            ChangeModuleRunStatus(VM.Start.Common.Enums.eRunStatus.OK);
            return true;
        }
    }
}
