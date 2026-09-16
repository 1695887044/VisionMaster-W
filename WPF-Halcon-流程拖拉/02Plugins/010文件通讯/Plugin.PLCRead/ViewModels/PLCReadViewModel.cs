using EventMgrLib;
using HalconDotNet;
using Plugin.PLCRead.Models;
using Plugin.PLCRead.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using VM.Halcon;
using VM.Halcon.Config;
using VM.Halcon.Model;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Communacation;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.ViewModels;
using VM.Start.Views;
using VM.Start.Views.Dock;

namespace Plugin.PLCRead.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        Line1,
        Line2,
    }
    #endregion

    [Category("文件通讯")]
    [DisplayName("PLC读取")]
    [ModuleImageName("PLCRead")]
    [Serializable]
    public class PLCReadViewModel : ModuleBase
    {
        public override void SetDefaultLink()
        {
        }

        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                string plcKey = "";
                foreach (var item in EComManageer.s_ECommunacationDic)
                {
                    if (item.Value.m_connectKey == CurKey)
                    {
                        plcKey = item.Key;
                    }
                }
                if (plcKey != "")
                {
                    //EComManageer.s_ECommunacationDic[plcKey].m_IntNumber = VM.Start.Common.Enums.PLCIntDataLengthEnum._16位;
                    //EComManageer.s_ECommunacationDic[plcKey].m_DoubleNumber = VM.Start.Common.Enums.PLCDoubleDataLengthEnum._32位;
                    if (EComManageer.readRegister(plcKey, DataType, StartAddress, out ReadValue))
                    {
                        Logger.AddLog(ReadValue.ToString());
                        ChangeModuleRunStatus(eRunStatus.OK);
                    }
                    else
                    {
                        ChangeModuleRunStatus(eRunStatus.NG);
                    }
                }
                else
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                }
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
            switch (DataType)
            {
                case PLCDataWriteReadTypeEnum.布尔:
                    break;
                case PLCDataWriteReadTypeEnum.整型:
                    if (ReadValue == "") ReadValue = "0";
                    AddOutputParam("读取int值", "int", int.Parse(ReadValue));
                    break;
                case PLCDataWriteReadTypeEnum.浮点:
                    if (ReadValue == "") ReadValue = "0.0000";
                    AddOutputParam("读取double值", "double", Convert.ToDouble(ReadValue).ToString("0.0000"));
                    break;
                case PLCDataWriteReadTypeEnum.字符串:
                    break;
            }       
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        #region Prop
        private ObservableCollection<ReadVarModel> _Vars = new ObservableCollection<ReadVarModel>();
        /// <summary>
        /// 变量
        /// </summary>
        public ObservableCollection<ReadVarModel> Vars
        {
            get { return _Vars; }
            set { _Vars = value; RaisePropertyChanged(); }
        }

        /// <summary>读取出来的值</summary>
        string ReadValue = "";
        
        /// <summary>
        /// 寄存器起始地址
        /// </summary>
        private int _StartAddress = 1;
        public int StartAddress
        {
            get { return _StartAddress; }
            set { Set(ref _StartAddress, value); }
        }
        /// <summary>
        /// 当前Key
        /// </summary>
        private string _CurKey = "";
        public string CurKey
        {
            get { return _CurKey; }
            set
            {
                Set(ref _CurKey, value, new Action(() =>
                {
                    upDataRemaks();
                }));
            }
        }
        private PLCDataWriteReadTypeEnum _DataType = PLCDataWriteReadTypeEnum.浮点;
        /// <summary>
        /// 指定图像
        /// </summary>
        public PLCDataWriteReadTypeEnum DataType
        {
            get
            {
                return _DataType;
            }
            set
            {
                Set(ref _DataType, value);
            }
        }
        /// <summary>
        /// Com口数据,用来显示KEY列表数据源
        /// </summary>
        private List<string> _ComKeys;
        public List<string> ComKeys
        {
            get
            {
                if (_ComKeys == null) _ComKeys = new List<string>();
                return _ComKeys;
            }
            set { _ComKeys = value; RaisePropertyChanged(); }
        }
        #endregion
        #region Command
        public override void Loaded()
        {
            base.Loaded();
            ComKeys = EComManageer.GetPLCConnectKeys();
            upDataRemaks();
            if (ComKeys != null && ComKeys.Count > 0)
            {
                CurKey = ComKeys[0];
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
                        var view = this.ModuleView as PLCReadView;
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
        private CommandBase _AddCommand;
        public CommandBase AddCommand
        {
            get
            {
                if (_AddCommand == null)
                {
                    _AddCommand = new CommandBase((obj) =>
                    {
                        switch (obj)
                        {
                            case "int":

                                break;
                            case "double":

                                break;
                            default:
                                break;
                        }
                    });
                }
                return _AddCommand;
            }
        }
        [NonSerialized]
        private CommandBase _DeleteCommand;
        public CommandBase DeleteCommand
        {
            get
            {
                if (_DeleteCommand == null)
                {
                    _DeleteCommand = new CommandBase((obj) =>
                    {
                        //var view = ModuleView as PLCReadView;
                        //if (view.dg.SelectedIndex == -1) return;
                        //Vars.RemoveAt(view.dg.SelectedIndex);
                        //UpdateData();
                    });
                }
                return _DeleteCommand;
            }
        }
        [NonSerialized]
        private CommandBase _MoveCommand;
        public CommandBase MoveCommand
        {
            get
            {
                if (_MoveCommand == null)
                {
                    _MoveCommand = new CommandBase((obj) =>
                    {
                        var view = ModuleView as PLCReadView;
                        switch (obj)
                        {
                            //case "Up":
                            //    if (view.dg.SelectedIndex <= 0 || Vars.Count <= 1) return;
                            //    Vars.Move(view.dg.SelectedIndex, view.dg.SelectedIndex - 1);
                            //    UpdateData();
                            //    break;
                            //case "Down":
                            //    if (view.dg.SelectedIndex == -1 || Vars.Count <= 1 || view.dg.SelectedIndex == (Vars.Count - 1)) return;
                            //    Vars.Move(view.dg.SelectedIndex, view.dg.SelectedIndex + 1);
                            //    UpdateData();
                            //    break;
                            //default:
                            //    break;
                        }
                    });
                }
                return _MoveCommand;
            }
        }
        #endregion
        #region Method
        private void UpdateData()
        {

        }
        public void upDataRemaks()
        {
            if (CurKey == "") return;
            string[] arr = CurKey.Split('.');
            foreach (var item in Solution.Ins.ProjectList)
            {
                //if (item.ProjectInfo.ProjectID == int.Parse(arr[0]))
                //{
                //    arr[0] = item.ProjectInfo.ProcessName;
                //}
            }
            //Remarks = arr[0] + "." + arr[1];
        }
        #endregion
    }
}
