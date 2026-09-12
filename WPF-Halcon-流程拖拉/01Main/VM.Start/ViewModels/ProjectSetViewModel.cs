using HandyControl.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using VM.Start.Common.Enums;
//using VM.Halcon.Helper;
using VM.Start.Common.Helper;
using VM.Start.Dialogs.Views;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.Views;
using VM.Start.Views.Dock;

//using static VM.Start.Models.VarModel;

namespace VM.Start.ViewModels
{
    //internal class ProjectSetViewModel
    public class ProjectSetViewModel : NotifyPropertyBase
    {
        #region Singleton

        private static readonly ProjectSetViewModel _instance = new ProjectSetViewModel();

        private ProjectSetViewModel() { }

        public static ProjectSetViewModel Ins
        {
            get { return _instance; }
        }

        #endregion

        #region Prop
        private ObservableCollection<ProjectRunModeVarModel> _ProjectRunModeDataSource =
            new ObservableCollection<ProjectRunModeVarModel>();

        /// <summary>
        /// 系统变量
        /// </summary>
        public ObservableCollection<ProjectRunModeVarModel> ProjectRunModeDataSource
        {
            get { return _ProjectRunModeDataSource; }
            set
            {
                _ProjectRunModeDataSource = value;
                RaisePropertyChanged();
            }
        }
        #endregion
        #region Command
        private CommandBase _ActivatedCommand;
        public CommandBase ActivatedCommand
        {
            get
            {
                if (_ActivatedCommand == null)
                {
                    _ActivatedCommand = new CommandBase(
                        (obj) =>
                        {
                            if (ProjectSetView.Ins.IsClosed)
                            {
                                ProjectSetView.Ins.IsClosed = false;
                                ProjectRunModeDataSource.Clear();

                                for (int i = 0; i < Solution.Ins.ProjectList.Count; i++)
                                {
                                    ProjectRunModeVarModel temp = new ProjectRunModeVarModel();
                                    temp.Name = Solution.Ins.ProjectList[i].ProjectInfo.ProcessName;
                                    temp.RunMode = Solution.Ins.ProjectList[i]
                                        .ProjectInfo
                                        .ProjectRunMode;
                                    temp.IsRefreshUi = Solution.Ins.ProjectList[i]
                                        .ProjectInfo
                                        .IsRefreshUi;
                                    ProjectRunModeDataSource.Add(temp);
                                }
                            }
                        }
                    );
                }
                return _ActivatedCommand;
            }
        }

        private CommandBase _ConfirmCommand;
        public CommandBase ConfirmCommand
        {
            get
            {
                if (_ConfirmCommand == null)
                {
                    _ConfirmCommand = new CommandBase(
                        (obj) =>
                        {
                            if (!IsRepeat())
                            {
                                for (int i = 0; i < Solution.Ins.ProjectList.Count; i++)
                                {
                                    Solution.Ins.ProjectList[i].ProjectInfo.ProcessName =
                                        ProjectRunModeDataSource[i].Name;
                                    Solution.Ins.ProjectList[i].ProjectInfo.ProjectRunMode =
                                        ProjectRunModeDataSource[i].RunMode;
                                    Solution.Ins.ProjectList[i].ProjectInfo.IsRefreshUi =
                                        ProjectRunModeDataSource[i].IsRefreshUi;
                                }
                                ToolView.Ins.UpdateTree();
                                ProjectSetView.Ins.Close();
                            }
                        }
                    );
                }
                return _ConfirmCommand;
            }
        }
        #endregion
        #region Method
        /// <summary>
        /// 判断流程名称是否有重复
        /// </summary>
        /// <returns></returns>
        public bool IsRepeat()
        {
            List<string> temp = ProjectRunModeDataSource.Select(c => c.Name).ToList();
            if (temp.Distinct().Count() != temp.Count())
            {
                MessageView.Ins.MessageBoxShow("存在流程名称重复！", eMsgType.Warn);
                return true;
            }
            return false;
        }
        #endregion
    }
}
