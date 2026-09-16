using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;

namespace Plugin.GrabImage.Model
{
    [Serializable]
    public class ImageNameModel:NotifyPropertyBase
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
        private bool _IsSelected;
        /// <summary>
        /// 是否选中
        /// </summary>
        public bool IsSelected
        {
            get { return _IsSelected; }
            set { Set(ref _IsSelected, value); }
        }
        private string _ImageName;
        /// <summary>
        /// 图片名称
        /// </summary>
        public string ImageName
        {
            get { return _ImageName; }
            set { Set(ref _ImageName, value); }
        }
        private string _ImagePath;
        /// <summary>
        /// 图片路径
        /// </summary>
        public string ImagePath
        {
            get { return _ImagePath; }
            set { Set(ref _ImagePath, value); }
        }

    }
}
