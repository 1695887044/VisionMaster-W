using EventMgrLib;
using HalconDotNet;
using Plugin.CRCode.Views;
using System;
using System.Collections.Generic;
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

namespace Plugin.CRCode.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        OutputStr
    }
    public enum eOperateCommand
    {
        StartLearn,
        Edit,
        EndLearn,
        Cancel
    }
  
    public enum CodeType
    {
        QR,
        DM,
    }
    #endregion

    [Category("检测识别")]
    [DisplayName("二维码")]
    [ModuleImageName("QRCode")]
    [Serializable]
    public class QRCodeViewModel : ModuleBase
    {
        public override void SetDefaultLink()
        {
            CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "HImage");
            var moduls = VarLinkViewModel.Ins.Modules.LastOrDefault();
            if (moduls == null || moduls.VarModels.Count == 0)
            {
                return;
            }
            InputImageLinkText = $"&{moduls.DisplayName}.{moduls.VarModels[0].Name}";
        }

        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                if (InputImageLinkText == null)
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                GetDispImage(InputImageLinkText);
                if (DispImage == null || !DispImage.IsInitialized())
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }

                HObject hObject = new HObject();
                hObject.GenEmptyObj();
                HTuple hv_ResuleHandle = new HTuple();
                HTuple hv_ResultStr = new HTuple();
                hObject.Dispose(); hv_ResuleHandle.Dispose(); hv_ResultStr.Dispose();
                HOperatorSet.FindDataCode2d(DispImage, out hObject, _DataCodeHandle, new HTuple(), new HTuple(), out hv_ResuleHandle, out hv_ResultStr);
                if (hv_ResultStr.Length <= 0)
                {
                    return false;
                }
                OutPutStr = hv_ResultStr.S;
                hObject.Dispose(); hv_ResuleHandle.Dispose(); hv_ResultStr.Dispose();
                #region 显示文字
                var view = ModuleView as QRCodeView;
                HTuple hv_WindowHandle;
                if (view == null || view.IsClosed)
                {
                    hv_WindowHandle = ViewDic.GetView(DispViewID).hControl.HalconWindow;
                }
                else
                {
                    hv_WindowHandle = view.mWindowH.hControl.HalconWindow;
                }
                string showText = OutPutStr;
                ShowTool.SetFont(hv_WindowHandle, 30, "false", "false");
                ShowTool.SetMsg(hv_WindowHandle, showText, "image", 20, 40, "green", "false");
                #endregion


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
            AddOutputParam("二维码信息", "string", OutPutStr);
          
        }
        #region Prop
        private string _OutPutStr;
        public string OutPutStr
        {
            set { _OutPutStr = value; }
            get { return _OutPutStr; }
        }
        [NonSerialized]
        public QRCodeView view;
        /// <summary> 区域列表 </summary>
        public Dictionary<string, ROI> RoiList = new Dictionary<string, ROI>();
        private string _InputImageLinkText;
        /// <summary>
        /// 输入图像链接文本
        /// </summary>
        public string InputImageLinkText
        {
            get { return _InputImageLinkText; }
            set { Set(ref _InputImageLinkText, value); }
        }
        public Array CodeTypes { get; set; } = Enum.GetValues(typeof(CodeType));
        private CodeType _CodeType = CodeType.QR;
        
        public CodeType CodeType
        {
            get { return _CodeType; }
            set { _CodeType = value; }
        }

        private HTuple _DataCodeHandle;

        public HTuple DataCodeHandle
        {
            get { return _DataCodeHandle; }
            set { _DataCodeHandle = value; }
        }
        /// <summary>显示结果区域</summary>
        private bool _ShowResultRoi = true;
        public bool ShowResultRoi
        {
            get { return _ShowResultRoi; }
            set { Set(ref _ShowResultRoi, value); }
        }
        #endregion

        #region Command
        public override void Loaded()
        {
            base.Loaded();
            view = ModuleView as QRCodeView;
            if (view != null)
            {
                ClosedView = true;
                if (view.mWindowH == null)
                {
                    view.mWindowH = new VMHWindowControl();
                    view.winFormHost.Child = view.mWindowH;
                }
                SetDefaultLink();
                if (InputImageLinkText == null) return;
                GetDispImage(InputImageLinkText);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    view.mWindowH.hControl.MouseUp += HControl_MouseUp;
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
        private CommandBase _TeachCommand;
        public CommandBase TeachCommand
        {
            get
            {
                if (_TeachCommand == null)
                {
                    _TeachCommand = new CommandBase((obj) =>
                    {
                        if (DispImage != null && DispImage.IsInitialized())
                        {
                            switch (CodeType)
                            {
                                case CodeType.QR:
                                    HOperatorSet.CreateDataCode2dModel("QR Code", "default_parameters", "maximum_recognition", out _DataCodeHandle);
                                    break;
                                case CodeType.DM:
                                    HOperatorSet.CreateDataCode2dModel("Data Matrix ECC 200", "default_parameters", "maximum_recognition", out _DataCodeHandle);
                                    break;
                                default:
                                    break;
                            }

                            HObject hObject = new HObject();
                            hObject.GenEmptyObj();
                            HTuple hv_ResuleHandle = new HTuple();
                            HTuple hv_ResultStr = new HTuple();
                            hObject.Dispose(); hv_ResuleHandle.Dispose(); hv_ResultStr.Dispose();
                            HOperatorSet.FindDataCode2d(DispImage, out hObject, _DataCodeHandle, new HTuple(), new HTuple(), out hv_ResuleHandle, out hv_ResultStr);
                            if (hv_ResultStr.Length <= 0) return;
                            OutPutStr = hv_ResultStr.S;
                            hObject.Dispose(); hv_ResuleHandle.Dispose(); hv_ResultStr.Dispose();
                            #region 显示文字
                            var view = ModuleView as QRCodeView;
                            HTuple hv_WindowHandle;
                            if (view == null || view.IsClosed)
                            {
                                hv_WindowHandle = ViewDic.GetView(DispViewID).hControl.HalconWindow;
                            }
                            else
                            {
                                hv_WindowHandle = view.mWindowH.hControl.HalconWindow;
                            }
                            string showText = OutPutStr;
                            ShowTool.SetFont(hv_WindowHandle, 30, "false", "false");
                            ShowTool.SetMsg(hv_WindowHandle, showText, "image", 20, 40, "green", "false");
                            #endregion

                        }

                    });
                }
                return _TeachCommand;
            }
        }
        private void OnVarChanged(VarChangedEventParamModel obj)
        {
            switch (obj.SendName.Split(',')[1])
            {
                case "InputImageLink":
                    InputImageLinkText = obj.LinkName;
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
                            case eLinkCommand.InputImageLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "HImage");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InputImageLink");
                                break;
                            case eLinkCommand.OutputStr:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "string");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},MathNumLink");
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
        private CommandBase _ClearPaintCommand;
        public CommandBase ClearPaintCommand
        {
            get
            {
                if (_ClearPaintCommand == null)
                {
                    _ClearPaintCommand = new CommandBase((obj) =>
                    {
                        view.mWindowH.HobjectToHimage(DispImage);
                    });
                }
                return _ClearPaintCommand;
            }
        }


        #endregion

        #region Method
        private void HControl_MouseUp(object sender, MouseEventArgs e)
        {
      
        }

        private void ShowHRoi()
        {
            var view = ModuleView as QRCodeView;
            VMHWindowControl mWindowH;
            if (view == null || view.IsClosed)
            {
                mWindowH = ViewDic.GetView(DispImage.DispViewID);
            }
            else
            {
                mWindowH = view.mWindowH;
            }
            mWindowH.HobjectToHimage(DispImage);
            if (view == null || view.IsClosed)
            {
                List<HRoi> roiList = DispImage.mHRoi.Where(c => c.ModuleName == ModuleParam.ModuleName && c.ModuleEncode == ModuleParam.ModuleEncode).ToList();
                foreach (HRoi roi in roiList)
                {
                    if (roi.roiType == HRoiType.文字显示)
                    {
                        HText roiText = (HText)roi;
                        ShowTool.SetFont(mWindowH.hControl.HalconWindow, roiText.size, "false", "false");
                        ShowTool.SetMsg(mWindowH.hControl.HalconWindow, roiText.text, "image", roiText.row, roiText.col, roiText.drawColor, "false");
                    }
                    else
                    {
                        mWindowH.WindowH.DispHobject(roi.hobject, roi.drawColor);
                    }
                }
            }
            else
            {
              
            }
        }

        private void SetBurshRegion()
        {
         

        }
   
        #endregion

    }
}
