using HalconDotNet;
using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace Core.Halcon.Models
{
   
    public class DrawingObjectInfo: INotifyPropertyChanged
    {
        private string roiName;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string RoiName
        {
            get { return roiName; }
            set { roiName = value; OnPropertyChanged(); }
        }


        public DrawingObjectInfo(DrawShapeType shape, HObject obj, HTuple[] hTuple)
        {
            this.ShapeType = shape;
            this.Hobject = obj;
            this.HTuples = hTuple;
            this.RoiName = shape.ToString();
        }

        /// <summary>
        /// 参数化构造：从形状参数创建（恢复显示场景，HObject 与可拖拽对象按需惰性创建）
        /// </summary>
        public DrawingObjectInfo(DrawShapeType shape, HTuple[] hTuple, string name = null)
        {
            this.ShapeType = shape;
            this.Hobject = null;
            this.HTuples = hTuple;
            this.RoiName = name ?? shape.ToString();
        }

        public DrawShapeType ShapeType { get; set; }

        public HObject Hobject { get; set; }

        public HTuple[] HTuples { get; set; }

        /// <summary>
        /// 可拖拽交互对象（HDrawingObject）：选中编辑时由控件惰性创建并 Attach 到窗口
        /// </summary>
        public HDrawingObject DrawObject { get; set; }

        private bool isSelected;
        /// <summary>
        /// 是否为当前编辑中的 ROI（轮廓高亮显示）
        /// </summary>
        public bool IsSelected
        {
            get { return isSelected; }
            set { isSelected = value; OnPropertyChanged(); }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
