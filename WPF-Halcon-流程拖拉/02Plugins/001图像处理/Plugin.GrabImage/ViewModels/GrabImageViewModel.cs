using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Core;
using VM.Start.Attributes;
using VM.Start.Common.Provide;
using System.Windows.Controls;
using System.Windows.Input;
using VM.Start.Common.Helper;
using VM.Start.Common.Enums;
using HalconDotNet;
using Plugin.GrabImage.Views;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using Plugin.GrabImage.Model;
using System.Windows.Forms;
using System.IO;
using Microsoft.WindowsAPICodePack.Dialogs;
using VM.Start.Views.Dock;
using VM.Halcon;
using VM.Start.Services;
using VM.Halcon.Config;
using HandyControl.Controls;
using VM.Halcon.Model;
using VM.Start.Views;
using VM.Start.Models;
using VM.Start.ViewModels;
using VM.Start.Events;
using VM.Start.Common;
using EventMgrLib;

namespace Plugin.GrabImage.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        ExposureTime,
        Gain
    }
    #endregion

    [Category("图像处理")]
    [DisplayName("采集图像")]
    [ModuleImageName("GrabImage")]
    [Serializable]
    public class GrabImageViewModel : ModuleBase
    {
        public override bool  ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                if (DispImage !=null && DispImage.IsInitialized())
                {
                    DispImage.Dispose();
                }
                DispImage = new RImage(DispViewID);
                switch (ImageSource)
                {
                    case eImageSource.指定图像:
                        if (!File.Exists(ImagePath))
                        {
                            break;
                        }
                        DispImage.ReadImage(ImagePath);
                        break;
                    case eImageSource.文件目录:
                        if (ImageNameModels == null || ImageNameModels.Count == 0)
                        {
                            break;
                        }
                        if (IsCyclicRead)
                        {
                            SelectedIndex++;
                            SelectedIndex = SelectedIndex >= ImageNameModels.Count ? 0 : SelectedIndex;
                        }
                        if (File.Exists(ImageNameModels[SelectedIndex].ImagePath))
                        {
                            DispImage.ReadImage(ImageNameModels[SelectedIndex].ImagePath);
                        }

                        break;
                    case eImageSource.相机采集:
                        if (SelectedCameraModel.Connected)
                        {
                            SelectedCameraModel.ExposeTime = int.Parse(ExposureTime.Value.ToString());
                            SelectedCameraModel.Gain = int.Parse(Gain.Value.ToString());
                            SelectedCameraModel.SetExposureTime(SelectedCameraModel.ExposeTime);
                            SelectedCameraModel.SetGain(SelectedCameraModel.Gain);
                            if (AcquisitionMode == eTrigMode.下降沿)
                            {
                                SelectedCameraModel.TrigMode = eTrigMode.下降沿;
                                SelectedCameraModel.SetTriggerMode(eTrigMode.下降沿);
                            }
                            else if (AcquisitionMode == eTrigMode.上升沿)
                            {
                                SelectedCameraModel.TrigMode = eTrigMode.上升沿;
                                SelectedCameraModel.SetTriggerMode(eTrigMode.上升沿);
                            }
                            else
                            {
                                SelectedCameraModel.TrigMode = eTrigMode.软触发;
                                SelectedCameraModel.SetTriggerMode(eTrigMode.软触发);
                            }
                            SelectedCameraModel.EventWait.Reset();
                            SelectedCameraModel.CaptureImage(false);
                            SelectedCameraModel.EventWait.WaitOne();

                            if (SelectedCameraModel.Image != null && SelectedCameraModel.Image.IsInitialized())
                            {
                                int viewId = DispImage.DispViewID;
                                DispImage = new RImage(SelectedCameraModel.Image);
                                DispImage.DispViewID = viewId;
                            }

                            SelectedCameraModel.TrigMode = eTrigMode.软触发;
                            SelectedCameraModel.SetTriggerMode(eTrigMode.软触发);
                            //ChangeModuleRunStatus(eRunStatus.OK);
                        }
                        else
                        {
                            Logger.AddLog(ModuleParam.ModuleName + ":" + SelectedCameraModel.CameraNo + "相机未连接!", eMsgType.Warn);
                            //ChangeModuleRunStatus(eRunStatus.NG);
                        }
                        break;
                    default:
                        break;
                }
                if (DispImage != null && DispImage.IsInitialized())
                {
                    VMHWindowControl mWindowH;
                    if (ModuleView == null || ModuleView.IsClosed)
                    {
                        mWindowH = ViewDic.GetView(DispImage.DispViewID);
                    }
                    else
                    {
                        mWindowH = ModuleView.mWindowH;
                    }
                    if (mWindowH != null)
                    {
                        mWindowH.ClearWindow();
                        mWindowH.Image = new HImage(DispImage);
                    }

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
            AddOutputParam("图像", "HImage", DispImage);
        }
        #region Prop
        /// <summary>图像索引</summary>
        public int _SelectedIndex = 0;
        public int SelectedIndex
        {
            get { return _SelectedIndex; }
            set
            {
                Set(ref _SelectedIndex, value);
            }
        }

        private bool _IsCyclicRead = true;
        /// <summary>
        /// 循环读取
        /// </summary>
        public bool IsCyclicRead
        {
            get { return _IsCyclicRead; }
            set
            {
                Set(ref _IsCyclicRead, value);
            }
        }
        private string _ImagePath;
        /// <summary>
        /// 图片路径
        /// </summary>
        public string ImagePath
        {
            get { return _ImagePath; }
            set { _ImagePath = value; RaisePropertyChanged(); }
        }
        private string _FilePath;
        /// <summary>
        /// 图片文件夹路径
        /// </summary>
        public string FilePath
        {
            get { return _FilePath; }
            set { _FilePath = value; RaisePropertyChanged(); }
        }
        public List<string> CanvasList
        {
            get { return base.CanvasList; }
            set { base.CanvasList = value; RaisePropertyChanged(); }
        }
        public int DispViewID
        {
            get { return base.DispViewID; }
            set { base.DispViewID = value; RaisePropertyChanged(); }
        }
        /// <summary>
        /// 采集魔术数据,用来显示列表数据源
        /// </summary>

        [field: NonSerialized]
        public Array AcquisitionModes
        {
            get { return Enum.GetValues(typeof(eTrigMode)); }
            set {; }
        }
        /// <summary>
        /// 当前采集模式
        /// </summary>
        private eTrigMode _AcquisitionMode = eTrigMode.软触发;
        public eTrigMode AcquisitionMode
        {
            get { return _AcquisitionMode; }
            set
            {
                Set(ref _AcquisitionMode, value, new Action(() =>
                {
                }));
            }
        }
        /// <summary>
        /// 曝光时间
        /// </summary>
        private LinkVarModel _ExposureTime = new LinkVarModel() { Text = "10000" };
        public LinkVarModel ExposureTime
        {
            get { return _ExposureTime; }
            set { _ExposureTime = value; RaisePropertyChanged(); }
        }

        /// <summary>
        /// 增益
        /// </summary>
        private LinkVarModel _Gain = new LinkVarModel() { Text = "0" };

        public LinkVarModel Gain
        {
            get { return _Gain; }
            set { _Gain = value; RaisePropertyChanged(); }
        }


        private string _ContentHeader ="文件目录";
        /// <summary>
        /// 内容标题
        /// </summary>
        public string ContentHeader
        {
            get { return _ContentHeader; }
            set { _ContentHeader = value; RaisePropertyChanged(); }
        }

        private eImageSource _ImageSource = eImageSource.指定图像;
        /// <summary>
        /// 指定图像
        /// </summary>
        public eImageSource ImageSource
        {
            get { return _ImageSource; }
            set
            {
                Set(ref _ImageSource, value, new Action(() =>
                {
                    switch (_ImageSource)
                    {
                        case eImageSource.指定图像:
                            ContentHeader = "指定图像";
                            break;
                        case eImageSource.文件目录:
                            ContentHeader = "文件目录";
                            break;
                        case eImageSource.相机采集:
                            ContentHeader = "相机采集";
                            break;
                        default:
                            break;
                    }
                }));
            }
        }
        private bool _SpecifiedImage_SelectFile = true;
        /// <summary>
        /// 选择文件
        /// </summary>
        public bool SpecifiedImage_SelectFile
        {
            get { return _SpecifiedImage_SelectFile; }
            set
            {
                Set(ref _SpecifiedImage_SelectFile, value);
            }
        }
        private bool _SpecifiedImage_LinkPath;
        /// <summary>
        /// 链接路径
        /// </summary>
        public bool SpecifiedImage_LinkPath
        {
            get { return _SpecifiedImage_LinkPath; }
            set
            {
                Set(ref _SpecifiedImage_LinkPath, value);
            }
        }
        public ObservableCollection<ImageNameModel> ImageNameModels { get; set; }=new ObservableCollection<ImageNameModel>();
        /// <summary>相机列表</summary>
        public ObservableCollection<CameraBase> CameraModels { get; set; } = CameraSetViewModel.Ins.CameraModels;
        private CameraBase _SelectedCameraModel = new CameraBase();

        public CameraBase SelectedCameraModel
        {
            get { return _SelectedCameraModel; }
            set { _SelectedCameraModel = value; RaisePropertyChanged(); }
        }

        #endregion

        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as GrabImageView;
            if (view != null)
            {
                if (view.mWindowH == null)
                {
                    view.mWindowH = new VMHWindowControl();
                    view.winFormHost.Child = view.mWindowH;
                }
            }
        }
        [NonSerialized]
        private CommandBase _ImagePathCommand;
        public CommandBase ImagePathCommand
        {
            get
            {
                if (_ImagePathCommand == null)
                {
                    _ImagePathCommand = new CommandBase((obj) =>
                    {
                        Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
                        dlg.Filter = "所有图像文件 | *.bmp; *.pcx; *.png; *.jpg; *.gif;*.tif; *.ico; *.dxf; *.cgm; *.cdr; *.wmf; *.eps; *.emf";
                        if (dlg.ShowDialog() == true)
                        {
                            ImagePath = dlg.FileName;
                            ExeModule();
                            if (DispImage != null && DispImage.IsInitialized())
                            {
                                var view = ModuleView as GrabImageView;
                                if (view == null) return;
                                view.mWindowH.Image = DispImage;
                            }
                        }

                    });
                }
                return _ImagePathCommand;
            }
        }
        [NonSerialized]
        private CommandBase _FilePathCommand;
        public CommandBase FilePathCommand
        {
            get
            {
                if (_FilePathCommand == null)
                {
                    _FilePathCommand = new CommandBase((obj) =>
                    {
                        CommonOpenFileDialog dialog = new CommonOpenFileDialog{IsFolderPicker = true};

                        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                        {
                            FilePath = dialog.FileName;
                            var files = Directory.GetFiles(FilePath, "*.*", SearchOption.AllDirectories).Where(s => s.EndsWith(".jpg") || s.EndsWith(".gif") || s.EndsWith(".png") || s.EndsWith(".bmp") || s.EndsWith(".jpg") || s.EndsWith(".eps") || s.EndsWith(".tif"));
                            if (files.Any())
                            {
                                var names = files.ToList();
                                ImageNameModels.Clear();
                                for (int i = 0; i < names.Count; i++)
                                {
                                    ImageNameModels.Add(new ImageNameModel() { ID = i + 1,ImagePath=names[i], ImageName = Path.GetFileName(names[i]), IsSelected = true });
                                }
                                DispImage = new RImage(DispViewID);
                                if (ImageNameModels!=null && ImageNameModels.Count>0 && File.Exists(ImageNameModels[0].ImagePath))
                                {
                                    SelectedIndex = 0;
                                    DispImage.ReadImage(ImageNameModels[0].ImagePath);
                                    if (DispImage != null && DispImage.IsInitialized())
                                    {
                                        var view = ModuleView as GrabImageView;
                                        if (view == null) return;
                                        view.mWindowH.Image = DispImage;
                                    }
                                }
                                if (DispImage != null && DispImage.IsInitialized())
                                {
                                    var view = ModuleView as GrabImageView;
                                    if (view == null || view.IsClosed)
                                    {
                                        ViewDic.GetView(DispViewID).Image = DispImage;
                                    }
                                    ChangeModuleRunStatus(eRunStatus.OK);
                                }
                                else
                                {
                                    ChangeModuleRunStatus(eRunStatus.NG);
                                }

                            }
                        }

                    });
                }
                return _FilePathCommand;
            }
        }

        [NonSerialized]
        private CommandBase _SpecifiedImage_LinkPathCommand;
        public CommandBase SpecifiedImage_LinkPathCommand
        {
            get
            {
                if (_SpecifiedImage_LinkPathCommand == null)
                {
                    _SpecifiedImage_LinkPathCommand = new CommandBase((obj) =>
                    {
                    });
                }
                return _SpecifiedImage_LinkPathCommand;
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
                        var view = ModuleView as GrabImageView;
                        if (view == null) return;
                        if (DispImage!=null && DispImage.IsInitialized())
                        {
                            view.mWindowH.Image = DispImage;
                        }
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
                        var view = this.ModuleView as GrabImageView;
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
        private CommandBase _PreviewMouseDoubleClick_FilePath;
        public CommandBase PreviewMouseDoubleClick_FilePath
        {
            get
            {
                if (_PreviewMouseDoubleClick_FilePath == null)
                {
                    _PreviewMouseDoubleClick_FilePath = new CommandBase((obj) =>
                    {
                        DispImage = new RImage(DispViewID);
                        if (File.Exists(ImageNameModels[SelectedIndex].ImagePath))
                        {
                            DispImage.ReadImage(ImageNameModels[SelectedIndex].ImagePath);
                            if (DispImage != null && DispImage.IsInitialized())
                            {
                                var view = ModuleView as GrabImageView;
                                if (view == null) return;
                                view.mWindowH.Image = DispImage;
                            }
                        }
                        if (DispImage != null && DispImage.IsInitialized())
                        {
                            var view = ModuleView as GrabImageView;
                            if (view == null || view.IsClosed)
                            {
                                ViewDic.GetView(DispViewID).Image = DispImage;
                            }
                            ChangeModuleRunStatus(eRunStatus.OK);
                        }
                        else
                        {
                            ChangeModuleRunStatus(eRunStatus.NG);
                        }
                    });
                }
                return _PreviewMouseDoubleClick_FilePath;
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
                            case eLinkCommand.ExposureTime:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},ExposureTimeLink");
                                break;
                            case eLinkCommand.Gain:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "int");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},GainLink");
                                break;
                            default:
                                break;
                        }

                    });
                }
                return _LinkCommand;
            }
        }
        private void OnVarChanged(VarChangedEventParamModel obj)
        {
            switch (obj.SendName.Split(',')[1])
            {
                case "ExposureTimeLink":
                    ExposureTime.Text= obj.LinkName;
                    break;
                case "GainLink":
                    Gain.Text = obj.LinkName;
                    break;
                default:
                    break;
            }
        }

        #endregion

    }
}
