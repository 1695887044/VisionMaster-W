using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using EventMgrLib;
using Microsoft.Win32;
using Mono.CompilerServices.SymbolWriter;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Extension;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Communacation;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.PersistentData;
using VM.Start.Services;
using VM.Start.Views;
using WPFLocalizeExtension.Engine;

namespace VM.Start.ViewModels
{
    public class CommunicationSetViewModel : NotifyPropertyBase
    {
        #region Singleton

        private static readonly CommunicationSetViewModel _instance =
            new CommunicationSetViewModel();

        private CommunicationSetViewModel() { }

        public static CommunicationSetViewModel Ins
        {
            get { return _instance; }
        }
        #endregion
        #region Prop
        private int _Communication_SelectedIndex;

        public int Communication_SelectedIndex
        {
            get { return _Communication_SelectedIndex; }
            set
            {
                _Communication_SelectedIndex = value;
                RaisePropertyChanged();
            }
        }

        public List<ECommunacation> CommunicationModels
        {
            get { return EComManageer.GetEcomList(); }
        }
        private ECommunacation _CurrentCommunication = new ECommunacation();
        public ECommunacation CurrentCommunication
        {
            set
            {
                _CurrentCommunication = value;
                RaisePropertyChanged();
            }
            get { return _CurrentCommunication; }
        }
        private Array _CommunicationTypes = Enum.GetValues(typeof(eCommunicationType));

        public Array CommunicationTypes
        {
            get { return _CommunicationTypes; }
            set { _CommunicationTypes = value; }
        }
        private eCommunicationType _CommunicationType = eCommunicationType.TCP客户端;

        public eCommunicationType CommunicationType
        {
            get { return _CommunicationType; }
            set
            {
                _CommunicationType = value;
                RaisePropertyChanged();
            }
        }

        #endregion
        #region Command
        private CommandBase _DataOperateCommand;
        public CommandBase DataOperateCommand
        {
            get
            {
                if (_DataOperateCommand == null)
                {
                    _DataOperateCommand = new CommandBase(
                        (obj) =>
                        {
                            switch (obj)
                            {
                                case "Add":
                                    string name = "";
                                    switch (CommunicationType)
                                    {
                                        case eCommunicationType.TCP客户端:
                                            name = EComManageer.CreateECom(
                                                eCommunicationType.TCP客户端
                                            );
                                            break;
                                        case eCommunicationType.TCP服务器:
                                            name = EComManageer.CreateECom(
                                                eCommunicationType.TCP服务器
                                            );
                                            break;
                                        case eCommunicationType.串口通讯:
                                            name = EComManageer.CreateECom(eCommunicationType.串口通讯);
                                            break;
                                        case eCommunicationType.UDP通讯:
                                            name = EComManageer.CreateECom(
                                                eCommunicationType.UDP通讯
                                            );
                                            break;
                                        default:
                                            break;
                                    }
                                    break;
                                case "Delete":
                                    if (CurrentCommunication == null)
                                        return;
                                    EComManageer.DisConnect(CurrentCommunication.Key);
                                    EComManageer.s_ECommunacationDic.Remove(
                                        CurrentCommunication.Key
                                    );
                                    break;
                                default:
                                    break;
                            }
                            CommunicationSetView.Ins.dg.ItemsSource = CommunicationModels;
                            Communication_SelectedIndex = CommunicationModels.Count - 1;
                        }
                    );
                }
                return _DataOperateCommand;
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
                            Solution.Ins.UpdataCommunacation();
                            CommunicationSetView.Ins.Close();
                        }
                    );
                }
                return _ConfirmCommand;
            }
        }

        #endregion

        #region Method
        #endregion
    }
}
