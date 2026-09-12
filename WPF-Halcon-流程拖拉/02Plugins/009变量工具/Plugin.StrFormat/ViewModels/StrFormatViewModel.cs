using EventMgrLib;
using HalconDotNet;
using Microsoft.Win32;
using Plugin.StrFormat.Views;
using Plugin.GrabImage.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

namespace Plugin.StrFormat.ViewModels
{
    #region enum
    public enum eLinkCommand
    {
        DispContentLink,
        StatusLink,
        InputImageLink
    }
    #endregion
    [Category("变量工具")]
    [DisplayName("字符格式化")]
    [ModuleImageName("StrFormat")]
    [Serializable]
    public class StrFormatViewModel : ModuleBase
    {
        public override void SetDefaultLink() { }

        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                string dispText;
                OutPutStr = "";
                for (int i = 0; i < TextModels.Count; i++)
                {
                    dispText = "";
                    if (TextModels[i].DispContent.StartsWith("&"))
                    {
                        var varMod_DispContent = Prj.GetParamByName(TextModels[i].DispContent);
                        if (varMod_DispContent == null)
                        {
                            ChangeModuleRunStatus(eRunStatus.NG);
                            return false;
                        }
                        else
                        {
                            if (varMod_DispContent.Value is double)
                            {
                                dispText = Math.Round(
                                        (double)varMod_DispContent.Value,
                                        DecimalPlaces
                                    )
                                    .ToString();
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
                    string showText = TextModels[i].Prefix + dispText + TextModels[i].Suffix;
                    OutPutStr += showText;
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

        #region Prop
        public ObservableCollection<TextModel> TextModels { get; set; } =
            new ObservableCollection<TextModel>();

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
        private string _OutPutStr;
        public string OutPutStr
        {
            get { return _OutPutStr; }
            set { _OutPutStr = value; }
        }
        #endregion

        #region Command
        public override void Loaded()
        {
            base.Loaded();
            var view = ModuleView as StrFormatView;
            if (view != null)
            {
                if (view.mWindowH == null)
                {
                    view.mWindowH = new VMHWindowControl();
                    view.winFormHost.Child = view.mWindowH;
                }
            }
        }

        public override void AddOutputParams()
        {
            base.AddOutputParams();
            AddOutputParam("格式化字符串", "string", OutPutStr);
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
                            var view = this.ModuleView as StrFormatView;
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
                    EventMgr.Ins
                        .GetEvent<VarChangedEvent>()
                        .Subscribe(OnVarChanged, o => o.SendName.StartsWith($"{ModuleGuid}"));
                    _LinkCommand = new CommandBase(
                        (obj) =>
                        {
                            eLinkCommand linkCommand = (eLinkCommand)obj;
                            switch (linkCommand)
                            {
                                case eLinkCommand.DispContentLink:
                                    CommonMethods.GetModuleList(
                                        ModuleParam,
                                        VarLinkViewModel.Ins.Modules,
                                        "string,double,int"
                                    );
                                    EventMgr.Ins
                                        .GetEvent<OpenVarLinkViewEvent>()
                                        .Publish($"{ModuleGuid},DispContent");
                                    break;
                                default:
                                    break;
                            }
                        }
                    );
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
                    _DataOperateCommand = new CommandBase(
                        (obj) =>
                        {
                            switch (obj)
                            {
                                case "Add":
                                    TextModels.Add(new TextModel() { ID = TextModels.Count + 1, });
                                    break;
                                case "Delete":
                                    if (SelectedText == null)
                                        return;
                                    TextModels.Remove(SelectedText);
                                    break;
                                case "Modify":
                                    break;

                                default:
                                    break;
                            }
                        }
                    );
                }
                return _DataOperateCommand;
            }
        }
        #endregion

        #region Method

        #endregion
    }
}
