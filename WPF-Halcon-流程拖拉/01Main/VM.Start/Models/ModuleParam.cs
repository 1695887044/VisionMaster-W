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
    /// 模块信息
    /// </summary>
    [Serializable]
    public class ModuleParam:NotifyPropertyBase
    {
        /// <summary>
        /// 模块名
        /// </summary>
        public string ModuleName { get; set; } = "";
        /// <summary>
        /// 模块编号
        /// </summary>
        public int ModuleNo { get; set; } 

        /// <summary>
        /// 当前模块所在的循环的index地址,不需要序列化
        /// </summary>
        [NonSerialized]
        public int pIndex = -1;
        /// <summary>第一次运行标志 </summary>
        [NonSerialized]
        public bool FirstRunFlag = false;
        /// <summary>
        /// 针对循环工具 用来模拟循环多少次的,实际中,当循环次数到达CyclicCount的时候
        /// </summary>
        public int CyclicCount = 0;
        private string _Remarks;
        /// <summary>
        /// 模块注释
        /// </summary>
        public string Remarks
        {
            get { return _Remarks; }
            set { Set(ref _Remarks, value); }
        }
        /// <summary>
        /// 模块编号 
        /// </summary>
        public int ModuleEncode { get; set; } = 1;
        /// <summary>
        /// 插件名称  每一个插件都是唯一	
        /// </summary>
        public string PluginName { get; set; } = "";
        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsUse { get; set; } = true;
        /// <summary>
        /// 使能断点
        /// </summary>
        public bool IsEnableBreakPoint { get; set; } = false;
        /// <summary>
        /// 启用超级工具
        /// </summary>
        public bool IsUseSuperTool { get; set; } = true;
        private double _ElapsedTime;
        /// <summary>
        /// 模块运行时间
        /// </summary>
        public double ElapsedTime
        {
            get { return _ElapsedTime; }
            set { Set(ref _ElapsedTime, value); }
        }
        private eRunStatus _Statue = eRunStatus.NotRun;
        /// <summary>
        /// 模块状态
        /// </summary>
        public eRunStatus Status
        {
            get { return _Statue; }
            set { Set(ref _Statue, value); }
        }
        /// <summary>
        /// 所属的项目id
        /// </summary>

        public int ProjectID { get; set; } = -1;

    }
}
