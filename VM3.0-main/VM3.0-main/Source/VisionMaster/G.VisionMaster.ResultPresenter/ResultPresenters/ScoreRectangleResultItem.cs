namespace G.VisionMaster.ResultPresenter.ResultPresenters;

public class ScoreRectangleResultItem : RectangleResultItem
{
    public ScoreRectangleResultItem(Rect rect, double score) : base(rect)
    {
        this.Score = score;
    }

    private double _score;
    [DataGridColumn("Auto")]
    [Display(Name = "置信度", GroupName = "基础信息")]
    public double Score
    {
        get { return _score; }
        set
        {
            _score = value;
            RaisePropertyChanged();
        }
    }
}

