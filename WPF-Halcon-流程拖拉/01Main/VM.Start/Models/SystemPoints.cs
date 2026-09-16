using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;
using VM.Start.Services;
using VM.Start.ViewModels;

namespace VM.Start.Models
{
   
    
    public class SystemPointOperate
    {
        #region Singleton
        private static readonly SystemPointOperate _instance = new SystemPointOperate();

        private SystemPointOperate()
        {

        }
        public static SystemPointOperate Ins
        {
            get { return _instance; }
        }
        public void SavePoints()
        { 
        
        }
        public void LoadPoints() 
        {
        
        }
        #endregion
        #region pro
        Dictionary <string , SystemPoint> PointDic= new Dictionary<string , SystemPoint>();
        #endregion

        #region Method

        #endregion
        #region Command

        #endregion

    }
    [Serializable]
    public class SystemPoint: NotifyPropertyBase
    {
        private string _PtName;
        public string PtName 
        {
           get { return _PtName; }
            set { _PtName = value; RaisePropertyChanged(); }
        }
        private string _CardName;
        public string CardName
        {
            get { return _CardName; }
            set { _CardName = value; RaisePropertyChanged(); }
        }
        private string _PointStr;
        public string PointStr 
        {
            get { return _PointStr; }
            set { _PointStr = value; RaisePropertyChanged(); }
        
        }
    }
}
