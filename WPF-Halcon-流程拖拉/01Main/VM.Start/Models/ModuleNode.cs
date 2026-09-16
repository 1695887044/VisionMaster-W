using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Core;
using VM.Start.Services;

namespace VM.Start.Models
{
    /// <summary>
    /// 模块 树形类
    /// </summary>
    [Serializable]
    public class ModuleNode : DependencyObject
    {

        public ModuleNode(ModuleBase info)
        {
            Children = new List<ModuleNode>();
            Name = info.ModuleParam.ModuleName;
            ProjectID = info.ModuleParam.ProjectID;
            CostTime = info.ModuleParam.ElapsedTime.ToString();
            switch (info.ModuleParam.Status)
            {
                case eRunStatus.OK:
                    StatusImage = "\xe62e";
                    StatusColor = Brushes.Lime;
                    IsRunning = false;
                    break;
                case eRunStatus.NG:
                    StatusImage = "\xe633";
                    StatusColor = Brushes.Red;
                    IsRunning = false;
                    break;
                case eRunStatus.NotRun:
                    StatusImage = null;
                    StatusColor = Brushes.Transparent;
                    IsRunning = false;
                    break;
                case eRunStatus.Running:
                    StatusImage = null;
                    StatusColor = Brushes.Transparent;
                    IsRunning = true;
                    break;
                default:
                    break;
            }
            IsUseSuperTool = info.ModuleParam.IsUseSuperTool;
            ModuleNo = info.ModuleParam.ModuleEncode;
            ModuleInfo = info;
            DisplayName = Name;
            Remarks = info.ModuleParam.Remarks;
            IsEnableBreakPoint = info.ModuleParam.IsEnableBreakPoint;
            //这里设置模块的图片 不同的模块不同的图片
            IconImage = GetImageByName(info.ModuleParam);

        }
        public ModuleNode(Project project)
        {
            Children = new List<ModuleNode>();
            //Name = project.ProjectInfo.ProjectName + project.ProjectInfo.ProjectID;
            ProjectID = project.ProjectInfo.ProjectID;
            //DisplayName = Name;
            Name = project.ProjectInfo.ProcessName;
            DisplayName = Name;
            Remarks = project.ProjectInfo.Remarks;
            IsEncypt = project.ProjectInfo.IsEncypt;
            //这里设置模块的图片 不同的模块不同的图片
            IconImage = GetProcessImageByName(project.ProjectInfo.ProjectType, project.ProjectInfo.ProjectRunMode);
        }
        public static BitmapImage GetProcessImageByName(eProjectType projectType, eProjectAutoRunMode runMode)
        {
            BitmapImage image = new BitmapImage(new Uri($"/VM.Start;component/Assets/Images/Tool/Default.png", UriKind.Relative));
            switch (projectType)
            {
                case eProjectType.Process:
                    if (runMode == eProjectAutoRunMode.主动执行)
                        image = new BitmapImage(new Uri($"/VM.Start;component/Assets/Images/Tool/主流程.png", UriKind.Relative));
                    else
                        image = new BitmapImage(new Uri($"/VM.Start;component/Assets/Images/Tool/子流程.png", UriKind.Relative));
                    break;
                case eProjectType.Method:
                    image = new BitmapImage(new Uri($"/VM.Start;component/Assets/Images/Tool/方法.png", UriKind.Relative));
                    break;
                case eProjectType.Folder:
                    image = new BitmapImage(new Uri($"/VM.Start;component/Assets/Images/Tool/Folder.png", UriKind.Relative));
                    break;
                default:
                    break;
            }
            return image;
        }

