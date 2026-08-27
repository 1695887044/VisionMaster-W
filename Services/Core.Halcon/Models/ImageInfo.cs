using HalconDotNet;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Core.Halcon.Models
{
    public class ImageInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private int width;

        public int Width
        {
            get { return width; }
            set { width = value; }
        }
        private int height;

        public int Height
        {
            get { return height; }
            set { height = value; }
        }
        private double pointX;

        public double PointX
        {
            get { return pointX; }
            set { pointX = value; }
        }
        private double pointY;

        public double PointY
        {
            get { return pointY; }
            set { pointY = value; }
        }
        private HTuple rgb1;

        public HTuple Rgb1
        {
            get { return rgb1; }
            set { rgb1 = value; }
        }
        private HTuple rgb2;

        public HTuple Rgb2
        {
            get { return rgb2; }
            set { rgb2 = value; }
        }
        private HTuple rgb3;

        public HTuple Rgb3
        {
            get { return rgb3; }
            set { rgb3 = value; }
        }
        private int channelCount;

        public int ChannelCount
        {
            get { return channelCount; }
            set { channelCount = value; }
        }
        private HImage image;

        public HImage Image
        {
            get { return image; }
            set
            {
                if (ReferenceEquals(image, value)) return;   // 可选：避免重复通知
                image = value;
                OnPropertyChanged();
            }
        }
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    }
}
