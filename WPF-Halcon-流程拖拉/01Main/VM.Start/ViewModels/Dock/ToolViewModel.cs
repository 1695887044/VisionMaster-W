using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;

namespace VM.Start.ViewModels.Dock
{
    public class ToolViewModel:NotifyPropertyBase
    {
        #region Singleton
        private static readonly ToolViewModel _instance = new ToolViewModel();

        private ToolViewModel()
        {
        }
        public static ToolViewModel Ins
        {
            get { return _instance; }
        }
        #endregion
        #region Prop        
        private int _SelectedIndex_0;

        public int SelectedIndex_0
        {
            get { return _SelectedIndex_0; }
            set
            {
                _SelectedIndex_0 = value;
                RaisePropertyChanged();
                if (_SelectedIndex_0 != -1)
                {
                    SelectedIndex_1 = -1;
                    SelectedIndex_2 = -1;
                    SelectedIndex_3 = -1;
                    SelectedIndex_4 = -1;
                    SelectedIndex_5 = -1;
                    SelectedIndex_6 = -1;
                    SelectedIndex_7 = -1;
                    SelectedIndex_8 = -1;
                    SelectedIndex_9 = -1;
                    SelectedIndex_10 = -1;
                    SelectedIndex_11 = -1;
                    SelectedIndex_12 = -1;
                    SelectedIndex_13 = -1;
                }
            }
        }

        private int _SelectedIndex_1;

        public int SelectedIndex_1
        {
            get { return _SelectedIndex_1; }
            set 
            { 
                _SelectedIndex_1 = value; 
                RaisePropertyChanged();
                if (_SelectedIndex_1 != -1)
                {
                    SelectedIndex_0 = -1;
                    SelectedIndex_2 = -1;
                    SelectedIndex_3 = -1;
                    SelectedIndex_4 = -1;
                    SelectedIndex_5 = -1;
                    SelectedIndex_6 = -1;
                    SelectedIndex_7 = -1;
                    SelectedIndex_8 = -1;
                    SelectedIndex_9 = -1;
                    SelectedIndex_10 = -1;
                    SelectedIndex_11 = -1;
                    SelectedIndex_12 = -1;
                    SelectedIndex_13 = -1;
                }
            }
        }
        private int _SelectedIndex_2;

        public int SelectedIndex_2
        {
            get { return _SelectedIndex_2; }
            set
            {
                _SelectedIndex_2 = value;
                RaisePropertyChanged();
                if (_SelectedIndex_2 != -1)
                {
                    SelectedIndex_0 = -1;
                    SelectedIndex_1 = -1;
                    SelectedIndex_3 = -1;
                    SelectedIndex_4 = -1;
                    SelectedIndex_5 = -1;
                    SelectedIndex_6 = -1;
                    SelectedIndex_7 = -1;
                    SelectedIndex_8 = -1;
                    SelectedIndex_9 = -1;
                    SelectedIndex_10 = -1;
                    SelectedIndex_11 = -1;
                    SelectedIndex_12 = -1;
                    SelectedIndex_13 = -1;
                }
            }
        }
        private int _SelectedIndex_3;

        public int SelectedIndex_3
        {
            get { return _SelectedIndex_3; }
            set
            {
                _SelectedIndex_3 = value;
                RaisePropertyChanged();
                if (_SelectedIndex_3 != -1)
                {
                    SelectedIndex_0 = -1;
                    SelectedIndex_1 = -1;
                    SelectedIndex_2 = -1;
                    SelectedIndex_4 = -1;
                    SelectedIndex_5 = -1;
                    SelectedIndex_6 = -1;
                    SelectedIndex_7 = -1;
                    SelectedIndex_8 = -1;
                    SelectedIndex_9 = -1;
                    SelectedIndex_10 = -1;
                    SelectedIndex_11 = -1;
                    SelectedIndex_12 = -1;
                    SelectedIndex_13 = -1;
                }
            }
        }
        private int _SelectedIndex_4;

