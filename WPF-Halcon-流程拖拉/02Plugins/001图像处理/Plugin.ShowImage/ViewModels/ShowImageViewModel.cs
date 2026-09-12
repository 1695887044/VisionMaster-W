using EventMgrLib;
using HalconDotNet;
using Plugin.ShowImage.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows.Forms;
using VM.Halcon;
using VM.Halcon.Config;
using VM.Halcon.Model;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Dialogs.Views;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.ViewModels;
using VM.Start.Views.Dock;

namespace Plugin.ShowImage.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        nImageIndex,
        InputImageLink,
    }
    #endregion

    [Category("图像处理")]
    [DisplayName("图像显示")]
    [ModuleImageName("ShowImage")]
    [Serializable]
    public class ShowImageViewModel : ModuleBase
    {

        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                if (nImageIndex == null || ImageParam.Count <= 0)
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                bool bImage = false;
                int nIndex = Convert.ToInt32(GetLinkValue(nImageIndex));
                for (int i = 0; i < ImageParam.Count; i++)
                {
                    if (nIndex == ImageParam[i].Index && ImageParam[i].InputImage.Text != "")
                    {
                        GetDispImage(ImageParam[i].InputImage.Text,true);
                        bImage = true;
                    }
                }
                if (DispImage == null || !DispImage.IsInitialized() || bImage == false)
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                ShowHRoi();
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
            if (DispImage == null)
                DispImage = new RImage();
            if (!DispImage.IsInitialized())
                DispImage.GenEmptyObj();
            base.AddOutputParams();
            AddOutputParam("图像", "HImage", DispImage);
        }
        #region Prop
        private LinkVarModel _nImageIndex = new LinkVarModel() { Text = "0" };
        /// <summary>
        /// 图像索引
        /// </summary>
        public LinkVarModel nImageIndex
        {
            get { return _nImageIndex; }
            set { _nImageIndex = value; RaisePropertyChanged(); }
        }

        private ObservableCollection<ImageParams> _ImageParam = new ObservableCollection<ImageParams>();
        /// <summary>
        /// 定义图像参数
        /// </summary>
        public ObservableCollection<ImageParams> ImageParam
        {
            get { return _ImageParam; }
            set { _ImageParam = value; RaisePropertyChanged(); }
        }

        private int _nSelectIndex;
        public int nSelectIndex
        {
            get { return _nSelectIndex; }
            set { Set(ref _nSelectIndex, value); }
        }

        /// <summary>显示结果区域</summary>
        private bool _ShowResultRoi = true;
        public bool ShowResultRoi
        {
            get { return _ShowResultRoi; }
            set { Set(ref _ShowResultRoi, value); }
        }
        private bool _ShowImage = true;
        /// <summary>
        /// 覆盖图像
        /// </summary>
        public bool ShowImage
        {
            get { return _ShowImage; }
            set
            {
                Set(ref _ShowImage, value);
            }
        }
        private bool _ShowOkLog;
        /// <summary>
        /// 显示OK日志
        /// </summary>
        public bool ShowOkLog
        {
            get { return _ShowOkLog; }
            set
            {
                Set(ref _ShowOkLog, value);
            }
        }
        private bool _ShowNgLog;
        /// <summary>
        /// 显示NG日志
        /// </summary>
        public bool ShowNgLog
        {
            get { return _ShowNgLog; }
            set
            {
                Set(ref _ShowNgLog, value);
            }
        }
        #endregion

        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as ShowImageView;
            if (view != null)
            {
                ClosedView = true;
                if (view.mWindowH == null)
                {
                    view.mWindowH = new VMHWindowControl();
                    view.winFormHost.Child = view.mWindowH;
                }
                if (DispImage != null && DispImage.IsInitialized())
                {
                    ShowHRoi();
                }
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
                        var view = this.ModuleView as ShowImageView;
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
                case "nImageIndex":
                    nImageIndex.Text = obj.LinkName;
                    break;
                case "InputImageLink":
                    ImageParam[nSelectIndex].InputImage.Text = obj.LinkName;
                    break;
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
                    //以GUID+类名作为筛选器
                    EventMgr.Ins.GetEvent<VarChangedEvent>().Subscribe(OnVarChanged, o => o.SendName.StartsWith($"{ModuleGuid}"));
                    _LinkCommand = new CommandBase((obj) =>
                    {
                        eLinkCommand linkCommand = (eLinkCommand)obj;
                        switch (linkCommand)
                        {
                            case eLinkCommand.nImageIndex:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},nImageIndex");
                                break;
                            case eLinkCommand.InputImageLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "HImage");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InputImageLink");
                                break;
                            default:
                                break;
                        }

                    });
                }
                return _LinkCommand;
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
                            case "Add":
                                ImageParam.Add(new ImageParams()
                                {
                                    Index = ImageParam.Count,
                                    LinkCommand = LinkCommand
                                });
                                break;
                            case "Delete":
                                if (nSelectIndex < 0) return;
                                ImageParam.RemoveAt(nSelectIndex);
                                break;
                            default:
                                break;
                        }
                    });
                }
                return _DataOperateCommand;
            }
        }
        #endregion
        #region Method
        private void ShowHRoi()
        {
            var view = ModuleView as ShowImageView;
            VMHWindowControl mWindowH;
            if (view == null || view.IsClosed)
            {
                mWindowH = ViewDic.GetView(DispImage.DispViewID);
                if (ShowImage)
                    mWindowH.HobjectToHimage(DispImage);
            }
            else
            {
                mWindowH = view.mWindowH;
                mWindowH.HobjectToHimage(DispImage);
            }
        }
        #endregion

    }
    [Serializable]
    public class ImageParams : NotifyPropertyBase
    {
        public int Index { get; set; }
        private LinkVarModel _InputImage = new LinkVarModel();
        public LinkVarModel InputImage
        {
            get { return _InputImage; }
            set { _InputImage = value; RaisePropertyChanged(); }
        }
        public CommandBase LinkCommand { get; set; }
    }
}
