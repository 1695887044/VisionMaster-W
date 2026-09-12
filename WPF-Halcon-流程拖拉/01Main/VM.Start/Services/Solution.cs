using EventMgrLib;
using ICSharpCode.NRefactory.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
//using VisionCore.PLC.Communacation;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Extension;
using VM.Start.Common.Helper;
using VM.Start.Common.RightControl;
using VM.Start.Communacation;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.ViewModels;
using VM.Start.Views;
using VM.Start.Views.Dock;

namespace VM.Start.Services
{
    [Serializable]
    public class Solution : NotifyPropertyBase
    {
        #region 单例模式
        private static Solution _Instance = null;

        public Solution() { }

        public static Solution Ins
        {
            get
            {
                if (_Instance == null)
                {
                    _Instance = new Solution();
                }
                return _Instance;
            }
            set { _Instance = value; }
        }
        #endregion

        #region Prop
        public int CurrentProjectID = -1;
        public Project CurrentProject = null;
        public bool QuickMode = false;
        private eViewMode _ViewMode = eViewMode.One;
        public CameraSetViewModel CameraSetViewModel;
        public HardwareConfigViewModel HardwareConfigViewModel;

        [NonSerialized]
        public UIDesignViewModel UIDesignViewModel;

        public LaserSetViewModel LaserSetViewModel;
        private bool _IsUseUIDesign = false;

