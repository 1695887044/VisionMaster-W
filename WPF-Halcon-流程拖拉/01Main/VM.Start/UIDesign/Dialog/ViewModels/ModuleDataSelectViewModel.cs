using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VM.Start.Common;
using VM.Start.Common.Helper;
using VM.Start.Models;
using VM.Start.UIDesign.Dialog.Views;

namespace VM.Start.UIDesign.Dialog.ViewModels
{
    public class ModuleDataSelectViewModel : NotifyPropertyBase
    {
        private ModuleDataSelectViewModel() { }

        public static ModuleDataSelectViewModel Ins
        {
            get { return ModuleDataSelectViewModel._instance; }
        }

        public ObservableCollection<ModuleList> Modules { get; set; } =
            new ObservableCollection<ModuleList>();

        public int ModuleIndex { get; set; }

        public VarModel Var { get; set; } = new VarModel();

        public int SelectedIndex_Pro
        {
            get { return this._SelectedIndex_Pro; }
            set
            {
                this._SelectedIndex_Pro = value;
                base.RaisePropertyChanged("SelectedIndex_Pro");
            }
        }

        public string SelectPro
        {
            get { return this._SelectPro; }
            set
            {
                this._SelectPro = value;
                base.RaisePropertyChanged("SelectPro");
                if (!string.IsNullOrEmpty(this._SelectPro))
                {
                    this.Modules = this.Dic[this._SelectPro];
                    ModuleDataSelectView.Ins.tcModuleList.ItemsSource = this.Modules;
                    ModuleDataSelectView.Ins.tcModuleList.SelectedIndex = 0;
                }
            }
        }

        public List<string> ProList { get; set; }

        public CommandBase ActivatedCommand
        {
            get
            {
                if (this._ActivatedCommand == null)
                {
                    this._ActivatedCommand = new CommandBase(
                        delegate(object obj)
                        {
                            if (ModuleDataSelectView.Ins.IsClosed)
                            {
                                ModuleDataSelectView.Ins.IsClosed = false;
                            }
                        }
                    );
                }
                return this._ActivatedCommand;
            }
        }

        public CommandBase ConfirmCommand
        {
            get
            {
                if (this._ConfirmCommand == null)
                {
                    this._ConfirmCommand = new CommandBase(
                        delegate(object obj)
                        {
                            if (this.IsModuleSelect)
                            {
                                this.IsModuleSelect = false;
                                if (this.Modules != null)
                                {
                                    this.ResultLinkData =
                                        this.SelectPro
                                        + "."
                                        + this.Modules[this.ModuleIndex].DisplayName;
                                }
                            }
                            else if (this.IsRunOnceProject)
                            {
                                this.IsRunOnceProject = false;
                                if (!string.IsNullOrEmpty(this.SelectPro))
                                {
                                    this.ResultLinkData = (this.SelectPro ?? "");
                                }
                            }
                            else if (this.Modules != null && this.Var != null)
                            {
                                this.ResultLinkData = string.Concat(
                                    new string[]
                                    {
                                        this.SelectPro,
                                        ".",
                                        this.Modules[this.ModuleIndex].DisplayName,
                                        ".",
                                        this.Var.Name
                                    }
                                );
                            }
                            ModuleDataSelectView.Ins.Close();
                        }
                    );
                }
                return this._ConfirmCommand;
            }
        }

        public void GetData(string dataType = "")
        {
            this.ResultLinkData = "";
            CommonMethods.GetAllModuleList(out this.Dic, dataType);
            this.ProList = this.Dic.Keys.ToList<string>();
            ModuleDataSelectView.Ins.combProList.ItemsSource = this.ProList;
            if (this.ProList.Count > 0)
            {
                this.SelectPro = this.ProList[0];
                this.SelectedIndex_Pro = 0;
            }
        }

        private static readonly ModuleDataSelectViewModel _instance =
            new ModuleDataSelectViewModel();

        public bool IsModuleSelect = false;

        public bool IsRunOnceProject = false;

        public Dictionary<string, ObservableCollection<ModuleList>> Dic =
            new Dictionary<string, ObservableCollection<ModuleList>>();

        [NonSerialized]
        private int _SelectedIndex_Pro;

        [NonSerialized]
        private string _SelectPro;

        public string ResultLinkData = "";

        [NonSerialized]
        private CommandBase _ActivatedCommand;

        [NonSerialized]
        private CommandBase _ConfirmCommand;
    }
}
