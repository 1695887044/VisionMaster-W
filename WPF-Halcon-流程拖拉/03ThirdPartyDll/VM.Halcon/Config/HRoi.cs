using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace VM.Halcon.Config
{
    /// <summary>
    /// 用于展示效果的HObject
    /// </summary>
    [Serializable]
    public class HRoi
    {
        /// <summary>模块编号</summary>
        public int ModuleEncode { get; set; }
        /// <summary>模块名</summary>
        public string ModuleName { get; set; }
        /// <summary>单元描述</summary>
        public string Remarks { get; set; }
        /// <summary>轮廓分类</summary>
        public HRoiType roiType { get; set; }
        /// <summary>画笔颜色</summary>
        public string drawColor { get; set; }
        /// <summary>测量roi</summary>
        public HObject hobject { get; set; }
        /// <summary>循环+</summary>
        public bool fors { get; set; }
        /// <summary>Fill显示</summary>
        public bool IsFillDisp { get; set; }
        /// <summary>测量roi</summary>
        public HRoi() { }
        public HRoi(int moduleEncode, string moduleName, string remarks, HRoiType _roiType, string _drawColor, HObject _hobject, bool isFillDisp = false)
        {
            ModuleEncode = moduleEncode;
            ModuleName = moduleName;
            Remarks = remarks;
            roiType = _roiType;
            drawColor = _drawColor;
            hobject = _hobject;
            IsFillDisp = isFillDisp;
        }
        public HRoi(int moduleEncode, string moduleName, string remarks, HRoiType _roiType, string _drawColor, HObject _hobject, bool _for, bool isFillDisp = false)
        {
            ModuleEncode = moduleEncode;
            ModuleName = moduleName;
            Remarks = remarks;
            roiType = _roiType;
            drawColor = _drawColor;
            hobject = _hobject;
            fors = _for;
            IsFillDisp = isFillDisp;
        }
        public HRoi(int moduleEncode, string moduleName, string remarks, HRoiType _roiType, string _drawColor, HObject[] _hobject)
        {
            int i = 0;
            ModuleEncode = moduleEncode;
            ModuleName = moduleName;
            Remarks = remarks;
            roiType = _roiType;
            drawColor = _drawColor;
            hobject = _hobject[i];
        }
        [OnSerializing]
        internal void OnSerializingMethod(StreamingContext context)
        {
            if (hobject != null && !hobject.IsInitialized())//修复为null 错误 magical 20171103
            {
                hobject = null;
            }
        }
    }
    [Serializable]
    public class HText : HRoi
    {
        /// <summary>文字</summary>
        public string text { get; set; }
        /// <summary>字体</summary>
        public string font = "mono";
        /// <summary>显示的位置-X</summary>
        public double row { get; set; }
        /// <summary>显示的位置-Y</summary>
        public double col { get; set; }
        /// <summary>大小</summary>
        public int size { get; set; }
        /// <summary>角度</summary>
        public int phi { get; set; }
        public HText(string _drawColor, string _text, double _row, double _col, int _size, string _font="mono")
        {
            drawColor = _drawColor;
            text = _text;
            font = _font;
            row = _row;
            col = _col;
            size = _size;
        }
        /// <summary>
        /// 测量roi
        /// </summary>
        /// <param name="_CellID">单元id</param>
        /// <param name="_CellType">单元类型</param>
        /// <param name="_CellNote">单元描述</param>
        /// <param name="_roiType">ROI类型</param>
        /// <param name="_drawColor">画笔颜色</param>
        /// <param name="_text">文本内容</param>
        /// <param name="_font">字体</param>
        /// <param name="_row">行</param>
        /// <param name="_col">列</param>
        /// <param name="_size">大小</param>
        public HText(int _CellID, string _CellType, string _CellNote, HRoiType _roiType, string _drawColor, string _text, double _row, double _col, int _size,string _font= "mono")
        {
            ModuleEncode = _CellID;
            ModuleName = _CellType;
            Remarks = _CellNote;
            roiType = _roiType;
            drawColor = _drawColor;
            text = _text;
            font = _font;
            row = _row;
            col = _col;
            size = _size;
        }
        /// <summary>
        /// 测量roi
        /// </summary>
        /// <param name="_CellID">单元id</param>
        /// <param name="_CellType">单元类型</param>
        /// <param name="_CellNote">单元描述</param>
        /// <param name="_roiType">ROI类型</param>
        /// <param name="_drawColor">画笔颜色</param>
        /// <param name="_text">文本内容</param>
        /// <param name="_font">字体</param>
        /// <param name="_row">行</param>
        /// <param name="_col">列</param>
        /// <param name="_size">大小</param>
        /// <param name="_for">循环+</param>
        public HText(int _CellID, string _CellType, string _CellNote, HRoiType _roiType, string _drawColor, string _text, string _font, double _row, double _col, int _size, HObject _hobject, bool _for)
        {
            ModuleEncode = _CellID;
            ModuleName = _CellType;
            Remarks = _CellNote;
            roiType = _roiType;
            drawColor = _drawColor;
            text = _text;
            font = _font;
            row = _row;
            col = _col;
            size = _size;
            fors = _for;
        }
    }
    /// <summary>
    /// 轮廓分类
    /// </summary>
    public enum HRoiType
    {
        检测点,
        检测X点,
        检测Y点,
        检测点P1,
        检测点P2,
        检测范围,
        检测中心,
        检测结果,
        搜索范围,
        搜索方向,
        屏蔽范围,
        文字显示,
        参考坐标,
        测量直线1,
        测量直线2,
    }
}
