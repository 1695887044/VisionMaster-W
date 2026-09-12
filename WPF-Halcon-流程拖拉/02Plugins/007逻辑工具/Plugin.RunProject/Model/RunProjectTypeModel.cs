using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;

namespace Plugin.RunProject.Model
{
    [Serializable]
    public class RunProjectTypeModel : NotifyPropertyBase
    {
        private string _ProcessName;
        /// <summary>
        /// 名称
        /// </summary>
        public string ProcessName
        {
            get {return _ProcessName; }
            set { Set(ref _ProcessName, value); }
        }

        private bool _IsRun = false;
        /// <summary>
        /// 是否执行
        /// </summary>
        public bool IsRun
        {
            get { return _IsRun; }
            set { Set(ref _IsRun,value); }
        }

        private bool _IsWait = false;
        /// <summary>
        /// 流程被调用时，是否等待流程运行完成
        /// </summary>
        public bool IsWait 
        {
            get { return _IsWait; }
            set { Set(ref _IsWait, value); }
        }
    }
}
