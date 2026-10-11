using G.Themes;
using System.Windows;

namespace G.Controls.Diagram.Presenters.Workflow.NodeDatas;

public abstract class LaneNodeDataBase : WorkflowNodeBase
{
    public LaneNodeDataBase()
    {
        this.Text = "标题";
        this.Fill = Brushes.Transparent;
        this.HeaderBrush = Brushes.Transparent;
    }

    private double _headerHeight = (double)Application.Current.FindResource(LayoutKeys.RowHeight);
    /// <summary> 说明  </summary>
    public double HeaderHeight
    {
        get { return _headerHeight; }
        set
        {
            _headerHeight = value;
            RaisePropertyChanged();
        }
    }

    private Brush _headerBrush;
    /// <summary> 说明  </summary>
    public Brush HeaderBrush
    {
        get { return _headerBrush; }
        set
        {
            _headerBrush = value;
            RaisePropertyChanged();
        }
    }

    private CornerRadius _cornerRadius = new CornerRadius(2);
    /// <summary> 说明  </summary>
    public new CornerRadius CornerRadius
    {
        get { return _cornerRadius; }
        set
        {
            _cornerRadius = value;
            RaisePropertyChanged();
        }
    }

}
