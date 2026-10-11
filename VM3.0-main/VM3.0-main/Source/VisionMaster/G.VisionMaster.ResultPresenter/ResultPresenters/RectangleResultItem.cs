using System.ComponentModel;

namespace G.VisionMaster.ResultPresenter.ResultPresenters;

public interface IRectangleResultItem
{
    Rect Rect { get; }
}

public class RectangleResultItem : ResultPresenterItemBase, IRectangleResultItem
{
    private readonly Rect _rect;
    public RectangleResultItem(Rect rect)
    {
        this.X = (int)rect.GetCenter().X;
        this.Y = (int)rect.GetCenter().Y;
        this.Area = (int)(rect.Width * rect.Height);
        this.Width = (int)rect.Width;
        this.Height = (int)rect.Height;
        this._rect = rect;
    }
    [Browsable(false)]
    public Rect Rect => this._rect;
    private double _x;
    [DataGridColumn("Auto")]
    [Display(Name = "X中心", GroupName = "基础信息")]
    public double X
    {
        get { return _x; }
        set
        {
            _x = value;
            RaisePropertyChanged();
        }
    }

    private double _y;
    [DataGridColumn("Auto")]
    [Display(Name = "Y中心", GroupName = "基础信息")]
    public double Y
    {
        get { return _y; }
        set
        {
            _y = value;
            RaisePropertyChanged();
        }
    }

    private double _width;
    [DataGridColumn("Auto")]
    [Display(Name = "宽度", GroupName = "基础信息")]
    public double Width
    {
        get { return _width; }
        set
        {
            _width = value;
            RaisePropertyChanged();
        }
    }

    private double _height;
    [DataGridColumn("Auto")]
    [Display(Name = "高度", GroupName = "基础信息")]
    public double Height
    {
        get { return _height; }
        set
        {
            _height = value;
            RaisePropertyChanged();
        }
    }

    private double _area;
    [DataGridColumn("Auto")]
    [Display(Name = "面积", GroupName = "基础信息")]
    public double Area
    {
        get { return _area; }
        set
        {
            _area = value;
            RaisePropertyChanged();
        }
    }
}

