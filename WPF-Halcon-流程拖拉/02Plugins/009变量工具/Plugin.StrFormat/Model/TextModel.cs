using EventMgrLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using VM.Start.Common;
using VM.Start.Common.Helper;
using VM.Start.Events;
using VM.Start.Models;
using VM.Start.ViewModels;
using VM.Start.Views;

namespace Plugin.GrabImage.Model
{
    [Serializable]
    public class TextModel:NotifyPropertyBase
    {
        private int _ID;
        /// <summary>
        /// ID
        /// </summary>
        public int ID
        {
            get { return _ID; }
            set { Set(ref _ID, value); }
        }
        private int _X_Pos;
        /// <summary>
        /// X坐标
        /// </summary>
        public int X_Pos
        {
            get { return _X_Pos; }
            set { Set(ref _X_Pos, value); }
        }
        private int _Y_Pos;
        /// <summary>
        /// Y坐标
        /// </summary>
        public int Y_Pos
        {
            get { return _Y_Pos; }
            set { Set(ref _Y_Pos, value); }
        }
        private string _StatusLink;
        /// <summary>
        /// 状态链接
        /// </summary>
        public string StatusLink
        {
            get { return _StatusLink; }
            set { Set(ref _StatusLink, value); }
        }
        private string _DispContent;
        /// <summary>
        /// 显示内容
        /// </summary>
        public string DispContent
        {
            get { return _DispContent; }
            set { Set(ref _DispContent, value); }
        }
        private string _Prefix;
        /// <summary>
        /// 前缀
        /// </summary>
        public string Prefix
        {
            get { return _Prefix; }
            set { Set(ref _Prefix, value); }
        }
        private string _Suffix;
        /// <summary>
        /// 后缀
        /// </summary>
        public string Suffix
        {
            get { return _Suffix; }
            set { Set(ref _Suffix, value); }
        }
        [field: NonSerialized]
        private Color _OK_Color;
        /// <summary>
        /// OK颜色
        /// </summary>
        public Color OK_Color
        {
            get { return _OK_Color; }
            set { Set(ref _OK_Color, value); }
        }
        [field: NonSerialized]
        private Color _NG_Color;
        /// <summary>
        /// NG颜色
        /// </summary>
        public Color NG_Color
        {
            get { return _NG_Color; }
            set { Set(ref _NG_Color, value); }
        }


    }
}
