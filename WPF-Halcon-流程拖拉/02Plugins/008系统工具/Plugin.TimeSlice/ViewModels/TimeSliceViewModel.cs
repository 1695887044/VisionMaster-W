using EventMgrLib;
using HalconDotNet;
using Plugin.TimeSlice.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Xml.Serialization;
using VM.Halcon;
using VM.Halcon.Config;
using VM.Halcon.Model;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Extension;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.ViewModels;
using VM.Start.Views.Dock;

namespace Plugin.TimeSlice.ViewModels
{
    #region enum
    #endregion

    [Category("系统工具")]
    [DisplayName("时间片段")]
    [ModuleImageName("TimeSlice")]
    [Serializable]
    public class TimeSliceViewModel : ModuleBase
    {
        public override void SetDefaultLink()
        {
        }

        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                if (TimeText==null)
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                if (IsTimeStart)
                {
                    DateTime= DateTime.Now;
                }
                else
                {
                    timeSlice=Math.Round(GenStartTime(),1);
                }
                
                ChangeModuleRunStatus(eRunStatus.OK);
                return true;

            }
            catch (Exception ex)
            {
                ChangeModuleRunStatus(eRunStatus.NG);
                Logger.GetExceptionMsg(ex);
                return false;
            }
        }
        public override void AddOutputParams()
        {
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("片段时间", "double", timeSlice);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        #region Prop


        private bool _IsTimeStart = true;

        public bool IsTimeStart
        {
            get { return _IsTimeStart; }
            set { Set(ref _IsTimeStart, value); }
        }
        public double timeSlice = 0;

        #endregion
        #region Command
        public override void Loaded()
        {
            base.Loaded();


        }
        [NonSerialized]
        private CommandBase _ExecuteCommand;
        public CommandBase ExecuteCommand
        {
            get
            {
                if (_ExecuteCommand == null)
                {
                    _ExecuteCommand = new CommandBase((obj) =>
                    {
                        ExeModule();
                    });
                }
                return _ExecuteCommand;
            }
        }
        [NonSerialized]
        private CommandBase _ConfirmCommand;
        public CommandBase ConfirmCommand
        {
            get
            {
                if (_ConfirmCommand == null)
                {
                    _ConfirmCommand = new CommandBase((obj) =>
                    {
                        var view = this.ModuleView as TimeSliceView;
                        if (view != null)
                        {
                            view.Close();
                        }
                    });
                }
                return _ConfirmCommand;
            }
        }
        #endregion
        #region Method
        private double GenStartTime()
        {
            int index = Prj.ModuleList.IndexOf(this);
            for (int i = index - 1; i >= 0; i--)
            {
                if (Prj.ModuleList[i].ModuleParam.ModuleName.StartsWith("时间片段"))
                {
                    if (TimeText== Prj.ModuleList[i].TimeText)
                    {
                        DateTime = DateTime.Now;
                        TimeSpan t = DateTime - Prj.ModuleList[i].DateTime;
                        return t.TotalMilliseconds;
                    }
                }
            }
            return 0;
        }
        #endregion
    }
}
