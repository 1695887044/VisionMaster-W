using EventMgrLib;
using Plugin.RunProject.Model;
using Plugin.RunProject.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
using VM.Start.Views;

namespace Plugin.RunProject.ViewModels
{
    public enum eLinkCommand
    {
        DataValueLink,
        VarValueLinK,
    }

    [Category("逻辑工具")]
    [DisplayName("执行流程")]
    [ModuleImageName("RunProject")]
    [Serializable]
    public class RunProjectViewModel : ModuleBase
    {

        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                switch (RunProjectType)
                {
                    case eRunProjectType.单次执行:
                        for (int i = 0; i < ProjectRunModeDataSource.Count; i++)
                        {
                            if (ProjectRunModeDataSource[i].IsRun)
                            {
                                Solution.Ins.ExecuteOnce(Solution.Ins.ProjectList[i].ProjectInfo.ProjectID);
                            }
                        }
                        for (int i = 0; i < ProjectRunModeDataSource.Count; i++)
                        {
                            if (ProjectRunModeDataSource[i].IsRun && ProjectRunModeDataSource[i].IsWait)
                            {
                                while (Solution.Ins.ProjectList[i].GetThreadStatus())
                                { }                                                                
                            }
                        }
                        break;
                    case eRunProjectType.循环执行:
                        for (int i = 0; i < ProjectRunModeDataSource.Count; i++)
                        {
                            if (ProjectRunModeDataSource[i].IsRun)
                            {
                                Solution.Ins.StartRun(Solution.Ins.ProjectList[i].ProjectInfo.ProjectID);
                            }
                        }
                        break;
                    case eRunProjectType.停止执行:
                        for (int i = 0; i < ProjectRunModeDataSource.Count; i++)
                        {
                            if (ProjectRunModeDataSource[i].IsRun)
                            {
                                Solution.Ins.StopRun(Solution.Ins.ProjectList[i].ProjectInfo.ProjectID);
                            }
                        }
                        break;
                    default:
                        break;
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
            base.AddOutputParams();
        }

        #region Prop
        private eRunProjectType _RunProjectType = eRunProjectType.单次执行;
        /// <summary>
        /// 流程被调用时的执行方式（单次运行/循环运行/停止运行）
        /// </summary>
        public eRunProjectType RunProjectType
        {
            get { return _RunProjectType; }
            set { Set(ref _RunProjectType, value); }
        }
        public ObservableCollection<RunProjectTypeModel> ProjectRunModeDataSource { get; set; } = new ObservableCollection<RunProjectTypeModel>();
        #endregion

        #region Command

        [NonSerialized]
        private CommandBase _ActivatedCommand;
        public CommandBase ActivatedCommand
        {
            get
            {
                if (_ActivatedCommand == null)
                {
                    _ActivatedCommand = new CommandBase((obj) =>
                    {
                        InitDataSource();
                        Logger.AddLog("activated");
                    });
                }
                return _ActivatedCommand;
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
                        var view = this.ModuleView as RunProjectView;
                        if (view != null)
                        {
                            view.Close();
                        }
                    });
                }
                return _ConfirmCommand;
            }
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

        #endregion

        #region 方法
        public void InitDataSource()
        {
            if (ProjectRunModeDataSource == null)
                ProjectRunModeDataSource = new ObservableCollection<RunProjectTypeModel>();
            //从ProjectRunModeDataSource中找出Solution.Ins.ProjectList删除的项目，并将其更新
            for (int i = 0; i < ProjectRunModeDataSource.Count; i++)
            {
                if (null == Solution.Ins.ProjectList.FirstOrDefault(c => c.ProjectInfo.ProcessName == ProjectRunModeDataSource[i].ProcessName))
                {
                    ProjectRunModeDataSource.RemoveAt(i);
                    i--;
                }
            }
            //从Solution.Ins.ProjectList中找到新增的项目，更新到ProjectRunModeDataSource
            if (ProjectRunModeDataSource.Count != Solution.Ins.ProjectList.Count)
            {
                for (int i = 0; i < Solution.Ins.ProjectList.Count; i++)
                {
                    if (null == ProjectRunModeDataSource.FirstOrDefault(c => c.ProcessName == Solution.Ins.ProjectList[i].ProjectInfo.ProcessName))
                    {
                        ProjectRunModeDataSource.Insert(i, new RunProjectTypeModel { ProcessName = Solution.Ins.ProjectList[i].ProjectInfo.ProcessName });
                    }
                }
            }
            //按照最新的排序更新ProjectRunModeDataSource
            foreach (var item in Solution.Ins.ProjectList)
            {
                int index = ProjectRunModeDataSource.FindIndex(c => c.ProcessName == item.ProjectInfo.ProcessName);
                ProjectRunModeDataSource.Move(index, ProjectRunModeDataSource.Count - 1);
            }
        }
        #endregion
    }
}
