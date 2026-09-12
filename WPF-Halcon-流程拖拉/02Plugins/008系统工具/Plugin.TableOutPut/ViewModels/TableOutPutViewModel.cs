using EventMgrLib;
using HalconDotNet;
using HandyControl.Controls;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Plugin.GrabImage.Model;
using Plugin.TableOutPut.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Xml.Linq;
using VM.Halcon;
using VM.Halcon.Config;
using VM.Halcon.Model;
using VM.Start.Attributes;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.Services;
using VM.Start.ViewModels;
using VM.Start.Views;
using VM.Start.Views.Dock;

namespace Plugin.TableOutPut.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        DispContentLink,
        StatusLink,
        InputImageLink,
      
    }
    #endregion
    [Category("系统工具")]
    [DisplayName("表格")]
    [ModuleImageName("Table")]
    [Serializable]
    public class TableOutPutViewModel : ModuleBase
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
            TablePath = "OutPutTable";
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

                if (mTable == null)
                    mTable = new DataTable();
                if (mTable.Columns.Count > 0) 
                mTable.Columns.Clear();

                    for (int i = 0; i < TextModels.Count; i++)
                    {
                        mTable.Columns.Add(TextModels[i].Prefix,typeof(string));
                    }
                
              
               if (DispImage != null && DispImage.IsInitialized() && TextModels.Count > 0)
                {
                    string blod = IsBlod == true ? "true" : "false";
                    string dispText = "";
                    string color = "green";
                    int xPos = 0, yPos = 0;

                    DataRow row = mTable.NewRow();
                    for (int i = 0; i < TextModels.Count; i++)
                    {
                     
                        xPos = TextModels[i].X_Pos;
                        yPos = TextModels[i].Y_Pos;

                        var varMod_StatusLink = Prj.GetParamByName(TextModels[i].StatusLink);
                        if (varMod_StatusLink == null)
                        {
                            ChangeModuleRunStatus(eRunStatus.NG);
                            return false;
                        }
                        if (bool.Parse(varMod_StatusLink.Value.ToString()))
                        {
                            color = "green";
                        }
                        else
                        {
                            color = "red";
                        }
                        if (TextModels[i].DispContent.StartsWith("&"))
                        {
                            var varMod_DispContent = Prj.GetParamByName(TextModels[i].DispContent);
                            if (varMod_DispContent == null)
                            {
                                ChangeModuleRunStatus(eRunStatus.NG);
                                return false;
                            }
                            if (varMod_DispContent.DataType == "bool")
                            {
                                if (bool.Parse(varMod_DispContent.Value.ToString()))
                                {
                                    dispText = OK_Label;
                                }
                                else
                                {
                                    dispText = NG_Label;
                                }
                            }
                            else
                            {
                                if (varMod_DispContent.Value is double)
                                {
                                    dispText = Math.Round((double)varMod_DispContent.Value, DecimalPlaces).ToString();
                                }
                                else
                                {
                                    dispText = varMod_DispContent.Value.ToString();
                                }
                            }
                        }
                        else
                        {
                            dispText = TextModels[i].DispContent;
                        }
                        //var view = ModuleView as DisplayDataView;
                        //HTuple hv_WindowHandle;
                        //if (view == null || view.IsClosed)
                        //{
                        //    hv_WindowHandle = ViewDic.GetView(DispViewID).hControl.HalconWindow;
                        //}
                        //else
                        //{
                        //    hv_WindowHandle = view.mWindowH.hControl.HalconWindow;
                        //}
                        //string showText = TextModels[i].Prefix + dispText + TextModels[i].Suffix;
                        //ShowTool.SetFont(hv_WindowHandle, FontSize, blod, "false");
                        //ShowTool.SetMsg(hv_WindowHandle, showText, "image", yPos, xPos, color, "false");

                        row[TextModels[i].Prefix]=dispText;
                    }
                    mTable.Rows.Add(row);
                    //if (CSVHelper.fileName == null)
                       
                    CSVHelper.fileName = TablePath;

                   
                    CSVHelper.WriteCSV(mTable,true);

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
        #region Prop
        public ObservableCollection<TextModel> TextModels { get; set; } = new ObservableCollection<TextModel>();
        private string _InputImageLinkText;
        /// <summary>
        /// 输入图像链接文本
        /// </summary>
        public string InputImageLinkText
        {
            get { return _InputImageLinkText; }
            set { Set(ref _InputImageLinkText, value); }
        }

        private string _TablePath;
        /// <summary>
        /// 输入图像链接文本
        /// </summary>
        public string TablePath
        {
            get { return _TablePath; }
            set { Set(ref _TablePath, value); }
        }

        private DataTable mTable;

        private TextModel _SelectedText = new TextModel();
        /// <summary>
        /// 选中的文本
        /// </summary>
        public TextModel SelectedText
        {
            get { return _SelectedText; }
            set { Set(ref _SelectedText, value); }
        }
        private int _DecimalPlaces = 3;
        /// <summary>
        /// 小数位数
        /// </summary>
        public int DecimalPlaces
        {
            get { return _DecimalPlaces; }
            set { Set(ref _DecimalPlaces, value); }
        }
        private int _FontSize = 25;
        /// <summary>
        /// 文字大小
        /// </summary>
        public int FontSize
        {
            get { return _FontSize; }
            set { Set(ref _FontSize, value); }
        }
        private string _OK_Label = "OK";
        /// <summary>
        /// OK标记
        /// </summary>
        public string OK_Label
        {
            get { return _OK_Label; }
            set { Set(ref _OK_Label, value); }
        }
        private string _NG_Label = "NG";
        /// <summary>
        /// NG标记
        /// </summary>
        public string NG_Label
        {
            get { return _NG_Label; }
            set { Set(ref _NG_Label, value); }
        }
        private bool _IsBlod = true;
        /// <summary>
        /// 粗体显示
        /// </summary>
        public bool IsBlod
        {
            get { return _IsBlod; }
            set { Set(ref _IsBlod, value); }
        }

        #endregion

        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as DisplayDataView;
            if (view != null)
            {
                if (view.mWindowH == null)
                {
                    view.mWindowH = new VMHWindowControl();
                    view.winFormHost.Child = view.mWindowH;
                }
                SetDefaultLink();
                GetDispImage(InputImageLinkText);
                if (DispImage != null && DispImage.IsInitialized())
                {
                    view.mWindowH.Image = DispImage;
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
                        var view = this.ModuleView as DisplayDataView;
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
                case "StatusLink":
                    SelectedText.StatusLink = obj.LinkName;
                    break;
                case "DispContent":
                    SelectedText.DispContent = obj.LinkName;
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
                            case eLinkCommand.DispContentLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "bool,string,double,int");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},DispContent");
                                break;
                            case eLinkCommand.StatusLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "bool");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},StatusLink");
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
                                TextModels.Add(new TextModel()
                                {
                                    ID = TextModels.Count + 1,
                                });
                                break;
                            case "Delete":
                                if (SelectedText == null) return;
                                TextModels.Remove(SelectedText);
                                break;
                            case "Modify":
                                break;

                            default:
                                break;
                        }
                    });
                }
                return _DataOperateCommand;
            }
        }


        private CommandBase _FilePathCommand;
        public CommandBase FilePathCommand
        {
            get
            {
                
                    _FilePathCommand = new CommandBase((obj) =>
                    {
        
                        System.Windows.Forms.SaveFileDialog saveFileDialog = new System.Windows.Forms.SaveFileDialog();
                        //打开的文件选择对话框上的标题
                        saveFileDialog.Title = "请选择文件";

                        saveFileDialog.Filter = "逗号分隔文件(*.csv)|*.csv";

                        if (saveFileDialog.ShowDialog()==DialogResult.OK)
                        {
                            TablePath = saveFileDialog.FileName.ToString();
                        }
                    });
                
                return _FilePathCommand;
            }
        }


        #endregion

        #region Method

        #endregion

    }

}
