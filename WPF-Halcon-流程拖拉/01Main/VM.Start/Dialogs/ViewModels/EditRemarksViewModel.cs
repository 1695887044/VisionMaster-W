using System.Windows.Media;
using System.Windows;
using VM.Start.Common.Helper;

namespace VM.Start.Dialogs.ViewModels
{
    public class EditRemarksViewModel : NotifyPropertyBase
    {
        #region Prop

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
        #endregion
    }
}
