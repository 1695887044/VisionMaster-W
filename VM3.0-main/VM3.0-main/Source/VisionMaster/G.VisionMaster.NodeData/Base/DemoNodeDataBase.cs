using G.Controls.Diagram.Presenter.NodeDatas.Base;
using G.VisionMaster.NodeData.HelpPresenters;
using G.VisionMaster.ResultPresenter;
using G.VisionMaster.ResultPresenter.ResultPresenters;

namespace G.VisionMaster.NodeData.Base;

public abstract class DemoNodeDataBase : HelpNodeDataBase
{
    #region - 基本参数添加方式示例 -

    private string _demoBaseParamter1;
    [Display(Name = "示例：基本参数", GroupName = VisionPropertyGroupNames.BaseParameters, Description = "用来演示如何增加基本参数", Order = int.MaxValue)]
    public string DemoBaseParamter1
    {
        get { return _demoBaseParamter1; }
        set
        {
            _demoBaseParamter1 = value;
            RaisePropertyChanged();
        }
    }
    #endregion

    #region - 运行参数添加方式示例 -

    private string _demoRunParamter1;
    [Display(Name = "示例：运行参数", GroupName = VisionPropertyGroupNames.RunParameters, Description = "用来演示如何增加结果参数", Order = int.MaxValue)]
    public string DemoRunParamter1
    {
        get { return _demoRunParamter1; }
        set
        {
            _demoRunParamter1 = value;
            RaisePropertyChanged();
        }
    }
    #endregion

    #region - 结果参数添加方式示例 -

    private string _demoResult1;
    [ReadOnly(true)]
    [Display(Name = "示例：结果参数", GroupName = VisionPropertyGroupNames.ResultParameters, Description = "用来演示如何增加结果参数，此结果可应用再条件分支等作为判断参数", Order = int.MaxValue)]
    public string DemoResult1
    {
        get { return _demoResult1; }
        set
        {
            _demoResult1 = value;
            RaisePropertyChanged();
        }
    }
    #endregion

    #region -  帮助页面添加方式示例 -
    public override IHelpPresenter CreateHelpPresenter()
    {
        return base.CreateHelpPresenter();
    }
    #endregion

    #region - 当前结果添加方式示例 -
    public override IResultPresenter CreateResultPresenter()
    {
        return Enumerable.Range(1, 3).ToDataGridValueResultPresenter(x => Random.NextDouble().ToString(), x => "示例：输出结果" + x);
    }
    #endregion
}

