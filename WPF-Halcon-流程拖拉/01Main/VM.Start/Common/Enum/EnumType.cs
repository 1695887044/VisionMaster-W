using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Markup;
using System.Xml.Linq;
using VM.Start.Properties;

namespace VM.Start.Common.Enums
{
    /// <summary>枚举名称</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class EnumDescriptionAttribute : Attribute
    {
        public string Description { get; }
        public EnumDescriptionAttribute(string description) : base()
        {
            this.Description = description;
        }
    }
    /// <summary>获取枚举字符串</summary>
    public static class REnum
    {
        public static string EnumToStr(Enum value)
        {
            if (value == null)
            {
                throw new ArgumentException("value");
            }
            string description = value.ToString();
            Type type = value.GetType();
            var fieldInfo = value.GetType().GetField(description);
            var attributes =
                (EnumDescriptionAttribute[])fieldInfo.GetCustomAttributes(typeof(EnumDescriptionAttribute), false);
            if (attributes != null && attributes.Length > 0)
            {
                description = attributes[0].Description;
            }
            return description;
        }
        /// <summary>
        /// get all information of enum,include value,name and description
        /// </summary>
        /// <param name="enumName">the type of enumName</param>
        /// <returns></returns>
        public static List<string> GetEnumDescription(Type enumName)
        {
            List<string> list = new List<string>();
            // get enum fileds
            FieldInfo[] fields = enumName.GetFields();
            foreach (FieldInfo field in fields)
            {
                if (!field.FieldType.IsEnum)
                {
                    continue;
                }
                // get enum value
                int value = (int)enumName.InvokeMember(field.Name, BindingFlags.GetField, null, null, null);
                string text = field.Name;
                string description = string.Empty;
                object[] array = field.GetCustomAttributes(typeof(EnumDescriptionAttribute), false);
                if (array.Length > 0)
                {
                    description = ((EnumDescriptionAttribute)array[0]).Description;
                }
                else
                {
                    description = ""; //none description,set empty
                }
                //add to list
                list.Add(description);
            }
            return list;
        }
    }
    public class EnumDescriptionTypeConverter : EnumConverter
    {
        public EnumDescriptionTypeConverter(Type type) : base(type)
        {
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string))
            {
                if (null != value)
                {
                    FieldInfo fi = value.GetType().GetField(value.ToString());

                    if (null != fi)
                    {
                        var attributes =
                            (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

                        return ((attributes.Length > 0) && (!string.IsNullOrEmpty(attributes[0].Description)))
                            ? attributes[0].Description
                            : value.ToString();
                    }
                }

                return string.Empty;
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }

    public class EnumBindingSourceExtension : MarkupExtension
    {
        private Type _enumType;

        public Type EnumType
        {
            get { return _enumType; }
            set
            {
                if (value != _enumType)
                {
                    if (null != value)
                    {
                        var enumType = Nullable.GetUnderlyingType(value) ?? value;
                        if (!enumType.IsEnum)
                        {
                            throw new ArgumentException("Type must bu for an Enum");
                        }

                    }

                    _enumType = value;
                }
            }
        }

        public EnumBindingSourceExtension()
        {

        }

        public EnumBindingSourceExtension(Type enumType)
        {
            EnumType = enumType;
        }
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (null == _enumType)
            {
                throw new InvalidOperationException("The EnumTYpe must be specified.");
            }

            var actualEnumType = Nullable.GetUnderlyingType(_enumType) ?? _enumType;
            var enumValues = Enum.GetValues(actualEnumType);

            if (actualEnumType == _enumType)
            {
                return enumValues;
            }

            var tempArray = Array.CreateInstance(actualEnumType, enumValues.Length + 1);
            enumValues.CopyTo(tempArray, 1);

            return tempArray;
        }
    }

    [Serializable]
    [TypeConverter(typeof(EnumDescriptionTypeConverter))]
    public enum eLoopMode
    {
        [Description("从Start到End-1递增")]
        Increase,
        [Description("从End-1到Start递减")]
        Decrease,
        [Description("无限循环")]
        Loop,
        [Description("遍历数组")]
        Foreach,
    }

