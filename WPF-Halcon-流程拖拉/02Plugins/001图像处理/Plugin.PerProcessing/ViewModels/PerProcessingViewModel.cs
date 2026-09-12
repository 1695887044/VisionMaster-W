using EventMgrLib;
using HalconDotNet;
using HandyControl.Controls;
using Plugin.PerProcessing.Models;
using Plugin.PerProcessing.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Halcon;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.ViewModels;

namespace Plugin.PerProcessing.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        Roi
    }
    public enum eOperateCommand
    {
        StartLearn,
        Edit,
        EndLearn,
        Cancel
    }
    public enum eEditMode
    {
        正常显示,
        绘制涂抹,
        擦除涂抹,
    }
    public enum eDrawShape
    {
        矩形,
        圆形,
    }
    public enum eRoiType
    {
        全图,
        ROI链接,
    }
    #endregion

    [Category("图像处理")]
    [DisplayName("预先处理")]
    [ModuleImageName("PerProcessing")]
    [Serializable]
    public class PerProcessingViewModel : ModuleBase
    {
        public override void SetDefaultLink()
        {
            if (InputImageLinkText == null)
            {
                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "HImage");
                var moduls = VarLinkViewModel.Ins.Modules.LastOrDefault();
                if (moduls == null || moduls.VarModels.Count == 0)
                {
                    return;
                }
                InputImageLinkText = $"&{moduls.DisplayName}.{moduls.VarModels[0].Name}";
            }
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
                if (m_outImage == null)
                    m_outImage = new HImage();
                if (m_InImage == null)
                    m_InImage = new HImage();
                if (m_PretreatHelp == null)
                    m_PretreatHelp = new PretreatHelp();

                GetDispImage(InputImageLinkText);
                if (DispImage != null && DispImage.IsInitialized())
                    m_InImage = DispImage.CopyImage();
                HImage TempImage;

                if (m_InImage != null && m_InImage.IsInitialized())
                {
                    int i = 0;
                    foreach (var item in m_ToolList)
                    {
                        if (i > 0)
                        {
                            TempImage = new HImage(m_outImage);
                        }
                        else
                        {
                            TempImage =new HImage(m_InImage);
                        } 
                        switch (item.m_name)
                        {
                            //TODO：图像调整 - Obj
                            case eOperatorType.彩色转灰:
                                if (item.m_enable)
                                    m_PretreatHelp.TransImage(TempImage, out m_outImage, item.m_TransImageType,item.m_TransImageChannel);// m_MirrorImage);     
                                break;
                            case eOperatorType.图像镜像:
                                if (item.m_enable)
                                    m_PretreatHelp.MirrorImage(TempImage, out m_outImage, item.m_MirrorImageType);// m_MirrorImage);             
                                break;
                            case eOperatorType.图像旋转:
                                if (item.m_enable)
                                    m_PretreatHelp.RotateImage(TempImage, out m_outImage, item.m_RotateImageAngle);//m_RotateImageAngle);
                                break;
                            case eOperatorType.修改图像尺寸:
                                if (item.m_enable)
                                    m_PretreatHelp.ChangeFormat(TempImage, out m_outImage, item.m_ChangeImageWidth, item.m_ChangeImageHeight);//m_ChangeFormatWidth, m_ChangeFormatHeight);
                                break;
                            //TODO：滤波 - Obj
                            case eOperatorType.均值滤波:
                                if (item.m_enable)
                                    m_PretreatHelp.MeanImage(TempImage, out m_outImage, item.m_MeanImageWidth, item.m_MeanImageHeight);
                                break;
                            case eOperatorType.中值滤波:
                                if (item.m_enable)
                                    m_PretreatHelp.MedianImage(TempImage, out m_outImage, item.m_MedianImageWidth, item.m_MedianImageHeight);
                                break;
                            case eOperatorType.高斯滤波:
                                if (item.m_enable)
                                    m_PretreatHelp.GaussImage(TempImage, out m_outImage, item.m_GaussImageSize);
                                break;
                            //TODO：形态学运算 - Obj
                            case eOperatorType.灰度膨胀:
                                if (item.m_enable)
                                    m_PretreatHelp.GrayErosion(TempImage, out m_outImage, item.m_GrayErosionWidth, item.m_GrayErosionHeight);
                                break;
                            case eOperatorType.灰度腐蚀:
                                if (item.m_enable)
                                    m_PretreatHelp.GrayDilation(TempImage, out m_outImage, item.m_GrayDilationWidth, item.m_GrayDilationHeight);
                                break;
                            //TODO：图像增强 - Obj
                            case eOperatorType.锐化:
                                if (item.m_enable)
                                    m_PretreatHelp.EmphaSize(TempImage, out m_outImage, item.m_EmphaSizeWidth, item.m_EmphaSizeHeight, item.m_EmphaSizeFactor);
                                break;
                            case eOperatorType.对比度:
                                if (item.m_enable)
                                    m_PretreatHelp.Illuminate(TempImage, out m_outImage, item.m_IlluminateWidth, item.m_IlluminateHeight, item.m_IlluminateFactor);
                                break;
                            case eOperatorType.亮度调节:
                                if (item.m_enable)
                                    m_PretreatHelp.ScaleImage(TempImage, out m_outImage, item.m_ScaleImageMult, item.m_ScaleImageAdd);
                                break;
                            case eOperatorType.灰度开运算:
                                if (item.m_enable)
                                    m_PretreatHelp.Opening(TempImage, out m_outImage, item.m_OpeningWidth, item.m_OpeningHeight);
                                break;
                            case eOperatorType.灰度闭运算:
                                if (item.m_enable)
                                    m_PretreatHelp.Closing(TempImage, out m_outImage, item.m_ClosingWidth, item.m_ClosingHeight);
                                break;
                            case eOperatorType.反色:
                                if (item.m_enable)
                                    m_PretreatHelp.InvertImage(TempImage, out m_outImage, item.m_InvertImageLogic);
                                break;
                            //TODO：二值化 - Obj
                            case eOperatorType.二值化:
                                if (item.m_enable)
                                    m_PretreatHelp.Threshold(TempImage, out m_outImage, item.m_ThresholdLow, item.m_ThresholdHight, item.m_ThresholdReverse);
                                break;
                            case eOperatorType.均值二值化:
                                if (item.m_enable)
                                    m_PretreatHelp.VarThreshold(TempImage, out m_outImage, item.m_VarThresholdWidth, item.m_VarThresholdHeight, item.m_VarThresholdSkew, item.m_VarThresholdType);
                                break;
                        }
                        i++;
                    }
                  
                    //var view = ModuleView as PerProcessingView;
                    //if (view != null)
                    //{
                    //    if (m_outImage != null && m_outImage.IsInitialized())
                    //    {
                    //        view.mWindowH.Image = m_outImage;
                    //    }
                    //}
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
            base.AddOutputParams();
            AddOutputParam("预处理图像", "HImage", m_outImage);
        }
        #region Prop
        [NonSerialized]
        public HImage m_outImage;
        [NonSerialized]
        public HImage m_InImage;
        public PretreatHelp m_PretreatHelp = new PretreatHelp();
        
        /// <summary>
        /// 数据源
        /// </summary>
        public ObservableCollection<ModelData> m_ToolList { get; set; } = new ObservableCollection<ModelData>();

        private string _InputImageLinkText;
        /// <summary>
        /// 输入图像链接文本
        /// </summary>
        public string InputImageLinkText
        {
            get { return _InputImageLinkText; }
            set
            {
                _InputImageLinkText = value;
                RaisePropertyChanged();
                GetDispImage(InputImageLinkText);
            }
        }

        private ModelData _SelectedText = new ModelData();
        /// <summary>
        /// 选中的文本
        /// </summary>
        public ModelData SelectedText
        {
            get { return _SelectedText; }
            set { Set(ref _SelectedText, value); }
        }
        private int _SelectedIndex;
        /// <summary>
        /// 选中的序号
        /// </summary>
        public int SelectedIndex
        {
            get { return _SelectedIndex; }
            set { Set(ref _SelectedIndex, value); }
        }
        /// <summary>
        /// 搜索区域源
        /// </summary>
        private eRoiType _SelectedROIType = eRoiType.全图;
        public eRoiType SelectedROIType
        {
            get { return _SelectedROIType; }
            set
            {
                Set(ref _SelectedROIType, value, new Action(() =>
                {
                    //ShowHRoi();
                }));
            }
        }

        private string _InputRoiLinkText;
        /// <summary>
        /// 输入ROI链接文本
        /// </summary>
        public string InputRoiLinkText
        {
            get { return _InputRoiLinkText; }
            set { Set(ref _InputRoiLinkText, value); }
        }
        #endregion
        #region command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as PerProcessingView;
            if (view != null)
            {
                ClosedView = true;
                if (view.mWindowH == null)
                {
                    view.mWindowH = new VMHWindowControl();
                    view.winFormHost.Child = view.mWindowH;
                }
                if (DispImage == null || !DispImage.IsInitialized())
                {
                    SetDefaultLink();
                    if (InputImageLinkText == null) return;
                }
                GetDispImage(InputImageLinkText);
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
                        var view = ModuleView as PerProcessingView;
                        if (view == null) return;
                        if (m_outImage != null && m_outImage.IsInitialized())
                        {
                            view.mWindowH.Image = m_outImage;
                        }
                    });
                }
                return _ExecuteCommand;
            }
        }
        [NonSerialized]
        private CommandBase _ComposeCommand;
        public CommandBase ComposeCommand
        {
            get
            {
                if (_ComposeCommand == null)
                {
                    _ComposeCommand = new CommandBase((obj) =>
                    {
                        //ExeModule();
                        var view = ModuleView as PerProcessingView;
                        if (view == null) return;
                        if (m_InImage != null && m_InImage.IsInitialized())
                        {
                            view.mWindowH.Image = m_InImage;
                        }
                    });
                }
                return _ComposeCommand;
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
                        var view = this.ModuleView as PerProcessingView;
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
        private CommandBase _DataOperateCommand;
        public CommandBase DataOperateCommand
        {
            get
            {
                if (_DataOperateCommand == null)
                {
                    _DataOperateCommand = new CommandBase((obj) =>
                    {
                        string[] sArray = obj.ToString().Split('_');
                        if (sArray.Length == 2)
                        {
                            m_ToolList.Add(new ModelData()
                            {
                                m_name = (eOperatorType)Enum.Parse(typeof(eOperatorType), sArray[1])
                            });
                        }
                        else
                        {
                            switch (sArray[0])
                            {
                                case "remove":
                                    if (SelectedText == null) return;
                                    m_ToolList.Remove(SelectedText);
                                    break;
                                case "up":
                                    if (SelectedText == null) return;
                                    int i = m_ToolList.IndexOf(SelectedText);
                                    if (i > 0)
                                        m_ToolList.Move(i, i - 1);
                                    break;
                                case "down":
                                    if (SelectedText == null) return;
                                    int j = m_ToolList.IndexOf(SelectedText);
                                    if (j + 1 < m_ToolList.Count)
                                        m_ToolList.Move(j, j + 1);
                                    break;

                                default:
                                    break;
                            }
                        }
                        SelectedIndex = m_ToolList.Count - 1;
                    });
                }
                return _DataOperateCommand;
            }
        }

        private void OnVarChanged(VarChangedEventParamModel obj)
        {
            switch (obj.SendName.Split(',')[1])
            {
                case "InputImageLink":
                    InputImageLinkText = obj.LinkName;
                    break;
                case "InputRoiLink":
                    InputRoiLinkText = obj.LinkName;
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
                            case eLinkCommand.Roi:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "HRegion");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InputRoiLink");
                                break;
                            default:
                                break;
                        }

                    });
                }
                return _LinkCommand;
            }
        }
        #endregion
    }
}
