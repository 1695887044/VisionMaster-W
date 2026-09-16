using EventMgrLib;
using Plugin.SaveImage.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Helper;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.ViewModels;
using Microsoft.WindowsAPICodePack.Dialogs;
using VM.Start.Models;
using VM.Start.Common.Enums;
using VM.Start.Common.Provide;
using System.IO;
using HalconDotNet;
using VM.Halcon;
using VM.Start.Views.Dock;
using VM.Start.Services;

namespace Plugin.SaveImage.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputImageLink,
        PictureName
    }
    public enum eSaveShape
    {
        原图,
        截图,
    }
    #endregion
    [Category("图像处理")]
    [DisplayName("存储图像")]
    [ModuleImageName("SaveImage")]
    [Serializable]
    public class SaveImageViewModel : ModuleBase
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
                if (InputImageLinkText == null || InputImageLinkText == "")
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                GetDispImage(InputImageLinkText);
                if (DispImage != null && DispImage.IsInitialized()&& FilePath!="")
                {
                    if(!Directory.Exists(FilePath))
                    {
                        Directory.CreateDirectory(FilePath);
                    }
                    string ImageNames = "";
                    DateTime dt = DateTime.Now;
                    ImageNames = FilePath + "\\" + dt.ToString("yyyyMMdd");
                    if (!Directory.Exists(ImageNames))
                    {
                        Directory.CreateDirectory(ImageNames);
                    }
                    if (ImageNameTime)
                    {
                     
                        ImageNames+= "\\" + dt.ToString("HHmmss") + "."+ ImageStytleList[SelectedIndex];
                    }
                    else
                    {
                        string LinkName = GetLinkValue(PicName).ToString();
                        if (LinkName == "")
                            LinkName = "1";
                        ImageNames += "\\" + LinkName + "." + ImageStytleList[SelectedIndex];
                    }
                    switch (SelectedSaveType)
                    {
                        case eSaveShape.原图:
                            HOperatorSet.WriteImage(DispImage, ImageStytleList[SelectedIndex], 0, ImageNames);
                            break;
                        case eSaveShape.截图:
                            VMHWindowControl mWindowH = ViewDic.GetView(DispImage.DispViewID);
                            HOperatorSet.DumpWindow(mWindowH.hControl.HalconWindow, ImageStytleList[SelectedIndex], ImageNames);
                            //HObject ho_Image = new HObject();
                            //HOperatorSet.DumpWindowImage(out ho_Image, mWindowH.hControl.HalconWindow);
                            //HOperatorSet.WriteImage(ho_Image, ImageStytleList[SelectedIndex], 0, ImageNames);
                            break;
                        default:
                            break;
                    }
                    if(SaveDay.ToString()!="")
                    {
                        RemovImage(FilePath, SaveDay);
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

        }
        #region Prop
        private string _InputImageLinkText;
        /// <summary>
        /// 输入图像链接文本
        /// </summary>
        public string InputImageLinkText
        {
            get { return _InputImageLinkText; }
            set { Set(ref _InputImageLinkText, value); }
        }

        private eSaveShape _SelectedSaveType = eSaveShape.原图;
        /// <summary>
        /// 保存类型
        /// </summary>
        public eSaveShape SelectedSaveType
        {
            get { return _SelectedSaveType; }
            set
            {
                Set(ref _SelectedSaveType, value, new Action(() =>
                {
                    
                }));
            }
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
        public List<string> ImageStytleList { get; set; } = new List<string>() { "bmp", "jpg","png", "tiff", "gif" };
        /// <summary>图像类型索引</summary>
        public int _SelectedIndex = 0;
        public int SelectedIndex
        {
            get { return _SelectedIndex; }
            set
            {
                Set(ref _SelectedIndex, value);
            }
        }
        public int _SaveDay = 3;
        public int SaveDay
        {
            get { return _SaveDay; }
            set
            {
                Set(ref _SaveDay, value);
            }
        }
        private LinkVarModel _PicName = new LinkVarModel() { Text = "1" };
        /// <summary>
        /// 图片名称
        /// </summary>
        public LinkVarModel PicName
        {
            get { return _PicName; }
            set { _PicName = value; RaisePropertyChanged(); }
        }
        private bool _ImageNameTime;
        /// <summary>
        /// 以当前时间命名
        /// </summary>
        public bool ImageNameTime
        {
            get { return _ImageNameTime; }
            set
            {
                Set(ref _ImageNameTime, value);
            }
        }
        private bool _DeleteFlag=true;
        /// <summary>
        /// 删除一次图片的标志位
        /// </summary>
        public bool DeleteFlag
        {
            get { return _DeleteFlag; }
            set
            {
                Set(ref _DeleteFlag, value);
            }
        }
        #endregion
        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as SaveImageView;
            if (view != null)
            {
                ClosedView = true;
                if (InputImageLinkText == null || InputImageLinkText == "")
                    SetDefaultLink();
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
                        var view = ModuleView as SaveImageView;
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
                case "InputImageLink":
                    InputImageLinkText = obj.LinkName;
                    break;
                case "PictureName":
                    PicName.Text = obj.LinkName;
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
                            case eLinkCommand.PictureName:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "string");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},PictureName");
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
        private CommandBase _FilePathCommand;
        public CommandBase FilePathCommand
        {
            get
            {
                if (_FilePathCommand == null)
                {
                    _FilePathCommand = new CommandBase((obj) =>
                    {
                        CommonOpenFileDialog dialog = new CommonOpenFileDialog { IsFolderPicker = true };

                        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                        {
                            FilePath = dialog.FileName;
                        }
                    });
                }
                return _FilePathCommand;
            }
        }

        #endregion
        #region Method
        public void RemovImage(string dir, int SaveDay)
        {
            Task.Run(() =>
            {
                try
                {
                    if (dir.Length > 5)
                    {
                        if (!Directory.Exists(dir) || SaveDay < 1)
                            return;
                        var now = DateTime.Now;
                        string sTime = now.ToString("HH:mm");
                        if ("08:30" == sTime)
                        {
                            if(DeleteFlag)
                            {
                                DeleteFlag = false;
                                Logger.AddLog($"流程[{Solution.Ins.GetProjectById(ModuleParam.ProjectID).ProjectInfo.ProcessName}]执行[{ModuleParam.ModuleName}]模块删除图片开始，耗时{ModuleParam.ElapsedTime}ms.", eMsgType.Info);
                                foreach (var f in Directory.GetFileSystemEntries(dir)/*.Where(f => File.Exists(f)*/)
                                {
                                    var t = File.GetCreationTime(f);
                                    var elapsedTicks = now.Ticks - t.Ticks;
                                    var elapsefSpan = new TimeSpan(elapsedTicks);
                                    if (elapsefSpan.TotalDays > SaveDay)
                                    {
                                        new FileInfo(f).Attributes = FileAttributes.Normal;
                                        new FileInfo(f).IsReadOnly = false;
                                        Directory.Delete(f, true);
                                    }
                                }
                                Logger.AddLog($"流程[{Solution.Ins.GetProjectById(ModuleParam.ProjectID).ProjectInfo.ProcessName}]执行[{ModuleParam.ModuleName}]模块删除图片结束，耗时{ModuleParam.ElapsedTime}ms.", eMsgType.Success);
                            }
                        }
                        else
                        {
                            DeleteFlag = true;
                        }       
                    }
                }
                catch (Exception ex)
                {
                    Logger.AddLog($"流程[{Solution.Ins.GetProjectById(ModuleParam.ProjectID).ProjectInfo.ProcessName}]执行[{ModuleParam.ModuleName}]模块删除图片失败，耗时{ModuleParam.ElapsedTime}ms.", eMsgType.Warn);
                }
            });
        }
        #endregion
    }
}