        private string _UIDesignText = "";
        public string UIDesignText
        {
            get { return _UIDesignText; }
            set
            {
                _UIDesignText = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// 画布模式
        /// </summary>
        public eViewMode ViewMode
        {
            get { return _ViewMode; }
            set
            {
                _ViewMode = value;
                RaisePropertyChanged();
            }
        }

        public bool IsUseUIDesign
        {
            get { return this._IsUseUIDesign; }
            set
            {
                this._IsUseUIDesign = value;
                base.RaisePropertyChanged();
            }
        }

        /// <summary>
        /// 流程列表
        /// </summary>
        public List<Project> ProjectList = new List<Project>();

        /// <summary>
        /// 系统变量
        /// </summary>
        public ObservableCollection<VarModel> SysVar = new ObservableCollection<VarModel>();
        public List<ECommunacation> eCommunacations = new List<ECommunacation>();
        #endregion

        #region Method
        public void SetIsEnable()
        {
            bool running = GetStates();
            if (running == true)
            {
                CommonMethods.UIAsync(() =>
                {
                    IsEnableControl.Ins.Stop = true;
                    IsEnableControl.Ins.RunCycle = false;
                    IsEnableControl.Ins.RunOnce = false;
                    IsEnableControl.Ins.NewSolution = false;
                    IsEnableControl.Ins.SolutionList = false;
                    IsEnableControl.Ins.Open = false;
                    IsEnableControl.Ins.Save = false;
                    IsEnableControl.Ins.GlobalVar = false;
                    IsEnableControl.Ins.CameraSet = false;
                    IsEnableControl.Ins.CommunicationSet = false;
                    IsEnableControl.Ins.HardwareConfig = false;
                    IsEnableControl.Ins.UIDesign = false;
                    ProcessView.Ins.btnRunOnce.Content = "\ue8f0";
                    ProcessView.Ins.btnRunCycle.Content = "\ue6ef";
                    ProcessView.Ins.btnRunOnce.ToolTip = "继续运行(F5)";
                    ProcessView.Ins.btnRunCycle.ToolTip = "逐过程,一步一步运行(F6)";
                });
            }
            else
            {
                CommonMethods.UIAsync(() =>
                {
                    IsEnableControl.Ins.Stop = false;
                    IsEnableControl.Ins.RunCycle = true;
                    IsEnableControl.Ins.RunOnce = true;
                    IsEnableControl.Ins.NewSolution = true;
                    IsEnableControl.Ins.SolutionList = true;
                    IsEnableControl.Ins.Open = true;
                    IsEnableControl.Ins.Save = true;
                    IsEnableControl.Ins.GlobalVar = true;
                    IsEnableControl.Ins.CameraSet = true;
                    IsEnableControl.Ins.CommunicationSet = true;
                    IsEnableControl.Ins.HardwareConfig = true;
                    IsEnableControl.Ins.UIDesign = true;
                    ProcessView.Ins.btnRunOnce.Content = "\ue67b";
                    ProcessView.Ins.btnRunCycle.Content = "\ue612";
                    ProcessView.Ins.btnRunOnce.ToolTip = "当前项目单次执行";
                    ProcessView.Ins.btnRunCycle.ToolTip = "当前项目连续执行";
                });
            }
        }

        public void LoadCommunacation()
        {
            if (eCommunacations == null)
                return;
            foreach (var item in eCommunacations)
            {
                item.IsConnected = false;
                item.IsHasObjectConnected = false;
            }
            EComManageer.setEcomList(eCommunacations); //将反序列化的LIST转为字典，并连接通信设备
        }

        public void UpdataCommunacation()
        {
            eCommunacations = EComManageer.GetEcomList();
        }

        /// <summary>
        /// 创建解决方案
        /// </summary>
        public void CreateSolution()
        {
            var messageView = MessageView.Ins;
            messageView.MessageBoxShow(
                "创建新的解决方案会覆盖掉当前已有的解决方案，确认继续？",
                eMsgType.Warn,
                MessageBoxButton.OKCancel
            );
            if (messageView.DialogResult == true)
            {
                ToolView.Ins.processTree.ItemsSource = null;
                ProcessView.Ins.moduleTree.ItemsSource = null;
                Ins = new Solution();
                Ins.CreateProject(eProjectType.Process);
                Ins.CreateProject(eProjectType.Process);
                Ins.CreateProject(eProjectType.Process);
                Ins.ProjectList[0].ProjectInfo.ProcessName = "Home";
                Ins.ProjectList[1].ProjectInfo.ProcessName = "主流程";
                Ins.ProjectList[2].ProjectInfo.ProcessName = "End";
                ToolView.Ins.UpdateTree();
            }
        }

        /// <summary>
        /// 创建流程
        /// </summary>
        public int CreateProject(eProjectType projectType)
        {
            
            Project project = new Project();
            project.ProjectInfo.ProjectType = projectType;
            //获取新不重复的id  如果已经存在  1,2,4   那么久获得的id 是 3
            bool flag = false;
            int id = 0;
            do
            {
                flag = true;
                switch (projectType)
                {
                    case eProjectType.Process:
                    case eProjectType.Method:
                        foreach (Project prj in ProjectList)
                        {
                            if (
                                prj.ProjectInfo.ProjectID == id
                                && (
                                    prj.ProjectInfo.ProjectType == eProjectType.Method
                                    || prj.ProjectInfo.ProjectType == eProjectType.Process
                                )
                            )
                            {
                                id++;
                                flag = false;
                                break;
                            }
                        }
                        break;
                    case eProjectType.Folder:
                        foreach (Project prj in ProjectList)
                        {
                            if (
                                prj.ProjectInfo.FolderID == id
                                && prj.ProjectInfo.ProjectType == eProjectType.Folder
                            )
                            {
                                id++;
                                flag = false;
                                break;
                            }
                        }
                        break;
                    default:
                        break;
                }
                if (flag == true)
                {
                    break;
                }
            } while (true);

            project.ProjectInfo.ProjectID = id;
            string name = "";
            switch (project.ProjectInfo.ProjectType)
            {
                case eProjectType.Process:
                    name = "流程";
                    break;
                case eProjectType.Method:
                    name = "方法";
                    break;
                case eProjectType.Folder:
                    name = "文件夹";
                    break;
                default:
                    break;
            }
            project.ProjectInfo.ProcessName = name + project.ProjectInfo.ProjectID;
            if (ProjectList.Count >= 3)
            {
                ProjectList.Insert(ProjectList.Count - 1, project);
            }
            else
            {
                ProjectList.Add(project);
            }
            CurrentProjectID = project.ProjectInfo.ProjectID;
            CurrentProject = project;
            return id;
        }

        /// <summary>
        /// 根据id获取对应的project
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public Project GetProjectById(int id)
        {
            return ProjectList.FirstOrDefault(
                c =>
                    c.ProjectInfo.ProjectID == id
                    && c.ProjectInfo.ProjectType != eProjectType.Folder
            );
        }

        public void DeleteProjectById(int id)
        {
            if (ProjectList.Count <= id)
                return;
            ProjectList.RemoveAt(id);
        }

        /// <summary>
        /// 获取所有项目运行状态
        /// </summary>
        /// <returns></returns>
        public bool GetStates()
        {
            foreach (Project prj in ProjectList)
            {
                if (prj.GetThreadStatus() == true)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 连续运行
        /// </summary>
        public void StartRun()
        {
            ProjectList.ForEach(item =>
            {
                if (
                    item.ProjectInfo.ProjectRunMode == eProjectAutoRunMode.主动执行
                    && item.ProjectInfo.ProcessName != "Home"
                    && item.ProjectInfo.ProcessName != "End"
                )
                {
                    item.RunMode = eRunMode.RunCycle;
                    item.Start();
                }
            });
        }

        /// <summary>
        /// 执行一次
        /// </summary>
        public void ExecuteOnce()
        {
            ProjectList.ForEach(item =>
            {
                if (
                    item.ProjectInfo.ProjectRunMode == eProjectAutoRunMode.主动执行
                    && item.ProjectInfo.ProcessName != "Home"
                    && item.ProjectInfo.ProcessName != "End"
                )
                {
                    item.RunMode = eRunMode.RunOnce;
                    item.Start();
                }
            });
        }

        /// <summary>
        /// 停止运行
        /// </summary>
        public void StopRun()
        {
            ProjectList.ForEach(item =>
            {
                if (item.ProjectInfo.ProcessName == "End")
                {
                    item.RunMode = eRunMode.RunOnce;
                    item.Start();
                }
                else if (item.ProjectInfo.ProcessName == "Home") { }
                else
                {
                    item.RunMode = eRunMode.None;
                    item.Stop();
                }
            });
        }

        /// <summary>
        /// 连续运行
        /// </summary>
        /// <param name="projectID"></param>
        public void StartRun(int projectID)
        {
            ProjectList.FirstOrDefault(c => c.ProjectInfo.ProjectID == projectID).RunMode =
                eRunMode.RunCycle; //循环
            ProjectList.FirstOrDefault(c => c.ProjectInfo.ProjectID == projectID).Start();
        }

        /// <summary>
        /// 执行一次
        /// </summary>
        /// <param name="projectID"></param>
        public void ExecuteOnce(int projectID)
        {
            ProjectList.FirstOrDefault(c => c.ProjectInfo.ProjectID == projectID).RunMode =
                eRunMode.RunOnce; //循环
            ProjectList.FirstOrDefault(c => c.ProjectInfo.ProjectID == projectID).Start();
        }

        /// <summary>
        /// 停止运行
        /// </summary>
        /// <param name="projectID"></param>
        public void StopRun(int projectID)
        {
            ProjectList.FirstOrDefault(c => c.ProjectInfo.ProjectID == projectID).RunMode =
                eRunMode.None; //停止
            ProjectList.FirstOrDefault(c => c.ProjectInfo.ProjectID == projectID).Stop();
        }
        #endregion
    }
}
