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
        public DrawShapeType ShapeType { get; set; }

        public HObject Hobject { get; set; }

        public HTuple[] HTuples { get; set; }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
