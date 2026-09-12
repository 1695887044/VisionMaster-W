using EventMgrLib;
using Plugin.StartLaser.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Communacation;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.ViewModels;

namespace Plugin.StartLaser.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        offSetX,
        offSetY,
        offSetR
    }
    #endregion

    [Category("激光工具")]
    [DisplayName("触发激光")]
    [ModuleImageName("Delay")]
    [Serializable]
    public class StartLaserViewModel : ModuleBase
    {
        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                if (SelectedLaserModel == null)
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                double x = double.Parse(OffsetX.Value.ToString());
                double y = double.Parse(OffsetY.Value.ToString());
                double r = double.Parse(OffsetR.Value.ToString());
                bool ret = false;
                if (OffsetX.Text.StartsWith("&"))
                {
                    x = double.Parse(Prj.GetParamByName(OffsetX.Text).Value.ToString());
                }
                if (OffsetY.Text.StartsWith("&"))
                {
                    y = double.Parse(Prj.GetParamByName(OffsetY.Text).Value.ToString());
                }
                if (OffsetR.Text.StartsWith("&"))
                {
                    r = double.Parse(Prj.GetParamByName(OffsetR.Text).Value.ToString());
                }

                ret = SelectedLaserModel.StartMarkExt(x, y, r);

                Logger.AddLog($"开始激光,激光补偿：{x}，{y}，{r}");
                ChangeModuleRunStatus(ret ? eRunStatus.OK : eRunStatus.NG);

                return true;
            }
            catch (Exception ex)
            {
                ChangeModuleRunStatus(eRunStatus.NG);
                Logger.GetExceptionMsg(ex);
                return false;
            }
        }

        int _ErrorCode = 0;
        LinkVarModel _OffsetX = new LinkVarModel() { Text = "0.0" };
        LinkVarModel _OffsetY = new LinkVarModel() { Text = "0.0" };
        LinkVarModel _OffsetR = new LinkVarModel() { Text = "0.0" };
        ILaserDevice _SelectedLaserModel;
        public ObservableCollection<ILaserDevice> LaserModels { get; set; } =
            LaserSetViewModel.Ins.LaserModels;
        public LinkVarModel OffsetX
        {
            get => _OffsetX;
            set
            {
                _OffsetX = value;
                RaisePropertyChanged();
            }
        }
        public LinkVarModel OffsetY
        {
            get => _OffsetY;
            set
            {
                _OffsetY = value;
                RaisePropertyChanged();
            }
        }
        public LinkVarModel OffsetR
        {
            get => _OffsetR;
            set
            {
                _OffsetR = value;
                RaisePropertyChanged();
            }
        }
        public ILaserDevice SelectedLaserModel
        {
            get => _SelectedLaserModel;
            set
            {
                _SelectedLaserModel = value;
                RaisePropertyChanged();
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
                    //以GUID+类名作为筛选器
                    EventMgr.Ins
                        .GetEvent<VarChangedEvent>()
                        .Subscribe(OnVarChanged, o => o.SendName.StartsWith($"{ModuleGuid}"));
                    _LinkCommand = new CommandBase(
                        (obj) =>
                        {
                            CommonMethods.GetModuleList(
                                ModuleParam,
                                VarLinkViewModel.Ins.Modules,
                                "double"
                            );
                            EventMgr.Ins
                                .GetEvent<OpenVarLinkViewEvent>()
                                .Publish($"{ModuleGuid},{(eLinkCommand)obj}");
                        }
                    );
                }
                return _LinkCommand;
            }
        }

        public override void AddOutputParams()
        {
            base.AddOutputParams();
            AddOutputParam("错误码", "int", _ErrorCode);
        }

        [NonSerialized]
        private CommandBase _ExecuteCommand;
        public CommandBase ExecuteCommand
        {
            get
            {
                if (_ExecuteCommand == null)
                {
                    _ExecuteCommand = new CommandBase(
                        (obj) =>
                        {
                            ExeModule();
                            var view = ModuleView as StartLaserView;
                            if (view == null)
                                return;
                        }
                    );
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
                    _ConfirmCommand = new CommandBase(
                        (obj) =>
                        {
                            var view = this.ModuleView as StartLaserView;
                            if (view != null)
                            {
                                view.Close();
                            }
                        }
                    );
                }
                return _ConfirmCommand;
            }
        }

        private void OnVarChanged(VarChangedEventParamModel obj)
        {
            switch (obj.SendName.Split(',')[1])
            {
                case "offSetX":
                    OffsetX.Text = obj.LinkName;
                    break;
                case "offSetY":
                    OffsetY.Text = obj.LinkName;
                    break;
                case "offSetZ":
                    OffsetR.Text = obj.LinkName;
                    break;
                default:
                    break;
            }
        }
    }
}
