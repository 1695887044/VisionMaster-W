using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using VisionCore.appAutoStartUp;
using VM.Start.Common.Helper;
using VM.Start.PersistentData;
using VM.Start.Views;
using WPFLocalizeExtension.Engine;

namespace VM.Start.ViewModels
{
    public class SystemParamViewModel : NotifyPropertyBase
    {
        #region Singleton

        private static readonly SystemParamViewModel _instance = new SystemParamViewModel();

        private SystemParamViewModel() { }

        public static SystemParamViewModel Ins
        {
            get { return _instance; }
        }

        #endregion

        #region Prop
        private CultureInfo _CurrentCulture;
        public CultureInfo CurrentCulture
        {
            get { return _CurrentCulture; }
            set
            {
                Set(ref _CurrentCulture, value);
                LocalizeDictionary.Instance.Culture = _CurrentCulture;
            }
        }
        private bool _AutoLoadLayout;

        public bool AutoLoadLayout
        {
            get { return _AutoLoadLayout; }
            set { Set(ref _AutoLoadLayout, value); }
        }
        private string _CompanyName;

        public string CompanyName
        {
            get { return _CompanyName; }
            set { Set(ref _CompanyName, value); }
        }
        private string _ProjectName;

        public string ProjectName
        {
            get { return _ProjectName; }
            set { Set(ref _ProjectName, value); }
        }
        private string _SoftwareIcoPath;

        public string SoftwareIcoPath
        {
            get { return _SoftwareIcoPath; }
            set { Set(ref _SoftwareIcoPath, value); }
        }
        private int _SoftwareVersion;

        public int SoftwareVersion
        {
            get { return _SoftwareVersion; }
            set { Set(ref _SoftwareVersion, value); }
        }

        /// <summary>
        /// 其它设置
        /// </summary>

        private bool _SolutionAutoLoad;
        public bool SolutionAutoLoad
        {
            get { return _SolutionAutoLoad; }
            set { Set(ref _SolutionAutoLoad, value); }
        }

        private bool _SolutionAutoRun;
        public bool SolutionAutoRun
        {
            get { return _SolutionAutoRun; }
            set { Set(ref _SolutionAutoRun, value); }
        }

        private string _SolutionPathText;
        public string SolutionPathText
        {
            get { return _SolutionPathText; }
            set { Set(ref _SolutionPathText, value); }
        }

        private bool _SoftwareAutoStartup;
        public bool SoftwareAutoStartup
        {
            get { return _SoftwareAutoStartup; }
            set { Set(ref _SoftwareAutoStartup, value); }
        }
        #endregion

        #region Command

        private CommandBase _activatedCommand;
        public CommandBase ActivatedCommand
        {
            get
            {
                if (_activatedCommand == null)
                {
                    _activatedCommand = new CommandBase(
                        (obj) =>
                        {
                            if (SystemParamView.Ins.IsClosed)
                            {
                                SystemParamView.Ins.IsClosed = false;
                                AutoLoadLayout = SystemConfig.Ins.AutoLoadLayout;
                                CompanyName = SystemConfig.Ins.CompanyName;
                                ProjectName = SystemConfig.Ins.ProjectName;
                                SoftwareIcoPath = SystemConfig.Ins.SoftwareIcoPath;
                                SoftwareVersion = SystemConfig.Ins.SoftwareVersion;
                                CurrentCulture = new CultureInfo(
                                    SystemConfig.Ins.CurrentCultureName
                                );

                                SolutionAutoLoad = SystemConfig.Ins.SolutionAutoLoad;
                                SolutionAutoRun = SystemConfig.Ins.SolutionAutoRun;
                                SolutionPathText = SystemConfig.Ins.SolutionPathText;
                                SoftwareAutoStartup = SystemConfig.Ins.SoftwareAutoStartup;
                            }
                        }
                    );
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
                    confirmCommand = new CommandBase(
                        (obj) =>
                        {
                            SystemConfig.Ins.AutoLoadLayout = AutoLoadLayout;
                            SystemConfig.Ins.CompanyName = CompanyName;
                            SystemConfig.Ins.ProjectName = ProjectName;
                            SystemConfig.Ins.SoftwareIcoPath = SoftwareIcoPath;
                            SystemConfig.Ins.SoftwareVersion = SoftwareVersion;
                            SystemConfig.Ins.CurrentCultureName = CurrentCulture.Name;

                            SystemConfig.Ins.SolutionAutoLoad = SolutionAutoLoad;
                            SystemConfig.Ins.SolutionAutoRun = SolutionAutoRun;
                            SystemConfig.Ins.SolutionPathText = SolutionPathText;
                            SystemConfig.Ins.SoftwareAutoStartup = SoftwareAutoStartup;

                            SJappAutoStartUp m_appAutoStartUp = new SJappAutoStartUp();
                            m_appAutoStartUp.SetMeAutoStart(SystemConfig.Ins.SoftwareAutoStartup);

                            SystemConfig.Ins.SaveSystemConfig();
                            SystemParamView.Ins.Close();
                        }
                    );
                }
                return confirmCommand;
            }
        }
        private CommandBase _SoftwareIcoPathCommand;
        public CommandBase SoftwareIcoPathCommand
        {
            get
            {
                if (_SoftwareIcoPathCommand == null)
                {
                    _SoftwareIcoPathCommand = new CommandBase(
                        (obj) =>
                        {
                            OpenFileDialog dlg = new OpenFileDialog();
                            dlg.Filter =
                                "所有图像文件 | *.bmp; *.pcx; *.png; *.jpg; *.gif;*.tif; *.ico; *.dxf; *.cgm; *.cdr; *.wmf; *.eps; *.emf";
                            if (dlg.ShowDialog() == true)
                            {
                                SoftwareIcoPath = dlg.FileName;
                            }
                        }
                    );
                }
                return _SoftwareIcoPathCommand;
            }
        }

        private CommandBase _SolutionPathCommand;
        public CommandBase SolutionPathCommand
        {
            get
            {
                if (_SolutionPathCommand == null)
                {
                    _SolutionPathCommand = new CommandBase(
                        (obj) =>
                        {
                            OpenFileDialog dlg = new OpenFileDialog();
                            dlg.Filter = "所有图像文件 | *.VM";
                            if (dlg.ShowDialog() == true)
                            {
                                SolutionPathText = dlg.FileName;
                            }
                        }
                    );
                }
                return _SolutionPathCommand;
            }
        }
        #endregion
    }
}