    /// <summary>
    /// 流程状态
    /// </summary>
    [Serializable]
    public enum eRunMode
    {
        None = 0,
        /// <summary>
        /// 运行一次
        /// </summary>
        RunOnce = 1,
        /// <summary>
        /// 循环运行
        /// </summary>
        RunCycle = 2,
    }

    [Serializable]
    public enum eProjectAutoRunMode
    {
        主动执行 = 0,
        调用执行 = 1,
    }
    /// <summary>
    /// 操作类型：加载，增加，删除
    /// </summary>
    public enum eOperateType
    {
        Add, Load, Remove, Clear
    }
    [Serializable]
    public enum eCommunicationType
    {
        TCP客户端 = 0,//客户端
        TCP服务器 = 1,//服务端
        UDP通讯 = 2,//udp
        串口通讯 = 3,//串口
    }

    public enum eTypes
    {
        Int,
        Double,
        String,
        HObject,
        HTuple,
        HImage,
        HRegion,
        HXld
    }
    public enum eRunStatus
    {
        /// <summary>
        /// 运行成功
        /// </summary>
        OK = 0,
        /// <summary>
        /// 运行失败
        /// </summary>
        NG = 1,
        /// <summary>
        /// 未运行
        /// </summary>
        NotRun = 2,
        /// <summary>
        /// 运行中
        /// </summary>
        Running = 3,
    }

    public enum eProjectType
    {
        /// <summary>
        /// 流程
        /// </summary>
        Process,
        /// <summary>
        /// 方法
        /// </summary>
        Method,
        /// <summary>
        /// 文件夹
        /// </summary>
        Folder,
    }
    public enum eMsgType
    {
        /// <summary>
        /// 成功
        /// </summary>
        Success,
        /// <summary>
        /// 消息
        /// </summary>
        Info,
        /// <summary>
        /// 警告
        /// </summary>
        Warn,
        /// <summary>
        /// 报错(不置位报警标志，设备可以继续运行)
        /// </summary>
        Error,
        /// <summary>
        /// 报警(置位报警标志，设备不能继续运行)
        /// </summary>
        Alarm,
    }
    public enum eAlarmState
    {
        /// <summary>
        /// 报警中
        /// </summary>
        Active = 0,
        /// <summary>
        /// 无报警
        /// </summary>
        NoActive = 1
    }

    public enum eBaudRate
    { 
       t9600,
       t115200,
    }

    public enum eStopBit
    { 
        One,
        Two
    }
   /// <summary>
   ///奇偶校验
   /// </summary>
    public enum ePaity
    {
        None,
        ODD,
        EVEN
    }
    public enum eButtonEnableControl
    {
        Starting,
        Stopping,
    }
    public enum eDataBits
    { 
    D8
    
    }
    public enum eResult
    {
        OK,
        NG,
    }
    public enum eProcessMode
    {
        平面焊接,
        旋转焊接,
    }
    public enum eRippleEditProgramType
    {
        WAIT,
        SPT
    }
    /// <summary>通过:指定,文件,相机获取图片 </summary>
    public enum eImageSource
    {
        指定图像,
        文件目录,
        相机采集
    }
    public enum eRunProjectType
    {
        单次执行,
        循环执行,
        停止执行
    }
    public enum eShieldRegion
    {
        手绘区域,
        链接区域
    }
    public enum eSearchRegion
    {
        矩形1,
        矩形2,
        链接区域
    }
    public enum PointType
    { 
        Three,
        Nine,
        Fourteen,
    }
    public enum eEndMark
    {
        无,
        回车,
        换行,
    }

    public enum eViewMode
    {
        One,
        Two,
        Three,
        Four,
        Five,
        Six,
        Seven,
        Eight,
        Night
    }

