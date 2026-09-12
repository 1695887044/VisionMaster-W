using EventMgrLib;
using HalconDotNet;
using Plugin.SplitString.Model;
using Plugin.SplitString.Views;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Configuration;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Serialization;
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
using VM.Start.Services;
using VM.Start.ViewModels;
using VM.Start.Views.Dock;

namespace Plugin.SplitString.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        DataLink,
    }
    public enum eSplitMode
    {
        固定长度,
        分隔符
    }
    #endregion

    [Category("变量工具")]
    [DisplayName("分割文本")]
    [ModuleImageName("SplitString")]
    [Serializable]
    public class SplitStringViewModel : ModuleBase
    {
        public override void SetDefaultLink()
        {

        }

        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                if (DataLinkText == null || Data.Count == 0)
                {
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }
                string str = GetLinkValue(DataLinkText).ToString();
                string[] strS = null;
                switch (SelectSplitMode)
                {
                    case eSplitMode.分隔符:
                        strS = str.Split(new string[] { SplitPoint }, StringSplitOptions.None);
                        break;
                    case eSplitMode.固定长度:
                        strS = GetStr(str, SplitNum);
                        break;
                    default:
                        break;
                }
                //判断是否有重名
                if (IsRepeat())
                {
                    MessageView.Ins.MessageBoxShow("不能有重复变量名称", eMsgType.Warn);
                    ChangeModuleRunStatus(eRunStatus.NG);
                    return false;
                }

                for (int i = 0; i < Data.Count; i++)
                {
                    if (i < strS.Length)
                    {
                        switch (Data[i].DataType)
                        {
                            case "int":
                                if (int.TryParse(strS[i], out int result1))
                                    Data[i].DataValue = result1.ToString();
                                else
                                    Data[i].DataValue = "0";
                                break;
                            case "double":
                                if (double.TryParse(strS[i], out double result2))
                                    Data[i].DataValue = result2.ToString();
                                else
                                    Data[i].DataValue = "0";
                                break;
                            case "bool":
                                if (bool.TryParse(strS[i], out bool result3))
                                    Data[i].DataValue = result3.ToString();
                                else
                                    Data[i].DataValue = "0";
                                break;
                            case "string":
                                Data[i].DataValue = strS[i];
                                break;
                        }
                    }
                    else
                    {
                        Data[i].DataValue = "0";
                    }
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
            //增加输出前，先把原先字典里的输出清除
            if (Prj.OutputMap.ContainsKey(ModuleParam.ModuleName))
            {
                Prj.OutputMap.Remove(ModuleParam.ModuleName);
            }

            if (ModuleParam.Status == eRunStatus.OK)
            {
                foreach (var data in Data)
                {
                    AddOutputParam(data.DataName, data.DataType, data.DataValue);
                }
            }
            AddOutputParam("状态", "bool", ModuleParam.Status == eRunStatus.OK ? true : false);
            AddOutputParam("时间", "int", ModuleParam.ElapsedTime);
        }
        #region Prop
        private eSplitMode _SelectSplitMode = eSplitMode.分隔符;

        public eSplitMode SelectSplitMode
        {
            get { return _SelectSplitMode; }
            set { Set(ref _SelectSplitMode, value); }
        }
        /// <summary>
        /// 数据信息
        /// </summary>
        public ObservableCollection<SplitStringModel> Data { get; set; } = new ObservableCollection<SplitStringModel>();

        private SplitStringModel _SelectedData = new SplitStringModel();
        /// <summary>
        /// 选中的文本
        /// </summary>
        public SplitStringModel SelectedData
        {
            get { return _SelectedData; }
            set { Set(ref _SelectedData, value); }
        }
        //名称计数
        public int ValueNum = 0;

        private string _DataLinkText;

        public string DataLinkText
        {
            get { return _DataLinkText; }
            set { Set(ref _DataLinkText, value); }
        }

        private string _SplitPoint = ",";

        public string SplitPoint
        {
            get { return _SplitPoint; }
            set { Set(ref _SplitPoint, value); }
        }

        private int _SplitNum = 1;

        public int SplitNum
        {
            get { return _SplitNum; }
            set { Set(ref _SplitNum, value); }
        }

        #endregion
        #region Command
        public override void Loaded()
        {
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
                        var view = this.ModuleView as SplitStringView;
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
                case "DataLink":
                    DataLinkText = obj.LinkName;
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
                            case eLinkCommand.DataLink:
                                CommonMethods.GetModuleList(ModuleParam, VarLinkViewModel.Ins.Modules);
                                EventMgr.Ins.GetEvent<OpenVarLinkViewEvent>().Publish($"{ModuleGuid},DataLink");
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
                            case "int":
                            case "double":
                            case "bool":
                            case "string":
                                string val = "Value" + ValueNum.ToString();
                                Data.Add(new SplitStringModel() { DataType = obj.ToString(), DataName = val });
                                ValueNum += 1;
                                break;
                            case "Delete":
                                if (SelectedData == null) return;
                                Data.Remove(SelectedData);
                                break;
                            case "DownMove":
                                break;
                            case "UpMove":
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
        /// <summary>
        /// 按照长度拆分字符串
        /// </summary>
        /// <param name="strs"></param>
        /// <param name="len"></param>
        /// <returns></returns>
        private String[] GetStr(string strs, int len)
        {
            double i = strs.Length;
            string[] myarray = new string[int.Parse(Math.Ceiling(i / len).ToString())];
            for (int j = 0; j < myarray.Length; j++)
            {
                len = len <= strs.Length ? len : strs.Length;
                myarray[j] = strs.Substring(0, len);
                strs = strs.Substring(len, strs.Length - len);
            }
            return myarray;
        }
        /// <summary>
        /// 判断是否重复名称
        /// </summary>
        /// <returns></returns>
        private bool IsRepeat()
        {
            string[] str = new string[Data.Count];
            for (int i = 0; i < Data.Count; i++)
            {
                str[i] = Data[i].DataName;
            }
            return IsSameWithHashSet(str);
        }

        private bool IsSameWithHashSet(string[] arr)
        {
            ISet<string> set = new HashSet<string>();

            for (var i = 0; i < arr.Length; i++)
            {
                set.Add(arr[i]);
            }

            return set.Count != arr.Length;
        }
        #endregion
    }
}
