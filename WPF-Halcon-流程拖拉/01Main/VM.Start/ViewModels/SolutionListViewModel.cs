using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using VM.Start.Common.Helper;
using VM.Start.PersistentData;
using VM.Start.Views;
using WPFLocalizeExtension.Engine;

namespace VM.Start.ViewModels
{
    public class SolutionListViewModel : NotifyPropertyBase
    {
        #region Singleton

        private static readonly SolutionListViewModel _instance = new SolutionListViewModel();

        private SolutionListViewModel()
        {

        }
        public static SolutionListViewModel Ins
        {
            get { return _instance; }
        }

        #endregion

        #region Prop

        #endregion

        #region Command

        private CommandBase _activatedCommand;
        public CommandBase ActivatedCommand
        {
            get
            {
                if (_activatedCommand == null)
                {
                    _activatedCommand = new CommandBase((obj) =>
                    {
                        if (SolutionListView.Ins.IsClosed)
                        {
                            SolutionListView.Ins.IsClosed = false;
                        }

                    });
                }
                return _activatedCommand;
            }
        }
        private CommandBase confirmCommand;
        public CommandBase ConfirmCommand
        {
            get
            {
                if (confirmCommand == null)
                {
                    confirmCommand = new CommandBase((obj) =>
                    {
                        SolutionListView.Ins.Close();
                    });
                }
                return confirmCommand;
            }
        }


        #endregion
    }
}
