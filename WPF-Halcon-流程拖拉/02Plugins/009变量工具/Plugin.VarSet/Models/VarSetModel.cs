using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;
using VM.Start.Script;

namespace Plugin.VarSet.Models
{
    [Serializable]
    public class VarSetModel : NotifyPropertyBase
    {
        private int _Index;

        private object _Value;

        private string _Link;

        private string _Expression = "NULL";

        [NonSerialized]
        private ExpressionScriptSupport _m_TempScriptSupport;

        private string _Note;

        public int Index
        {
            get { return _Index; }
            set
            {
                _Index = value;
                RaisePropertyChanged("Index");
            }
        }

        public string DataType { get; set; }

        public string Name { get; set; }

        public object Value
        {
            get { return _Value; }
            set
            {
                _Value = value;
                RaisePropertyChanged("Value");
            }
        }

        public string Link
        {
            get { return _Link; }
            set
            {
                _Link = value;
                RaisePropertyChanged("Link");
            }
        }

        public string Expression
        {
            get { return _Expression; }
            set
            {
                _Expression = value;
                RaisePropertyChanged("Expression");
            }
        }

        public ExpressionScriptSupport m_TempScriptSupport
        {
            get
            {
                if (_m_TempScriptSupport == null)
                {
                    _m_TempScriptSupport = new ExpressionScriptSupport();
                }
                return _m_TempScriptSupport;
            }
            set { _m_TempScriptSupport = value; }
        }

        [field: NonSerialized]
        public bool IsCompileSuccess { get; set; }

        public string Note
        {
            get { return _Note; }
            set
            {
                _Note = value;
                RaisePropertyChanged("Note");
            }
        }
    }
}
