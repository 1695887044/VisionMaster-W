using EventMgrLib;
using HalconDotNet;
using HslCommunication;
using HslCommunication.ModBus;
using Plugin.PLCCommunicate.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
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
using VM.Start.Views.Dock;

namespace Plugin.PLCCommunicate.ViewModels
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
    [DisplayName("PLC通讯")]
    [ModuleImageName("PLCCommunicate")]
    [Serializable]
    public class PLCCommunicateViewModel : ModuleBase
    {
        public override void SetDefaultLink()
        {
        }

        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                if (!EComManageer.GetKeys().Contains(CurKey))
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                else
                {
                    EComManageer.setIsPLC(CurKey, true); //设置为PLC模式
                    bool res = EComManageer.Connect(CurKey);
                    if (res)
                        ChangeModuleRunStatus(eRunStatus.OK);
                    else
                        ChangeModuleRunStatus(eRunStatus.NG);
                    return res;
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
            base.AddOutputParams();
        }
        #region Prop

        /// <summary>
        /// PLC类型
        /// </summary>
        [field: NonSerialized]
        public Array PlcCommunicationTypes
        {
            get { return Enum.GetValues(typeof(PLCType)); }
        }
        private PLCType _PlcCommunicationType = PLCType.ModbusTCP;
        public PLCType PlcCommunicationType
        {
            get { return _PlcCommunicationType; }
            set { Set(ref _PlcCommunicationType, value); }
        }
        /// <summary>
        /// 数据解析格式
        /// </summary>
        [field: NonSerialized]
        public Array PLCDataFormats
        {
            get { return Enum.GetValues(typeof(PLCDataType)); }
        }
        private PLCDataType _PLCDataFormat = PLCDataType.CDAB;
        public PLCDataType PLCDataFormat
        {
            get { return _PLCDataFormat; }
            set { Set(ref _PLCDataFormat, value); }
        }
        /// <summary>
        /// 站号
        /// </summary>
        private int _StationNumber = 1;
        public int StationNumber
        {
            get { return _StationNumber; }
            set { Set(ref _StationNumber, value); }
        }

        /// <summary>
        /// 地址是否从零开始
        /// </summary>
        private bool _AddressStartWithZero = false;
        public bool AddressStartWithZero
        {
            get { return _AddressStartWithZero; }
            set { Set(ref _AddressStartWithZero, value); }
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
                if (_CurKey == null) return;
                if (EComManageer.GetKeys().Contains(_CurKey))
                {
                    EComManageer.s_ECommunacationDic[_CurKey].IsPLC = false;      //把之前的plc标志位复位
                    EComManageer.s_ECommunacationDic[_CurKey].m_connectKey = "";
                    Logger.AddLog("1:" + _CurKey);
                }                
                Set(ref _CurKey, value, new Action(() =>
                {
                    Remarks = EComManageer.GetRemarks(_CurKey);
                    EComManageer.s_ECommunacationDic[_CurKey].IsPLC = true;     //设置新的PLC标志
                    EComManageer.s_ECommunacationDic[_CurKey].m_connectKey = getKey();
                    Logger.AddLog("2:" + _CurKey);
                }));
            }
        }
        /// <summary>
        /// 备注
        /// </summary>
        private string _Remarks;
        public string Remarks
        {
            get { return _Remarks; }
            set { Set(ref _Remarks, value); }
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

        /// <summary>
        /// PLC int数据长度
        /// </summary>
        private PLCIntDataLengthEnum _IntDataLength = PLCIntDataLengthEnum._32位;
        public PLCIntDataLengthEnum IntDataLength
        {
            get { return _IntDataLength; }
            set { Set(ref _IntDataLength, value); }
        }
        /// <summary>
        /// PLC double数据长度
        /// </summary>
        private PLCDoubleDataLengthEnum _DoubleDataLength = PLCDoubleDataLengthEnum._32位;
        public PLCDoubleDataLengthEnum DoubleDataLength
        {
            get { return _DoubleDataLength; }
            set { Set(ref _DoubleDataLength, value); }
        }

        #endregion
        #region Command
        public override void Loaded()
        {
            loadData();
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
                        EComManageer.DisConnect(CurKey);
                        saveData();
                        EComManageer.Connect(CurKey);
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
                        var view = this.ModuleView as PLCCommunicateView;
                        if (view != null)
                        {
                            EComManageer.DisConnect(CurKey);
                            saveData();
                            EComManageer.Connect(CurKey);
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
                default:
                    break;
            }
        }
        [NonSerialized]
        private CommandBase _LinkCommand;
        public CommandBase LinkCommand
        {
            get
            {
                if (_LinkCommand == null)
                {
                    _LinkCommand = new CommandBase((obj) =>
                    {
                    });
                }
                return _LinkCommand;
            }
        }
        #endregion
        #region Method
        public string getKey()
        {
            return Prj.ProjectInfo.ProjectID.ToString() + "." + ModuleParam.ModuleName;
        }
        public void saveData()
        {
            if (!EComManageer.GetKeys().Contains(CurKey)) return;
            EComManageer.s_ECommunacationDic[CurKey].m_StationCode = StationNumber;
            EComManageer.s_ECommunacationDic[CurKey].m_StartWithZero = AddressStartWithZero;
            EComManageer.s_ECommunacationDic[CurKey].m_PLCType = PlcCommunicationType;
            EComManageer.s_ECommunacationDic[CurKey].m_PLCDataType = PLCDataFormat;
            EComManageer.s_ECommunacationDic[CurKey].m_IntNumber = IntDataLength;
            EComManageer.s_ECommunacationDic[CurKey].m_DoubleNumber = DoubleDataLength;
        }
        public void loadData()
        {
            if (!EComManageer.GetKeys().Contains(CurKey)) return;
            StationNumber = EComManageer.s_ECommunacationDic[CurKey].m_StationCode;
            AddressStartWithZero = EComManageer.s_ECommunacationDic[CurKey].m_StartWithZero;
            PlcCommunicationType = EComManageer.s_ECommunacationDic[CurKey].m_PLCType;
            PLCDataFormat = EComManageer.s_ECommunacationDic[CurKey].m_PLCDataType;
            IntDataLength = EComManageer.s_ECommunacationDic[CurKey].m_IntNumber;
            DoubleDataLength = EComManageer.s_ECommunacationDic[CurKey].m_DoubleNumber;
        }
        #endregion
    }
}
