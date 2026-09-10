using HalconDotNet;
using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace Core.Halcon.Models
{
   
    public class DrawingObjectInfo : INotifyPropertyChanged, IDisposable
    {
        private string roiName;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string RoiName
        {
            get { return roiName; }
            set { roiName = value; OnPropertyChanged(); }
        }


        /// <summary>
        /// 参数化构造：从形状参数创建（恢复显示场景，可拖拽对象按需惰性创建）
        /// </summary>
        public DrawingObjectInfo(DrawShapeType shape, HTuple[] hTuple, string name = null)
        {
            this.ShapeType = shape;
            this.HTuples = hTuple;
            this.RoiName = name ?? shape.ToString();
        }

        public DrawShapeType ShapeType { get; set; }

        private HTuple[] hTuples;
        /// <summary>
        /// 形状参数（核心回传通道）：拖拽时控件回写、参数微调时 VM 写入，
        /// 每次变更都触发 INPC —— VM 据此同步 Params，控件据此应用画布句柄
        /// </summary>
        public HTuple[] HTuples
        {
            get { return hTuples; }
            set { hTuples = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 可拖拽交互对象（HDrawingObject）：选中编辑时由控件惰性创建并 Attach 到窗口
        /// </summary>
        public HDrawingObject DrawObject { get; set; }

        /// <summary>
        /// 持有已注册的拖拽回调委托：HALCON 原生层只存函数指针不持有托管引用，
        /// 委托被 GC 后回调会静默失效，这里保活
        /// </summary>
        public HDrawingObject.HDrawingObjectCallbackClass DragCallback { get; set; }

        /// <summary>同上（OnResize 用同类型委托，分开保活）</summary>
        public HDrawingObject.HDrawingObjectCallbackClass ResizeCallback { get; set; }

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

        private bool disposed;
        /// <summary>
        /// 释放原生 HALCON 资源：HDrawingObject 句柄 + 回调保活引用。
        /// 删除 ROI / 清空画布 / 控件卸载时必须调用，否则原生句柄泄漏
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (DrawObject != null)
            {
                try { DrawObject.Dispose(); }
                catch { /* 句柄可能已被原生层释放，忽略 */ }
                DrawObject = null;
            }
            DragCallback = null;
            ResizeCallback = null;
        }
    }
}
