using EventMgrLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Events;
using System.IO;
using System.Reflection;

namespace VM.Start.Common.Const
{
    public class Version
    {
        #region Singleton

        private static readonly Lazy<Version> Instance = new Lazy<Version>(() => new Version());

        private Version()
        {

        }
        public static Version Ins { get; } = Instance.Value;
        #endregion
        public string _SoftwareVersion = File.GetLastWriteTime(Directory.GetCurrentDirectory() + "\\VM.Start.exe").ToString("yyyyMMdd")+"-"+
            Assembly.GetExecutingAssembly().GetName().Version.ToString();//"20221209-1.101.0.0";
        public string SoftwareVersion
        {
            get { return _SoftwareVersion; }
            set { _SoftwareVersion = value; }

        }
    }
}