        public static BitmapImage GetImageByName(ModuleParam moduleParam)
        {
            BitmapImage image = new BitmapImage(new Uri($"/VM.Start;component/Assets/Images/Tool/Default.png", UriKind.Relative));
            if (!PluginService.PluginDic_Module.Keys.Contains(moduleParam.PluginName)) return image;
            switch (moduleParam.PluginName)
            {
                case "条件分支":
                    if (moduleParam.ModuleName.StartsWith("如果"))
                    {
                        return new BitmapImage(new Uri($"/VM.Start;component/Assets/Images/Tool/IfStart.png", UriKind.Relative));
                    }
                    else if (moduleParam.ModuleName.StartsWith("否则如果"))
                    {
                        return new BitmapImage(new Uri($"/VM.Start;component/Assets/Images/Tool/elseif.png", UriKind.Relative));
                    }
                    else if (moduleParam.ModuleName.StartsWith("否则"))
                    {
                        return new BitmapImage(new Uri($"/VM.Start;component/Assets/Images/Tool/else.png", UriKind.Relative));
                    }
                    else
                    {
                        return new BitmapImage(new Uri($"/VM.Start;component/Assets/Images/Tool/end.png", UriKind.Relative));
                    }
                case "循环工具":
                    if (moduleParam.ModuleName.StartsWith("循环开始"))
                    {
                        return new BitmapImage(new Uri($"/VM.Start;component/Assets/Images/Tool/recycleStart.png", UriKind.Relative));
                    }
                    else
                    {
                        return new BitmapImage(new Uri($"/VM.Start;component/Assets/Images/Tool/recycleEnd.png", UriKind.Relative));
                    }

                default:
                    return new BitmapImage(new Uri($"/VM.Start;component/Assets/Images/Tool/{PluginService.PluginDic_Module[moduleParam.PluginName].ImageName}.png", UriKind.Relative));

            }
        }
        private int _ProjectID;
        /// <summary>
        /// 项目ID
        /// </summary>
        public int ProjectID
        {
            get { return _ProjectID; }
            set { Set(ref _ProjectID, value); }
        }
        private bool _IsEncypt = false;
        /// <summary>
        /// 加密
        /// </summary>
        public bool IsEncypt
        {
            get { return _IsEncypt; }
            set { Set(ref _IsEncypt, value); }
        }
        private bool _IsSelected;
        /// <summary>
        /// 选中
        /// </summary>
        public bool IsSelected
        {
            get { return _IsSelected; }
            set { Set(ref _IsSelected, value); }
        }

        /// <summary>
        /// 编号
        /// </summary>
        public int ModuleNo { get; set; }
        /// <summary>
        /// 使能断点
        /// </summary>
        public bool IsEnableBreakPoint { get; set; } = false;

        public ImageSource IconImage { get; set; }
        private ModuleBase _ModuleInfo;
        public ModuleBase ModuleInfo
        {
            get { return _ModuleInfo; }
            set
            {
                _ModuleInfo = value;
                if (_ModuleInfo != null)
                {
                    IsUse = true;
                }
            }
        }
        /// <summary>
        /// 是否使用
        /// </summary>
        public bool IsUse { get; set; }
        public bool IsCategory { get; set; } = false;
        public int Hierarchy { get; set; } = 0;// 层级  0是第一层 孙子层级是2  
        private string m_Name;
        public string Name
        {
            get { return m_Name; }
            set { m_Name = value; }
        }
        /// <summary>
        /// 显示的名称
        /// </summary>
        public string DisplayName { get; set; }
        /// <summary>
        /// 备注
        /// </summary>
        public string Remarks { get; set; }



        public ModuleNode ParentModuleNode { get; set; }//父类
        public List<ModuleNode> Children { get; set; }


        /// <summary>
        /// 是否展开
        /// </summary>
        public bool IsExpanded { get; set; } = true;


        public string ModuleForeground { get; set; } = "#000000";

        public bool IsHideExpanded { get; set; } = false;

        //DragOver的时候 下划线加粗
        public static readonly DependencyProperty DragOverHeightProperty =
        DependencyProperty.Register("DragOverHeight", typeof(int), typeof(ModuleNode),
        new PropertyMetadata((int)1));

