using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;

namespace VM.Start.Models
{
    [Serializable]
    public class LinkVarModel : NotifyPropertyBase
    {
        public Action TextChanged;
        private string _Text = "";
        /// <summary>
        /// 显示文本
        /// </summary>
        public string Text
        {
            get { return _Text; }
            set
            {
                _Text = value;
                RaisePropertyChanged();
                if (_Text.StartsWith("&"))
                {
                    IsReadOnly = true;
                }
                else
                {
                    Value = _Text;
                    IsReadOnly = false;
                }
                TextChanged?.Invoke();
            }
        }
        private bool _IsReadOnly = false;
        /// <summary>
        /// 只读
        /// </summary>
        public bool IsReadOnly
        {
            get { return _IsReadOnly; }
            set
            {
                _IsReadOnly = value;
                RaisePropertyChanged();
            }
        }
        private object _Value;
        /// <summary>
        /// 值
        /// </summary>
        public object Value
        {
            get { return _Value; }
            set 
            {
                if (value==null || value.ToString().StartsWith("&")) return;
                _Value = value;
                RaisePropertyChanged();
            }
        }
    }
}
