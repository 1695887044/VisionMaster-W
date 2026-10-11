global using G.Extensions.Behvaiors.DataGrids;

namespace G.VisionMaster.ResultPresenter.ResultPresenters;

public interface IResultPresenterItem
{
    int Index { get; set; }
    string Name { get; set; }
}

public abstract class ResultPresenterItemBase : BindableBase, IResultPresenterItem
{
    [DataGridColumn("Auto")]
    [Display(Name = "序号", GroupName = "基础信息", Order = -2)]
    public int Index { get; set; }
    private string _name;
    [DataGridColumn("*")]
    [Display(Name = "结果信息", GroupName = "基础信息", Order = -1)]
    public string Name
    {
        get { return _name; }
        set
        {
            _name = value;
            RaisePropertyChanged();
        }
    }
}

public class ValueResultPresenterItem<T> : ResultPresenterItemBase
{
    public ValueResultPresenterItem()
    {

    }
    public ValueResultPresenterItem(T value)
    {
        this.Value = value;
    }
    private T _value;
    [DataGridColumn("*")]
    [Display(Name = "结果数值", GroupName = "基础信息")]
    public T Value
    {
        get { return _value; }
        set
        {
            _value = value;
            RaisePropertyChanged();
        }
    }
}