        public int DragOverHeight
        {
            get { return (int)GetValue(DragOverHeightProperty); }
            set
            {
                if (value != DragOverHeight)//和上次不一样才更新
                {
                    SetValue(DragOverHeightProperty, value);
                }
            }
        }

        //当最后一个元素 是 子集的时候,下划线要往左移动
        public static readonly DependencyProperty LastNodeMarginProperty =
        DependencyProperty.Register("LastNodeMargin", typeof(string), typeof(ModuleNode),
        new PropertyMetadata((string)"0,0,0,0"));

        public string LastNodeMargin
        {
            get { return (string)GetValue(LastNodeMarginProperty); }
            set
            { SetValue(LastNodeMarginProperty, value); }
        }

        //模块运行状态
        public static readonly DependencyProperty StatusImageProperty =
        DependencyProperty.Register("StatusImage", typeof(string), typeof(ModuleNode),
        new PropertyMetadata());
        public string StatusImage
        {
            get { return (string)GetValue(StatusImageProperty); }
            set
            {

                SetValue(StatusImageProperty, value);
            }
        }
        //模块运行状态字体颜色
        public static readonly DependencyProperty StatusColorProperty =
        DependencyProperty.Register("StatusColor", typeof(Brush), typeof(ModuleNode),
        new PropertyMetadata(Brushes.Transparent));

        public Brush StatusColor
        {
            get { return (Brush)GetValue(StatusColorProperty); }
            set
            {
                SetValue(StatusColorProperty, value);
            }
        }        
        //模块运行 时间
        public static readonly DependencyProperty CostTimeProperty =
        DependencyProperty.Register("CostTime", typeof(string), typeof(ModuleNode),
        new PropertyMetadata("0"));

        public string CostTime
        {
            get { return (string)GetValue(CostTimeProperty); }
            set
            {
                SetValue(CostTimeProperty, value);
            }
        }

        //是否正在运行
        public static readonly DependencyProperty IsRunningProperty =
        DependencyProperty.Register("IsRunning", typeof(bool), typeof(ModuleNode),
        new PropertyMetadata(false));

        public bool IsRunning
        {
            get { return (bool)GetValue(IsRunningProperty); }
            set
            { SetValue(IsRunningProperty, value); }
        }


        //是否第一个元素  要补画上划线
        public static readonly DependencyProperty IsFirstNodeProperty =
        DependencyProperty.Register("IsFirstNode", typeof(bool), typeof(ModuleNode),
        new PropertyMetadata(false));

        public bool IsFirstNode
        {
            get { return (bool)GetValue(IsFirstNodeProperty); }
            set
            { SetValue(IsFirstNodeProperty, value); }

        }

        public bool IsUseSuperTool
        {
            get { return (bool)GetValue(IsUseSuperToolProperty); }
            set { SetValue(IsUseSuperToolProperty, value); }
        }

        public static readonly DependencyProperty IsUseSuperToolProperty =
            DependencyProperty.Register("IsUseSuperTool", typeof(bool), typeof(ModuleNode), new PropertyMetadata(false));


        //是否被多选择 选中
        public static readonly DependencyProperty IsMultiSelectedProperty =
        DependencyProperty.Register("IsMultiSelected", typeof(bool), typeof(ModuleNode),
        new PropertyMetadata(false));

        public bool IsMultiSelected
        {
            get { return (bool)GetValue(IsMultiSelectedProperty); }
            set
            { SetValue(IsMultiSelectedProperty, value); }

        }
        #region NotifyPropertyBase
        public void Set<T>(ref T field, T value, Action action = null, [CallerMemberName] string propName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            RaisePropertyChanged(propName);
            action?.Invoke();
        }
        public event PropertyChangedEventHandler PropertyChanged;

        public void RaisePropertyChanged([CallerMemberName] string propName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        #endregion


    }
}
