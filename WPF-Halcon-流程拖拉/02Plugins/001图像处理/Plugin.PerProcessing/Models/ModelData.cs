using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using VM.Start.Common.Enums;
using VM.Start.Common.Helper;

namespace Plugin.PerProcessing.Models
{
    #region enum
    public enum eOperatorType
    {
        彩色转灰,
        图像镜像,
        图像旋转,
        修改图像尺寸,

        均值滤波,
        中值滤波,
        高斯滤波,

        灰度膨胀,
        灰度腐蚀,

        锐化,
        对比度,
        亮度调节,
        灰度开运算,
        灰度闭运算,
        反色,

        二值化,
        均值二值化,
    }
    public enum eTransImageType
    {
        通用比例转换,
        RGB,
        HSV,
        HSI,
        YUV,
    }
    public enum eTransImageChannel
    {
        第一通道,
        第二通道,
        第三通道,
    }
    public enum eMirrorImageType
    {
        水平镜像,
        垂直镜像,
        对角镜像,
    }
    public enum eVarThresholdType
    {
        大于等于,
        小于等于,
        等于,
        不等于,
    }
    public enum eRotateImageAngle
    { 
        _90,
        _180,
        _270,
    }
    #endregion

    [Serializable]
    public class ModelData : NotifyPropertyBase
    {
        [field: NonSerialized]
        public Array TransImageTypes   //图像转换类型
        {
            get { return Enum.GetValues(typeof(eTransImageType)); }
            set {; }
        }
        [field: NonSerialized]
        public Array TransImageChannels   //转换通道
        {
            get { return Enum.GetValues(typeof(eTransImageChannel)); }
            set {; }
        }
        [field: NonSerialized]
        public Array MirrorImageTypes   //镜像类型
        {
            get { return Enum.GetValues(typeof(eMirrorImageType)); }
            set {; }
        }
        [field: NonSerialized]
        public Array VarThresholdTypes   //比较类型
        {
            get { return Enum.GetValues(typeof(eVarThresholdType)); }
            set {; }
        }
        [field: NonSerialized]
        public Array RotateImageAngles   //旋转角度
        {
            get { return Enum.GetValues(typeof(eRotateImageAngle)); }
            set {; }
        }
        /// <summary>
        /// 启用
        /// </summary>
        private bool _m_enable = true;
        public bool m_enable
        {
            get { return _m_enable; }
            set { Set(ref _m_enable, value); }
        }
        /// <summary>
        /// 数据类型
        /// </summary>
        private eOperatorType _m_name;
        public eOperatorType m_name
        {
            get { return _m_name; }
            set { Set(ref _m_name, value); }
        }
        /// <summary>
        /// 显示数据
        /// </summary>
        private string _m_value;
        public string m_value
        {
            get
            {
                if (_m_value == null)
                    _m_value = string.Empty;
                switch (m_name)
                {
                    //图像调整
                    case eOperatorType.彩色转灰:
                        _m_value = m_TransImageType.ToString() + " ; " + m_TransImageChannel;
                        break;
                    case eOperatorType.图像镜像:
                        _m_value = m_MirrorImageType.ToString();
                        break;
                    case eOperatorType.图像旋转:
                        _m_value = m_RotateImageAngle.ToString();
                        break;
                    case eOperatorType.修改图像尺寸:
                        _m_value = m_ChangeImageWidth.ToString() + " ; " + m_ChangeImageHeight.ToString();
                        break;
                    //滤波
                    case eOperatorType.均值滤波:
                        _m_value = m_MeanImageWidth.ToString() + " ; " + m_MeanImageHeight.ToString();
                        break;
                    case eOperatorType.中值滤波:
                        _m_value = m_MedianImageWidth.ToString() + " ; " + m_MedianImageHeight.ToString();
                        break;
                    case eOperatorType.高斯滤波:
                        _m_value = m_GaussImageSize.ToString();
                        break;
                    //形态学运算
                    case eOperatorType.灰度膨胀:
                        _m_value = m_GrayDilationWidth.ToString() + " ; " + m_GrayDilationHeight.ToString();
                        break;
                    case eOperatorType.灰度腐蚀:
                        _m_value = m_GrayErosionWidth.ToString() + " ; " + m_GrayErosionHeight.ToString();
                        break;
                    //图像增强
                    case eOperatorType.锐化:
                        _m_value = m_EmphaSizeWidth.ToString() + " ; " + m_EmphaSizeHeight.ToString() + " ; " + m_EmphaSizeFactor.ToString();
                        break;
                    case eOperatorType.对比度:
                        _m_value = m_IlluminateWidth.ToString() + " ; " + m_IlluminateHeight.ToString() + " ; " + m_IlluminateFactor.ToString();
                        break;
                    case eOperatorType.亮度调节:
                        _m_value = m_ScaleImageMult.ToString() + " ; " + m_ScaleImageAdd.ToString();
                        break;
                    case eOperatorType.灰度开运算:
                        _m_value = m_OpeningWidth.ToString() + " ; " + m_OpeningHeight.ToString();
                        break;
                    case eOperatorType.灰度闭运算:
                        _m_value = m_ClosingWidth.ToString() + " ; " + m_ClosingHeight.ToString();
                        break;
                    case eOperatorType.反色:
                        _m_value = m_InvertImageLogic.ToString();
                        break;
                    //二值化
                    case eOperatorType.二值化:
                        _m_value = m_ThresholdLow.ToString() + " ; " + m_ThresholdHight.ToString() + " ; " +
                                   m_ThresholdReverse.ToString();
                        break;
                    case eOperatorType.均值二值化:
                        _m_value = m_VarThresholdWidth.ToString() + " ; " + m_VarThresholdHeight.ToString() + " ; " +
                                   m_VarThresholdSkew.ToString() + " ; " + m_VarThresholdType.ToString();
                        break;
                }
                return _m_value;
            }
            set { Set(ref _m_value, value); }
        }

