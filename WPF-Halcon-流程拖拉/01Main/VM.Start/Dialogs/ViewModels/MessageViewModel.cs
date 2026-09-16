using System.Windows.Media;
using System.Windows;
using VM.Start.Common.Helper;

namespace VM.Start.Dialogs.ViewModels
{
    public class MessageViewModel : NotifyPropertyBase
    {
        #region Prop

        private string _Message;
        /// <summary>
        /// 显示内容
        /// </summary>
        public string Message
        {
            get { return _Message; }
            set { Set(ref _Message, value); }
        }
        private string _ToolBarMsg;
        /// <summary>
        /// 工具条内容
        /// </summary>
        public string ToolBarMsg
        {
            get { return _ToolBarMsg; }
            set { Set(ref _ToolBarMsg, value); }
        }
        private ImageSource _Icon;
        /// <summary>
        /// 图标
        /// </summary>
        public ImageSource Icon
        {
            get { return _Icon; }
            set { Set(ref _Icon, value); }
        }
        private Visibility _ConfirmVisibility = Visibility.Visible;
        /// <summary>
        /// 确认按钮显示状态
        /// </summary>
        public Visibility ConfirmVisibility
        {
            get { return _ConfirmVisibility; }
            set { Set(ref _ConfirmVisibility, value); }
        }
        private Visibility _CancelVisibility = Visibility.Collapsed;
        /// <summary>
        /// 取消按钮显示状态
        /// </summary>
        public Visibility CancelVisibility
        {
            get { return _CancelVisibility; }
            set { Set(ref _CancelVisibility, value); }
        }
        private bool _IsCloseButtonEnabled = true;
        /// <summary>
        /// 关闭按钮使能
        /// </summary>
        public bool IsCloseButtonEnabled
        {
            get { return _IsCloseButtonEnabled; }
            set { Set(ref _IsCloseButtonEnabled, value); }
        }
        private bool _IsMinButtonEnabled = true;
        /// <summary>
        /// 最小化按钮使能
        /// </summary>
        public bool IsMinButtonEnabled
        {
            get { return _IsMinButtonEnabled; }
            set { Set(ref _IsMinButtonEnabled, value); }
        }


        #endregion
    }
}
