using ControlzEx.Standard;
using EventMgrLib;
using HalconDotNet;
using Plugin.CreateString.Model;
using Plugin.CreateString.Views;
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

namespace Plugin.CreateString.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        Data,
    }
    #endregion

    [Category("变量工具")]
    [DisplayName("创建文本")]
    [ModuleImageName("CreateString")]
    [Serializable]
    public class CreateStringViewModel : ModuleBase
    {
        public override void SetDefaultLink()
        {

        }

        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                ResultString = null;
                if (CreateString.Count == 0)
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                string DataFormatCopy = DataFormat;
                foreach (var data in CreateString)
                {
                    string str1 = "{" + data.IDString + "}";
                    if (DataFormatCopy.Contains(str1))
                    {
                        string str2 = null;
                        switch (data.DataType)
                        {
                            case "bool":
                                str2 = (Convert.ToBoolean(GetLinkValue(data.DataLinkText)))
                                    ? TrueReplace
                                    : FalseReplace;
                                break;
                            case "double":
                                str2 = Math.Round(Convert.ToDouble(GetLinkValue(data.DataLinkText)), DataReserver)
                                    .ToString();
                                break;
                            default:
                                str2 = GetLinkValue(data.DataLinkText).ToString();
                                break;
                        }
                        DataFormatCopy = DataFormatCopy.Replace(str1, str2);
                    }

                    ResultString = DataFormatCopy;
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
            AddOutputParam("结果文本", "string", ResultString);
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        #region Prop
        /// <summary>
        /// 数据信息
        /// </summary>
        public ObservableCollection<CreateStringModel> CreateString { get; set; } = new ObservableCollection<CreateStringModel>();

        private CreateStringModel _SelectedData = new CreateStringModel();
        /// <summary>
        /// 选中的文本
        /// </summary>
        public CreateStringModel SelectedData
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

        private string _DataFormat = "x={Value1}";
        /// <summary>
        /// 数据格式
        /// </summary>
        public string DataFormat
        {
            get { return _DataFormat; }
            set { Set(ref _DataFormat, value); }
        }

        private string _TrueReplace = "OK";
        /// <summary>
        /// True值替换
        /// </summary>
        public string TrueReplace
        {
            get { return _TrueReplace; }
            set { Set(ref _TrueReplace, value); }
        }
        private string _FalseReplace = "NG";
        /// <summary>
        /// False值替换
        /// </summary>
        public string FalseReplace
        {
            get { return _FalseReplace; }
            set { Set(ref _FalseReplace, value); }
        }

        private int _DataReserver = 1;
        /// <summary>
        /// 数据保留小数位
        /// </summary>
        public int DataReserver
        {
            get { return _DataReserver; }
            set { Set(ref _DataReserver, value); }
        }

        private string _ResultString;
        /// <summary>
        /// 结果文本
        /// </summary>
        public string ResultString
        {
            get { return _ResultString; }
            set { Set(ref _ResultString, value); }
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
                        var view = this.ModuleView as CreateStringView;
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
                    CreateString.Add(new CreateStringModel() { DataLinkText = LinkDataName, ID = CreateString.Count + 1, DataType = obj.DataType });
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
                        if (SelectedData == null) return;
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
                                CreateString.Remove(SelectedData);
                                break;
                            case "DownMove":
                                if ((Convert.ToInt32(SelectedData.ID)) < CreateString.Count)
                                    CreateString.Move(Convert.ToInt32(SelectedData.ID) - 1, Convert.ToInt32(SelectedData.ID));
                                break;
                            case "UpMove":
                                if ((Convert.ToInt32(SelectedData.ID) - 1) > 0)
                                    CreateString.Move(Convert.ToInt32(SelectedData.ID) - 1, Convert.ToInt32(SelectedData.ID) - 2);
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
            foreach (var Data in CreateString)
            {
                Data.ID = ID;
                ID += 1;
            }
        }
        #endregion
    }
}
