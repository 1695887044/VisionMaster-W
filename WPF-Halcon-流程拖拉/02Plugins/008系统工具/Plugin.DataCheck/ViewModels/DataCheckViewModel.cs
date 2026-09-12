using ControlzEx.Standard;
using EventMgrLib;
using HalconDotNet;
using Plugin.DataCheck.Model;
using Plugin.DataCheck.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
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

namespace Plugin.DataCheck.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        Data,
    }
    #endregion

    [Category("系统工具")]
    [DisplayName("数据检查")]
    [ModuleImageName("DataCheck")]
    [Serializable]
    public class DataCheckViewModel : ModuleBase
    {
        public override void SetDefaultLink()
        {

        }

        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                Result = true;
                if (DataChecks.Count == 0)
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                foreach (var Data in DataChecks)
                {
                    if (Data.IsCheck)
                    {
                        var value = GetLinkValue(Data.DataLinkText);
                        double valueD = 0;
                        switch (Data.DataType)
                        {
                            case "int":
                            case "double":
                            case "bool":
                                valueD = Convert.ToDouble(value);
                                break;
                            default:
                                break;
                        }
                        Data.DataLinkValue = valueD.ToString();
                        if (valueD > (Convert.ToDouble(Data.lowerLimit) - Convert.ToDouble(Data.lowerDeviation)) && valueD < (Convert.ToDouble(Data.upperLimit) + Convert.ToDouble(Data.upperDeviation)))
                        {
                            Data.State = true;
                        }
                        else
                        {
                            Data.State = false;
                            Result = false;
                        }
                    }
                    else
                    {
                        Data.State = true;
                    }
                }
                if (Result)
                {
                    ChangeModuleRunStatus(eRunStatus.OK);
                    return true;
                }
                else
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
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
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        #region Prop
        /// <summary>
        /// 数据信息
        /// </summary>
        public ObservableCollection<DataCheckModel> DataChecks { get; set; } = new ObservableCollection<DataCheckModel>();

        private DataCheckModel _SelectedData = new DataCheckModel();
        /// <summary>
        /// 选中的文本
        /// </summary>
        public DataCheckModel SelectedData
        {
            get { return _SelectedData; }
            set { Set(ref _SelectedData, value); }
        }

        private string _LinkDataName;

        public string LinkDataName
        {
            get { return _LinkDataName; }
            set { Set(ref _LinkDataName, value); }
        }

        private string _LinkDataValue;

        public string LinkDataValue
        {
            get { return _LinkDataName; }
            set { Set(ref _LinkDataName, value); }
        }

        private bool _Result = false;

        public bool Result
        {
            get { return _Result; }
            set { Set(ref _Result, value); }
        }


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
                        var view = this.ModuleView as DataCheckView;
                        if (view != null)
                        {
                            view.Close();
                        }
                    });
                }
                return _ConfirmCommand;
            }
        }
        private void OnVarChanged(VarChangedEventParamModel obj)
        {
            switch (obj.SendName.Split(',')[1])
            {
                case "LinkData":
                    LinkDataName = obj.LinkName;
                    if (obj.DataType == "bool")
                    {
                        DataChecks.Add(new DataCheckModel() { DataLinkText = LinkDataName, ID = DataChecks.Count + 1, DataType = obj.DataType, upperLimit = "1.1", lowerLimit = "0.6" });
                    }
                    else
                    {
                        DataChecks.Add(new DataCheckModel() { DataLinkText = LinkDataName, ID = DataChecks.Count + 1, DataType = obj.DataType });
                    }
                    break;
                default:
                    break;
            }
        }
        private void OnVarChanged1(VarChangedEventParamModel obj)
        {
            switch (obj.SendName.Split(',')[1])
            {
                case "DataChanged":
                    SelectedData.DataLinkText = obj.LinkName;
                    SelectedData.DataType = obj.DataType;
                    break;
                default:
                    break;
            }
        }

        [NonSerialized]
        private CommandBase _LinkCommandButton;
        public CommandBase LinkCommandButton
        {
            get
            {
                if (_LinkCommandButton == null)
                {
                    //以GUID+类名作为筛选器
                    EventMgr.Ins.GetEvent<VarChangedEvent>().Subscribe(OnVarChanged, o => o.SendName.StartsWith($"{ModuleGuid}"));
                    _LinkCommandButton = new CommandBase((obj) =>
                    {
                        CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules);
                        EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},LinkData");
                    });
                }
                return _LinkCommandButton;
            }
        }
        [NonSerialized]
        private CommandBase _DataChangedCommand;
        public CommandBase DataChangedCommand
        {
            get
            {
                if (_DataChangedCommand == null)
                {
                    //以GUID+类名作为筛选器
                    EventMgr.Ins.GetEvent<VarChangedEvent>().Subscribe(OnVarChanged1, o => o.SendName.StartsWith($"{ModuleGuid}"));
                    _DataChangedCommand = new CommandBase((obj) =>
                    {
                        CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules);
                        EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},DataChanged");
                    });
                }
                return _DataChangedCommand;
            }
        }
        [NonSerialized]
        private CommandBase _DataOperateCommand;
        public CommandBase DataOperateCommand
        {
            get
            {
                if (_DataOperateCommand == null)
                {
                    _DataOperateCommand = new CommandBase((obj) =>
                    {
                        switch (obj)
                        {
                            case "Delete":
                                if (SelectedData == null) return;
                                DataChecks.Remove(SelectedData);
                                break;
                            case "DownMove":
                                if ((Convert.ToInt32(SelectedData.ID)) < DataChecks.Count)
                                    DataChecks.Move(Convert.ToInt32(SelectedData.ID) - 1, Convert.ToInt32(SelectedData.ID));
                                break;
                            case "UpMove":
                                if ((Convert.ToInt32(SelectedData.ID) - 1) > 0)
                                    DataChecks.Move(Convert.ToInt32(SelectedData.ID) - 1, Convert.ToInt32(SelectedData.ID) - 2);
                                break;
                            default:
                                break;
                        }
                        IDChanged();
                    });
                }
                return _DataOperateCommand;
            }
        }
        #endregion
        #region Method
        private void IDChanged()
        {
            int ID = 1;
            foreach (var Data in DataChecks)
            {
                Data.ID = ID;
                ID += 1;
            }
        }
        #endregion
    }
}
