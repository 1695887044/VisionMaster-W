using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.Common.Helper
{
    public static class FilePaths
    {
        private static string _ConfigFilePath = Directory.GetCurrentDirectory() + "\\ConfigFile\\";
        public static string ConfigFilePath
        {
            get
            {
                if (string.IsNullOrEmpty(_ConfigFilePath))
                {
                    _ConfigFilePath = Directory.GetCurrentDirectory() + "\\ConfigFile\\";
                }
                //if (!Directory.Exists(_ConfigFilePath))
                //{
                //    Directory.CreateDirectory(_ConfigFilePath);
                //}
                return _ConfigFilePath;
            }
        }
        private static string _RecipePath = Directory.GetCurrentDirectory() + "\\Recipe\\";
        public static string RecipePath
        {
            get
            {
                if (string.IsNullOrEmpty(_RecipePath))
                {
                    _RecipePath = Directory.GetCurrentDirectory() + "\\Recipe\\";
                }
                if (!Directory.Exists(_RecipePath))
                {
                    Directory.CreateDirectory(_RecipePath);
                }
                return _RecipePath;
            }
        }
        private static string _HelpDecomentPath = Directory.GetCurrentDirectory() + "\\Help\\";
        public static string HelpDecomentPath
        {
            get
            {
                if (string.IsNullOrEmpty(_HelpDecomentPath))
                {
                    _HelpDecomentPath = Directory.GetCurrentDirectory() + "\\Help\\";
                }
                //if (!Directory.Exists(_HelpDecomentPath))
                //{
                //    Directory.CreateDirectory(_HelpDecomentPath);
                //}
                return _HelpDecomentPath;
            }
        }

        private static string _SystemConfig = ConfigFilePath + "SystemConfig.json";
        public static string SystemConfig
        {
            get
            {
                if (!File.Exists(_SystemConfig))
                {
                    File.Create(_SystemConfig).Close();
                }
                return _SystemConfig;
            }
        }
        private static string _UserConfig = ConfigFilePath + "UserConfig.xml";
        public static string UserConfig
        {
            get
            {
                if (!File.Exists(_UserConfig))
                {
                    File.Create(_UserConfig).Close();
                }
                return _UserConfig;
            }
        }
        private static string _MotionConfig = ConfigFilePath + "MotionConfig.conf";
        public static string MotionConfig
        {
            get
            {
                if (!File.Exists(_MotionConfig))
                {
                    File.Create(_MotionConfig).Close();
                }
                return _MotionConfig;
            }
        }
        private static string _DefultSoftwareIcon = ConfigFilePath + "Logo.png";
        public static string DefultSoftwareIcon
        {
            get { return _DefultSoftwareIcon; }
        }
        private static string _Regions = ConfigFilePath + "Regions.xml";
        public static string Regions
        {
            get
            {
                if (!File.Exists(_Regions))
                {
                    File.Create(_Regions).Close();
                }
                return _Regions;
            }
        }
        private static string _DockLayout = ConfigFilePath + "DockLayout.config";
        public static string DockLayout
        {
            get
            {
                if (!File.Exists(_DockLayout))
                {
                    File.Create(_DockLayout).Close();
                }
                return _DockLayout;
            }
        }
        private static string _DefaultDockLayout = ConfigFilePath + "DefaultDockLayout.config";
        public static string DefaultDockLayout
        {
            get
            {
                if (!File.Exists(_DefaultDockLayout))
                {
                    File.Create(_DefaultDockLayout).Close();
                }
                return _DefaultDockLayout;
            }
        }

        private static string _UIDesignTemplateFilePath =
            FilePaths.ConfigFilePath + "UIDesignTemplate.xaml";
        public static string UIDesignTemplateFilePath
        {
            get
            {
                if (!File.Exists(FilePaths._UIDesignTemplateFilePath))
                {
                    File.Create(FilePaths._UIDesignTemplateFilePath).Close();
                }
                return FilePaths._UIDesignTemplateFilePath;
            }
        }

        private static string _UIDesignHomeFilePath =
            FilePaths.ConfigFilePath + "UIDesignMainView.xaml";
        public static string UIDesignHomeFilePath
        {
            get
            {
                if (!File.Exists(FilePaths._UIDesignHomeFilePath))
                {
                    File.Create(FilePaths._UIDesignHomeFilePath).Close();
                }
                return FilePaths._UIDesignHomeFilePath;
            }
        }
    }
}
