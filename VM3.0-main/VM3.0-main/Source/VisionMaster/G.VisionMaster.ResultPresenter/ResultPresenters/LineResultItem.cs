using System.ComponentModel;

namespace G.VisionMaster.ResultPresenter.ResultPresenters;

public struct VisionLine
{
    public Point Start { get; set; }
    public Point End { get; set; }
}
public class LineResultItem : ResultPresenterItemBase, IRectangleResultItem
{
    private readonly Rect _rect;
    public LineResultItem(Point start, Point end)
    {
        this._start = new Point((int)start.X, (int)start.Y);
        this._end = new Point((int)start.X, (int)start.Y);
        this._rect = new Rect(start, end);
    }
    [Browsable(false)]
    public Rect Rect => this._rect;

    private Point _start;
    [DataGridColumn("*", PropertyPath = "{0:F2}")]
    [Display(Name = "起点", GroupName = "基础信息")]
    public Point Start
    {
        get { return _start; }
        set
        {
            _start = value;
            RaisePropertyChanged();
        }
    }

    private Point _end;
    [DataGridColumn("*", PropertyPath = "{0:F2}")]
    [Display(Name = "终点", GroupName = "基础信息")]
    public Point End
    {
        get { return _end; }
        set
        {
            _end = value;
            RaisePropertyChanged();
        }
    }
}