        public int SelectedIndex_4
        {
            get { return _SelectedIndex_4; }
            set
            {
                _SelectedIndex_4 = value;
                RaisePropertyChanged();
                if (_SelectedIndex_4 != -1)
                {
                    SelectedIndex_0 = -1;
                    SelectedIndex_1 = -1;
                    SelectedIndex_2 = -1;
                    SelectedIndex_3 = -1;
                    SelectedIndex_5 = -1;
                    SelectedIndex_6 = -1;
                    SelectedIndex_7 = -1;
                    SelectedIndex_8 = -1;
                    SelectedIndex_9 = -1;
                    SelectedIndex_10 = -1;
                    SelectedIndex_11 = -1;
                    SelectedIndex_12 = -1;
                    SelectedIndex_13 = -1;
                }
            }
        }
        private int _SelectedIndex_5;

        public int SelectedIndex_5
        {
            get { return _SelectedIndex_5; }
            set
            {
                _SelectedIndex_5 = value;
                RaisePropertyChanged();
                if (_SelectedIndex_5 != -1)
                {
                    SelectedIndex_0 = -1;
                    SelectedIndex_1 = -1;
                    SelectedIndex_2 = -1;
                    SelectedIndex_3 = -1;
                    SelectedIndex_4 = -1;
                    SelectedIndex_6 = -1;
                    SelectedIndex_7 = -1;
                    SelectedIndex_8 = -1;
                    SelectedIndex_9 = -1;
                    SelectedIndex_10 = -1;
                    SelectedIndex_11 = -1;
                    SelectedIndex_12 = -1;
                    SelectedIndex_13 = -1;
                }
            }
        }
        private int _SelectedIndex_6;

        public int SelectedIndex_6
        {
            get { return _SelectedIndex_6; }
            set
            {
                _SelectedIndex_6 = value;
                RaisePropertyChanged();
                if (_SelectedIndex_6 != -1)
                {
                    SelectedIndex_0 = -1;
                    SelectedIndex_1 = -1;
                    SelectedIndex_2 = -1;
                    SelectedIndex_3 = -1;
                    SelectedIndex_4 = -1;
                    SelectedIndex_5 = -1;
                    SelectedIndex_7 = -1;
                    SelectedIndex_8 = -1;
                    SelectedIndex_9 = -1;
                    SelectedIndex_10 = -1;
                    SelectedIndex_11 = -1;
                    SelectedIndex_12 = -1;
                    SelectedIndex_13 = -1;
                }
            }
        }
        private int _SelectedIndex_7;

        public int SelectedIndex_7
        {
            get { return _SelectedIndex_7; }
            set
            {
                _SelectedIndex_7 = value;
                RaisePropertyChanged();
                if (_SelectedIndex_7 != -1)
                {
                    SelectedIndex_0 = -1;
                    SelectedIndex_1 = -1;
                    SelectedIndex_2 = -1;
                    SelectedIndex_3 = -1;
                    SelectedIndex_4 = -1;
                    SelectedIndex_5 = -1;
                    SelectedIndex_6 = -1;
                    SelectedIndex_8 = -1;
                    SelectedIndex_9 = -1;
                    SelectedIndex_10 = -1;
                    SelectedIndex_11 = -1;
                    SelectedIndex_12 = -1;
                    SelectedIndex_13 = -1;
                }
            }
        }
        private int _SelectedIndex_8;

        public int SelectedIndex_8
        {
            get { return _SelectedIndex_8; }
            set
            {
                _SelectedIndex_8 = value;
                RaisePropertyChanged();
                if (_SelectedIndex_8 != -1)
                {
                    SelectedIndex_0 = -1;
                    SelectedIndex_1 = -1;
                    SelectedIndex_2 = -1;
                    SelectedIndex_3 = -1;
                    SelectedIndex_4 = -1;
                    SelectedIndex_5 = -1;
                    SelectedIndex_6 = -1;
                    SelectedIndex_7 = -1;
                    SelectedIndex_9 = -1;
                    SelectedIndex_10 = -1;
                    SelectedIndex_11 = -1;
                    SelectedIndex_12 = -1;
                    SelectedIndex_13 = -1;
                }
            }
        }
        private int _SelectedIndex_9;

