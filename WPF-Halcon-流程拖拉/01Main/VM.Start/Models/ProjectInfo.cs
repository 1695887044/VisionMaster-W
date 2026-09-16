using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;

namespace VM.Start.Models
{
    /// <summary>
    /// 项目信息
    /// </summary>
    [Serializable]
    public class ProjectInfo:NotifyPropertyBase
    {
        /// <summary>
        /// 项目ID
        /// </summary>
        public int ProjectID { get; set; }
        /// <summary>
        /// 文件夹ID
        /// </summary>
        public int FolderID { get; set; }

        /// <summary>
        /// 项目名称
        /// </summary>
        //public string ProjectName { get; set; }
        /// <summary>
        /// 流程名称
        /// </summary>
        public string ProcessName { get; set; }

        private string _Remarks;
        /// <summary>
        /// 模块注释
        /// </summary>
        public string Remarks
        {
            get { return _Remarks; }
            set { Set(ref _Remarks, value); }
        }
        private bool _IsEncypt=false;
        /// <summary>
        /// 加密
        /// </summary>
        public bool IsEncypt
        {
            get { return _IsEncypt; }
            set { Set(ref _IsEncypt, value); }
        }


        /// <summary>
        /// 流程运行模式（主动执行/调用执行）
        /// </summary>
        private eProjectAutoRunMode _ProjectRunMode = eProjectAutoRunMode.主动执行;
        public eProjectAutoRunMode ProjectRunMode
        {
            get { return _ProjectRunMode; }
            set { Set(ref _ProjectRunMode, value); }
        }
        public eProjectType ProjectType { get; set; }
        /// <summary>
        /// 是否刷新UI
        /// </summary>
        private bool _IsRefreshUi = true;
        public bool IsRefreshUi
        {
            get { return _IsRefreshUi; }
            set { Set(ref _IsRefreshUi, value); }
        }
    }
}
