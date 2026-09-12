using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using VM.Start.Common;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Models;
using VM.Start.PersistentData;
using VM.Start.Services;
using VM.Start.Views;
using VM.Start.Views.Dock;
using WPFLocalizeExtension.Engine;

namespace VM.Start.ViewModels
{
    public class CanvasSetViewModel : NotifyPropertyBase
    {
        #region Singleton

        private static readonly CanvasSetViewModel _instance = new CanvasSetViewModel();

        private CanvasSetViewModel()
        {

        }
        public static CanvasSetViewModel Ins
        {
            get { return _instance; }
        }

        #endregion

        #region Prop

        #endregion

        #region Command

        private CommandBase _ActivatedCommand;
        public CommandBase ActivatedCommand
        {
            get
            {
                if (_ActivatedCommand == null)
                {
                    _ActivatedCommand = new CommandBase((obj) =>
                    {
                        if (CanvasSetView.Ins.IsClosed)
                        {
                            CanvasSetView.Ins.IsClosed = false;

                        }

                    });
                }
                return _ActivatedCommand;
            }
        }
        private CommandBase _ConfirmCommand;
        public CommandBase ConfirmCommand
        {
            get
            {
                if (_ConfirmCommand == null)
                {
                    _ConfirmCommand = new CommandBase((obj) =>
                    {

                        VisionView.Ins.ViewMode = Solution.Ins.ViewMode;
                        CanvasSetView.Ins.Close();
                    });
                }
                return _ConfirmCommand;
            }
        }

        #endregion
    }
}
