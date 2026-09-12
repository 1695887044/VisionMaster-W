using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;
using VM.Start.Dialogs.Views;

namespace VM.Start.Dialogs.ViewModels
{
    public class AboutViewModel:NotifyPropertyBase
    {
        #region Singleton
        private static readonly AboutViewModel Instance = new AboutViewModel();

        public AboutViewModel()
        {
        }

        public static AboutViewModel Ins
        {
            get { return Instance; }
        }

        #endregion
        private string _ActiveStateText;
        /// <summary>
        /// 激活状态
        /// </summary>
        public string ActiveStateText
        {
            get { return _ActiveStateText; }
            set { Set(ref _ActiveStateText, value); }
        }
        private Brush _ActiveStateColor;
        /// <summary>
        /// 激活状态显示文本的颜色
        /// </summary>
        public Brush ActiveStateColor
        {
            get { return _ActiveStateColor; }
            set { Set(ref _ActiveStateColor, value); }
        }
        private eActiveState _ActiveState;
        /// <summary>
        /// 激活状态
        /// </summary>
        public eActiveState ActiveState
        {
            get { return _ActiveState; }
            set
            {
                _ActiveState = value;
                switch (value)
                {
                    case eActiveState.Actived:
                        ActiveStateText = "已激活";
                        ActiveStateColor = Brushes.Lime;
                        break;
                    case eActiveState.NotActived:
                        ActiveStateText = "未激活";
                        ActiveStateColor = Brushes.Red;
                        break;
                    case eActiveState.Probation:
                        ActiveStateText = "试用";
                        ActiveStateColor = Brushes.Lime; 
                        break;
                }
            }
        }

    }
}
