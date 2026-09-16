using EventMgrLib;
using HalconDotNet;
using Microsoft.WindowsAPICodePack.Dialogs;
using Plugin.WriteText.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using VM.Halcon;
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

namespace Plugin.WriteText.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        InputLinkName,
        Value
    }
    #endregion
    [Category("系统工具")]
    [DisplayName("写入文本")]
    [ModuleImageName("WriteText")]
    [Serializable]
    public class RotateCenterViewModel : ModuleBase
    {
        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                if (FilePath == "")
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                if (!Directory.Exists(FilePath))
                {
                    Directory.CreateDirectory(FilePath);
                }
                string sSavePath = FilePath + "\\";
                DateTime dt = DateTime.Now;
                if (bTimeName)
                {
                    sSavePath += GetLinkValue(InputLinkName).ToString() + "_" + dt.ToString("yyyyMMdd") + "." + Extensions;
                }
                else
                {
                    sSavePath += GetLinkValue(InputLinkName).ToString() + "." + Extensions;
                }
                if (DataParams.Count < 0)
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                string sTitle = "", sValue = "", sEndSymbol = "", sSplitSymbol = "";
                switch (SymbolList[EndSelectedIndex])
                {
                    case "无":
                        break;
                    case "逗号":
                        sEndSymbol = ",";
                        break;
                    case "分号":
                        sEndSymbol = ";";
                        break;
                    case "换行":
                        sEndSymbol = "\n";
                        break;
                    default:
                        break;
                }
                switch (SymbolList[SplitSelectedIndex])
                {
                    case "无":
                        break;
                    case "逗号":
                        sSplitSymbol = ",";
                        break;
                    case "分号":
                        sSplitSymbol = ";";
                        break;
                    case "换行":
                        sSplitSymbol = "\n";
                        break;
                    default:
                        break;
                }
                for (int i = 0; i < DataParams.Count; i++)
                {
                    if (i < DataParams.Count - 1)
                    {
                        sTitle += DataParams[i].Title + sSplitSymbol;
                        sValue += GetLinkValue(DataParams[i].Value).ToString() + sSplitSymbol;
                    }
                    else
                    {
                        sTitle += DataParams[i].Title + sEndSymbol;
                        sValue += GetLinkValue(DataParams[i].Value).ToString() + sEndSymbol;
                    }
                }
                //if (bCover)
                //    sValue = sValue.Replace(sEndSymbol,"");
                if (!File.Exists(sSavePath))
                {
                    StreamWriter FileWriter = new StreamWriter(sSavePath, true, Encoding.Default);
                    FileWriter.Write(sTitle);
                    FileWriter.Write(sValue);
                    FileWriter.Close();
                }
                else
                {
                    StreamWriter FileWriter = new StreamWriter(sSavePath, true, Encoding.Default);
                    FileWriter.Write(sValue);
                    FileWriter.Close();
                }
                if (bClearFile)
                {
                    RemovImage(FilePath, nDay);
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
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        #region Prop     
        private ObservableCollection<TxtParams> _DataParams = new ObservableCollection<TxtParams>();
        public ObservableCollection<TxtParams> DataParams
        {
            get { return _DataParams; }
            set { _DataParams = value; RaisePropertyChanged(); }
        }
        private int _nSelectIndex;
        public int nSelectIndex
        {
            get { return _nSelectIndex; }
            set { Set(ref _nSelectIndex, value); }
        }

        private string _FilePath;
        public string FilePath
        {
            get { return _FilePath; }
            set { _FilePath = value; RaisePropertyChanged(); }
        }
        private bool _bClearFile = true;
        /// <summary>
        /// 清除文本
        /// </summary>
        public bool bClearFile
        {
            get { return _bClearFile; }
            set
            {
                Set(ref _bClearFile, value);
            }
        }
        private int _nDay = 3;
        public int nDay
        {
            get { return _nDay; }
            set
            {
                Set(ref _nDay, value);
            }
        }
        private LinkVarModel _InputLinkName = new LinkVarModel() { Value = "1" };
        public LinkVarModel InputLinkName
        {
            get { return _InputLinkName; }
            set { _InputLinkName = value; RaisePropertyChanged(); }
        }
        private bool _bTimeName = true;
        public bool bTimeName
        {
            get { return _bTimeName; }
            set
            {
                Set(ref _bTimeName, value);
            }
        }
        //扩展名
        private string _Extensions = "csv";
        public string Extensions
        {
            get { return _Extensions; }
            set
            {
                Set(ref _Extensions, value);
            }
        }
        //覆盖数据
        private bool _bCover = false;
        public bool bCover
        {
            get { return _bCover; }
            set
            {
                Set(ref _bCover, value);
            }
        }
        public List<string> SymbolList { get; set; } = new List<string>() { "无", "逗号", "分号", "换行" };
        private int _EndSelectedIndex = 3;
        public int EndSelectedIndex
        {
            get { return _EndSelectedIndex; }
            set
            {
                Set(ref _EndSelectedIndex, value);
            }
        }
        private int _SplitSelectedIndex = 1;
        public int SplitSelectedIndex
        {
            get { return _SplitSelectedIndex; }
            set
            {
                Set(ref _SplitSelectedIndex, value);
            }
        }
        private bool _DeleteFlag = true;
        /// <summary>
        /// 删除表格一次的标志位
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
            var view = ModuleView as WriteTextView;
            if (view != null)
            {
                ClosedView = true;
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
                        var view = ModuleView as WriteTextView;
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
                case "InputLinkName":
                    InputLinkName.Text = obj.LinkName;
                    break;
                case "Value":
                    DataParams[nSelectIndex].Value = obj.LinkName;
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
                            case eLinkCommand.InputLinkName:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "string");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},InputLinkName");
                                break;
                            case eLinkCommand.Value:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules, "string,double,int");
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},Value");
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
        [NonSerialized]
        private CommandBase _OperateCommand;
        public CommandBase OperateCommand
        {
            get
            {
                if (_OperateCommand == null)
                {
                    _OperateCommand = new CommandBase((obj) =>
                    {
                        try
                        {
                            switch (obj)
                            {
                                case "添加":
                                    DataParams.Add(new TxtParams()
                                    {
                                        ID = DataParams.Count + 1,
                                        Title = "",
                                        Value = "",
                                        LinkCommand = LinkCommand
                                    });
                                    break;
                                case "删除":
                                    if (nSelectIndex >= 0)
                                        DataParams.RemoveAt(nSelectIndex);
                                    UpdateIndex();
                                    break;
                                case "上移":
                                    if (nSelectIndex <= 0)
                                        return;
                                    DataParams.Move(nSelectIndex, nSelectIndex - 1);
                                    UpdateIndex();
                                    break;
                                case "下移":
                                    if (nSelectIndex < 0 || nSelectIndex >= DataParams.Count - 1)
                                        return;
                                    DataParams.Move(nSelectIndex, nSelectIndex + 1);
                                    UpdateIndex();
                                    break;
                                case "全部删除":
                                    var messageView = MessageView.Ins;
                                    messageView.MessageBoxShow("是否进行全部删除?", eMsgType.Warn, MessageBoxButton.OKCancel);
                                    if (messageView.DialogResult == true && DataParams.Count > 0)
                                        DataParams.Clear();
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.GetExceptionMsg(ex);
                        }
                    });
                }
                return _OperateCommand;
            }
        }
        #endregion
        #region Method     
        private void UpdateIndex()
        {
            if (DataParams.Count == 0) return;
            for (int i = 0; i < DataParams.Count; i++)
            {
                DataParams[i].ID = i + 1;
            }
            System.Windows.Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                System.ComponentModel.ICollectionView view = System.Windows.Data.CollectionViewSource.GetDefaultView(DataParams);
                view.Refresh();
            }));
        }

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
                        if ("09:00" == sTime)
                        {
                            if (DeleteFlag)
                            {
                                DeleteFlag = false;
                                Logger.AddLog($"流程[{ModuleParam.ProjectID}]执行[{ModuleParam.ModuleName}]模块文本开始，耗时{ModuleParam.ElapsedTime}ms.", eMsgType.Info);
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
                                Logger.AddLog($"流程[{ModuleParam.ProjectID}]执行[{ModuleParam.ModuleName}]模块文本结束，耗时{ModuleParam.ElapsedTime}ms.", eMsgType.Success);
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
                    Logger.AddLog($"流程[{ModuleParam.ProjectID}]执行[{ModuleParam.ModuleName}]模块删除图片失败，耗时{ModuleParam.ElapsedTime}ms.", eMsgType.Warn);
                }
            });
        }
        #region 数据存储
        /// <summary>数据存储</summary>
        public class Csv
        {
            /// <summary>
            /// 保存CSV
            /// </summary>
            /// <param name="FullPath">路径</param>
            /// <param name="FileName">名称</param>
            /// <param name="date">时间</param>
            /// <param name="dataRow">标题行</param>
            /// <param name="dataCol">内容列</param>
            /// <returns></returns>
            public static bool Save(string FullPath, string FileName, string dataRow, string dataCol)
            {
                try
                {
                    FileStream mFileStream;
                    StreamWriter mStreamWriter;
                    string date = DateTime.Now.ToString("yyyy-MM-d");
                    if (!Directory.Exists(FullPath))
                    {
                        Directory.CreateDirectory(FullPath); //在指定路径中创建所有目录。 ////DateTime.Now.ToString("yyyyMMddHHmmss");
                    }
                    string name = Path.GetFileNameWithoutExtension(FullPath + "\\" + FileName);//返回不具有扩展名的指定路径字符串的文件名。
                    string path = FullPath + "\\" + date + " " + name + ".csv";
                    if (!File.Exists(path))
                    {
                        using (File.Create(path)) { }//在指定路径中创建文件。
                        mFileStream = new FileStream(path, FileMode.Append);
                        mStreamWriter = new StreamWriter(mFileStream, Encoding.UTF8);
                        mStreamWriter.WriteLine(dataRow);
                        mStreamWriter.WriteLine(dataCol);
                        mStreamWriter.Flush();
                        mStreamWriter.Close();
                        mFileStream.Close();
                    }
                    else
                    {
                        mFileStream = new FileStream(path, FileMode.Append);
                        mStreamWriter = new StreamWriter(mFileStream, Encoding.UTF8);
                        mStreamWriter.WriteLine(dataCol);
                        mStreamWriter.Flush();
                        mStreamWriter.Close();
                        mFileStream.Close();
                    }
                }
                catch
                {
                    return false;
                }
                return true;
            }
        }
        #endregion
        #endregion
    }
    [Serializable]
    public class TxtParams : NotifyPropertyBase
    {
        public int ID { get; set; }
        public string Title { get; set; }
        private string _Value = "";

        public string Value
        {
            get { return _Value; }
            set { _Value = value; RaisePropertyChanged(); }
        }
        public CommandBase LinkCommand { get; set; }
    }
}