        #region 图像调整
        /// <summary>
        /// 彩色转灰,
        /// </summary>
        private eTransImageType _m_TransImageType = eTransImageType.通用比例转换; //转换类型：1.通用比例转换;2.RGB;3.HSV;4.HSI;5.YUV
        public eTransImageType m_TransImageType
        {
            get { return _m_TransImageType; }
            set {Set(ref _m_TransImageType, value);m_value = "";}  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private eTransImageChannel _m_TransImageChannel = eTransImageChannel.第一通道;  //通道：1.第一通道;2.第二通道;3.第三通道
        public eTransImageChannel m_TransImageChannel
        {
            get { return _m_TransImageChannel; }
            set { Set(ref _m_TransImageChannel, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        /// <summary>
        /// 图像镜像,
        /// </summary>
        private eMirrorImageType _m_MirrorImageType = eMirrorImageType.水平镜像; //镜像类型：1.水平镜像；2.垂直镜像；3.对角镜像
        public eMirrorImageType m_MirrorImageType
        {
            get { return _m_MirrorImageType; }
            set { Set(ref _m_MirrorImageType, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        /// <summary>
        /// 图像旋转，
        /// </summary>
        private eRotateImageAngle _m_RotateImageAngle = eRotateImageAngle._90; //旋转角度；1.90；180；270
        public eRotateImageAngle m_RotateImageAngle
        {
            get { return _m_RotateImageAngle; }
            set { Set(ref _m_RotateImageAngle, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        /// <summary>
        /// 修改图像尺寸
        /// </summary>
        private int _m_ChangeImageWidth = 500; //宽            1-999  step = 1
        public int m_ChangeImageWidth
        {
            get { return _m_ChangeImageWidth; }
            set { Set(ref _m_ChangeImageWidth, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private int _m_ChangeImageHeight = 400; //高           1-999  step = 1
        public int m_ChangeImageHeight
        {
            get { return _m_ChangeImageHeight; }
            set { Set(ref _m_ChangeImageHeight, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        #endregion
        #region 滤波
        /// <summary>
        /// 均值滤波
        /// </summary>
        private int _m_MeanImageWidth = 5; //宽度             1-999  step = 1
        public int m_MeanImageWidth
        {
            get { return _m_MeanImageWidth; }
            set { Set(ref _m_MeanImageWidth, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private int _m_MeanImageHeight = 5; //高度        1-999  step = 1
        public int m_MeanImageHeight
        {
            get { return _m_MeanImageHeight; }
            set { Set(ref _m_MeanImageHeight, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        /// <summary>
        /// 中值滤波
        /// </summary>
        private int _m_MedianImageWidth = 5;//宽度     1-999  step = 1
        public int m_MedianImageWidth
        {
            get { return _m_MedianImageWidth; }
            set { Set(ref _m_MedianImageWidth, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private int _m_MedianImageHeight = 5;//高度    1-999  step = 1
        public int m_MedianImageHeight
        {
            get { return _m_MedianImageHeight; }
            set { Set(ref _m_MedianImageHeight, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        /// <summary>
        /// 高斯滤波大小
        /// </summary>
        private int _m_GaussImageSize = 5;           //3-11  step = 2(奇数)
        public int m_GaussImageSize
        {
            get { return _m_GaussImageSize; }
            set { Set(ref _m_GaussImageSize, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        #endregion
        #region 形态学运算
        /// <summary>
        /// 灰度膨胀
        /// </summary>
        private int _m_GrayDilationWidth = 5; //宽度 1-9999
        public int m_GrayDilationWidth
        {
            get { return _m_GrayDilationWidth; }
            set { Set(ref _m_GrayDilationWidth, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private int _m_GrayDilationHeight = 5; //高度 1-9999
        public int m_GrayDilationHeight
        {
            get { return _m_GrayDilationHeight; }
            set { Set(ref _m_GrayDilationHeight, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        /// <summary>
        /// 灰度腐蚀
        /// </summary>
        private int _m_GrayErosionWidth = 5; //宽度 1-9999
        public int m_GrayErosionWidth
        {
            get { return _m_GrayErosionWidth; }
            set { Set(ref _m_GrayErosionWidth, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private int _m_GrayErosionHeight = 5; //高度 1-9999
        public int m_GrayErosionHeight
        {
            get { return _m_GrayErosionHeight; }
            set { Set(ref _m_GrayErosionHeight, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        #endregion
        #region 图像增强
        /// <summary>
        /// 图像锐化
        /// </summary>
        private int _m_EmphaSizeWidth = 3;//宽度   3-201  奇数
        public int m_EmphaSizeWidth
        {
            get { return _m_EmphaSizeWidth; }
            set { Set(ref _m_EmphaSizeWidth, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private int _m_EmphaSizeHeight = 3;//高度   3-201  奇数
        public int m_EmphaSizeHeight
        {
            get { return _m_EmphaSizeHeight; }
            set { Set(ref _m_EmphaSizeHeight, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private double _m_EmphaSizeFactor = 0.3; //对比因子  0.3-20  step=0.1
        public double m_EmphaSizeFactor
        {
            get { return _m_EmphaSizeFactor; }
            set { Set(ref _m_EmphaSizeFactor, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        /// <summary>
        /// 对比度
        /// </summary>
        private int _m_IlluminateWidth = 101;//宽度  3-299 奇数
        public int m_IlluminateWidth
        {
            get { return _m_IlluminateWidth; }
            set { Set(ref _m_IlluminateWidth, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private int _m_IlluminateHeight = 101;//高度  3-299  奇数
        public int m_IlluminateHeight
        {
            get { return _m_IlluminateHeight; }
            set { Set(ref _m_IlluminateHeight, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private double _m_IlluminateFactor = 0.7;//对比因子   0-0.5  step = 0.1
        public double m_IlluminateFactor
        {
            get { return _m_IlluminateFactor; }
            set { Set(ref _m_IlluminateFactor, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        /// <summary>
        /// 亮度调节
        /// </summary>
        private double _m_ScaleImageMult  =0.1;//Mult   0.1-99999 step = 0.1
        public double m_ScaleImageMult
        {
            get { return _m_ScaleImageMult; }
            set { Set(ref _m_ScaleImageMult, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private int _m_ScaleImageAdd = 1;//Add     -999 - 999   step=1
        public int m_ScaleImageAdd
        {
            get { return _m_ScaleImageAdd; }
            set { Set(ref _m_ScaleImageAdd, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        /// <summary>
        /// 灰度开运算
        /// </summary>
        private int _m_OpeningWidth = 5;//宽度     1-999 step=1
        public int m_OpeningWidth
        {
            get { return _m_OpeningWidth; }
            set { Set(ref _m_OpeningWidth, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private int _m_OpeningHeight =5;//高度          1-999 step=1
        public int m_OpeningHeight
        {
            get { return _m_OpeningHeight; }
            set { Set(ref _m_OpeningHeight, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        /// <summary>
        /// 灰度闭运算
        /// </summary>
        private int _m_ClosingWidth =5;//宽度               1-999 step=1
        public int m_ClosingWidth
        {
            get { return _m_ClosingWidth; }
            set { Set(ref _m_ClosingWidth, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private int _m_ClosingHeight = 5;//高度                     1-999 step=1
        public int m_ClosingHeight
        {
            get { return _m_ClosingHeight; }
            set { Set(ref _m_ClosingHeight, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        /// <summary>
        /// 图像反色
        /// </summary>
        private bool _m_InvertImageLogic;
        public bool m_InvertImageLogic
        {
            get { return _m_InvertImageLogic; }
            set { Set(ref _m_InvertImageLogic, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        #endregion       
        #region 二值化
        /// <summary>
        /// 二值化
        /// </summary>
        private int _m_ThresholdLow = 50;//低阈值   0-255  step=1
        public int m_ThresholdLow
        {
            get { return _m_ThresholdLow; }
            set { Set(ref _m_ThresholdLow, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private int _m_ThresholdHight = 200;//高阈值  0-255  step=1
        public int m_ThresholdHight
        {
            get { return _m_ThresholdHight; }
            set { Set(ref _m_ThresholdHight, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private bool _m_ThresholdReverse = false;//黑白反转
        public bool m_ThresholdReverse
        {
            get { return _m_ThresholdReverse; }
            set { Set(ref _m_ThresholdReverse, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        /// <summary>
        /// 均值二值化
        /// </summary>
        private int _m_VarThresholdWidth = 50;//宽度  1-999  step=1
        public int m_VarThresholdWidth
        {
            get { return _m_VarThresholdWidth; }
            set { Set(ref _m_VarThresholdWidth, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private int _m_VarThresholdHeight = 50;//高度   1-999  step=1
        public int m_VarThresholdHeight
        {
            get { return _m_VarThresholdHeight; }
            set { Set(ref _m_VarThresholdHeight, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private int _m_VarThresholdSkew = 30;//阈值偏移  1-999  step=1
        public int m_VarThresholdSkew
        {
            get { return _m_VarThresholdSkew; }
            set { Set(ref _m_VarThresholdSkew, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        private eVarThresholdType _m_VarThresholdType = eVarThresholdType.大于等于;//比较类型
        public eVarThresholdType m_VarThresholdType
        {
            get { return _m_VarThresholdType; }
            set { Set(ref _m_VarThresholdType, value); m_value = ""; }  //m_value="" 是为了改变一下值，以便界面更新m_value
        }
        #endregion
    }
}