        public int SelectedIndex_9
        {
            get { return _SelectedIndex_9; }
            set
            {
                _SelectedIndex_9 = value;
                RaisePropertyChanged();
                if (_SelectedIndex_9 != -1)
                {
                    SelectedIndex_0 = -1;
                    SelectedIndex_1 = -1;
                    SelectedIndex_2 = -1;
                    SelectedIndex_3 = -1;
                    SelectedIndex_4 = -1;
                    SelectedIndex_5 = -1;
                    SelectedIndex_6 = -1;
                    SelectedIndex_7 = -1;
                    SelectedIndex_8 = -1;
                    SelectedIndex_10 = -1;
                    SelectedIndex_11 = -1;
                    SelectedIndex_12 = -1;
                    SelectedIndex_13 = -1;
                }
            }
        }
        private int _SelectedIndex_10;

        public int SelectedIndex_10
        {
            get { return _SelectedIndex_10; }
            set
            {
                _SelectedIndex_10 = value;
                RaisePropertyChanged();
                if (_SelectedIndex_10 != -1)
                {
                    SelectedIndex_0 = -1;
                    SelectedIndex_1 = -1;
                    SelectedIndex_2 = -1;
                    SelectedIndex_3 = -1;
                    SelectedIndex_4 = -1;
                    SelectedIndex_5 = -1;
                    SelectedIndex_6 = -1;
                    SelectedIndex_7 = -1;
                    SelectedIndex_8 = -1;
                    SelectedIndex_9 = -1;
                    SelectedIndex_11 = -1;
                    SelectedIndex_12 = -1;
                    SelectedIndex_13 = -1;
                }
            }
        }
        private int _SelectedIndex_11;

        public int SelectedIndex_11
        {
            get { return _SelectedIndex_11; }
            set
            {
                _SelectedIndex_11 = value;
                RaisePropertyChanged();
                if (_SelectedIndex_11 != -1)
                {
                    SelectedIndex_0 = -1;
                    SelectedIndex_1 = -1;
                    SelectedIndex_2 = -1;
                    SelectedIndex_3 = -1;
                    SelectedIndex_4 = -1;
                    SelectedIndex_5 = -1;
                    SelectedIndex_6 = -1;
                    SelectedIndex_7 = -1;
                    SelectedIndex_8 = -1;
                    SelectedIndex_9 = -1;
                    SelectedIndex_10 = -1;
                    SelectedIndex_12 = -1;
                    SelectedIndex_13 = -1;
                }
            }
        }
        private int _SelectedIndex_12;

        public int SelectedIndex_12
        {
            get { return _SelectedIndex_12; }
            set
            {
                _SelectedIndex_12 = value;
                RaisePropertyChanged();
                if (_SelectedIndex_12 != -1)
                {
                    SelectedIndex_0 = -1;
                    SelectedIndex_1 = -1;
                    SelectedIndex_2 = -1;
                    SelectedIndex_3 = -1;
                    SelectedIndex_4 = -1;
                    SelectedIndex_5 = -1;
                    SelectedIndex_6 = -1;
                    SelectedIndex_7 = -1;
                    SelectedIndex_8 = -1;
                    SelectedIndex_9 = -1;
                    SelectedIndex_10 = -1;
                    SelectedIndex_11 = -1;
                    SelectedIndex_13 = -1;
                }
            }
        }
        private int _SelectedIndex_13;

        public int SelectedIndex_13
        {
            get { return _SelectedIndex_13; }
            set
            {
                _SelectedIndex_13 = value;
                RaisePropertyChanged();
                if (_SelectedIndex_13 != -1)
                {
                    SelectedIndex_0 = -1;
                    SelectedIndex_1 = -1;
                    SelectedIndex_2 = -1;
                    SelectedIndex_3 = -1;
                    SelectedIndex_4 = -1;
                    SelectedIndex_5 = -1;
                    SelectedIndex_6 = -1;
                    SelectedIndex_7 = -1;
                    SelectedIndex_8 = -1;
                    SelectedIndex_9 = -1;
                    SelectedIndex_10 = -1;
                    SelectedIndex_11 = -1;
                    SelectedIndex_12 = -1;
                }
            }
        }

        #endregion

    }
}