    public enum eActiveState
    {
        /// <summary>
        /// 已激活
        /// </summary>
        Actived,
        /// <summary>
        /// 未激活
        /// </summary>
        NotActived,
        /// <summary>
        /// 试用
        /// </summary>
        Probation
    }
    public enum eHomeMode
    {
        负极限_原点,
        正极限_原点,
        原点,
        负极限_Index,
        正极限_Index,
        零位置预设, 
        负极限,
        正极限
    }
    public enum eDirection
    {
        /// <summary>
        /// 正向
        /// </summary>
        Positive = 1,
        /// <summary>
        /// 负向
        /// </summary>
        Negative = -1
    }
    public enum eDataType
    {
        Int,
        Double,
        String,
        Bool,
        IntAry,
        DoubleAry,
        StringAry,
        BoolAry,
        区域,
        ImageAry,

    }
    /// <summary>定位模板类型</summary>
    public enum eModelType
    {
        形状模板,
        灰度模板
    }
    /// <summary>对齐方式</summary>
    public enum eAlignMode
    {
        左边,
        中间,
        右边
    }
    public enum eFilterMode
    {
        无,
        中值滤波,
        均值滤波,
        高斯滤波,
        平滑滤波
    }
    /// <summary>取值模式</summary>
    public enum eValueMode
    {
        平均值,
        最大值,
        最小值,

    }
    /// <summary>
    /// 调整模式
    /// </summary>
    [Serializable]
    public enum eImageAdjust
    {
        None = 0,
        垂直镜像,
        水平镜像,
        顺时针90度,
        逆时针90度,
        旋转180度
    }

    /// <summary>对比极性类型</summary>
    public enum eCompType
    {
        [EnumDescription("use_polarity")]
        黑白对比一致,
        [EnumDescription("ignore_color_polarity")]
        黑白对比不一致,
        [EnumDescription("ignore_global_polarity")]
        黑白对比局部不一致
    }
    /// <summary>精细程度</summary>
    public enum eOptimization
    {
        [EnumDescription("point_reduction_high")]
        精细,
        [EnumDescription("point_reduction_medium")]
        正常,
        [EnumDescription("point_reduction_low")]
        粗略,
        [EnumDescription("auto")]
        自定义
    }
    /// <summary>测量点筛选</summary>
    public enum eMeasSelect
    {
        [EnumDescription("first")]
        第一点,
        [EnumDescription("last")]
        最末点,
        [EnumDescription("all")]
        所有点,
        [EnumDescription("strongest")]
        最强点
    }
    /// <summary>测量模式</summary>
    public enum eMeasMode
    {
        [EnumDescription("negative")]
        由白到黑,
        [EnumDescription("positive")]
        由黑到白,
        [EnumDescription("uniform")]
        规格一致,
        [EnumDescription("all")]
        所有信息
    }
    /// <summary>
    /// 触发模式
    /// </summary>
    [Serializable]
    public enum eTrigMode
    {
        内触发 =0,
        软触发,        
        上升沿,
        下降沿,        
    }
    /// <summary>
    /// PLC数据解析格式
    /// </summary>
    [Serializable]
    public enum PLCDataType
    {
        ABCD = 0,
        BADC,
        CDAB,
        DCBA,
    }
    /// <summary>
    /// 改变参数类型
    /// </summary>
    [Serializable]
    public enum ChangType
    {
        曝光,
        触发,
        宽度,
        高度,
        增益
    }


    /// <summary>
    /// 通信类型
    /// </summary>
    [Serializable]
    public enum PLCType
    {
        ModbusRtu = 0,
        ModbusTCP = 1,
    }
    /// <summary>
    /// PLC int数据宽度
    /// </summary>
    [Serializable]
    public enum PLCDataWriteReadTypeEnum
    {
        布尔 = 0,
        整型 = 1,
        浮点 = 2,
        字符串 = 3,
    }
    [Serializable]
    public enum PLCIntDataLengthEnum
    {
        _16位 = 0,
        _32位 = 1,
        _64位 = 2,
    }
    /// <summary>
    /// PLC double数据宽度
    /// </summary>
    [Serializable]
    public enum PLCDoubleDataLengthEnum
    {
        _32位 = 1,
        _64位 = 2,
    }
    /// <summary>
    /// PLC
    /// </summary>
    [Serializable]
    public enum PLCFunctionCodeEnum
    {
        _01 = 0,
        _02 = 1,
        _03 = 2,
        _04 = 3,
    }
}
