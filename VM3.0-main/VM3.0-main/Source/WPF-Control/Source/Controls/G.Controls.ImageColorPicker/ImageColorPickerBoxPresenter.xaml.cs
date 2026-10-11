using G.Mvvm.ViewModels.Base;
using System.Windows.Media;

namespace G.Controls.ImageColorPicker
{
    public class ImageColorPickerBoxPresenter:BindableBase
    {
        private ImageSource _imageSource;
        public ImageSource ImageSource
        {
            get { return _imageSource; }
            set
            {
                _imageSource = value;
                RaisePropertyChanged();
            }
        }

        private Color? _color;
        public Color? Color
        {
            get { return _color; }
            set
            {
                _color = value;
                RaisePropertyChanged();
            }
        }

    }
}
