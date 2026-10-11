global using G.Controls.Diagram.Presenter.Flowables;
using G.Controls.Diagram.Presenter.NodeDatas.Base;
using G.Mvvm.ViewModels.Base;
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;

namespace G.VisionMaster.DiagramData;
public class VisionMessage : BindableBase, IVisionMessage
{
    private int _Index;
    [Display(Name = "执行序号")]
    public int Index
    {
        get { return _Index; }
        set
        {
            _Index = value;
            RaisePropertyChanged();
        }
    }

    private TimeSpan _TimeSpan;
    [Display(Name = "时间")]
    public TimeSpan TimeSpan
    {
        get { return _TimeSpan; }
        set
        {
            _TimeSpan = value;
            RaisePropertyChanged();
        }
    }

    private string _Type;
    [Display(Name = "模块")]
    public string Type
    {
        get { return _Type; }
        set
        {
            _Type = value;
            RaisePropertyChanged();
        }
    }

    private string _Message;
    [Display(Name = "数据")]
    public string Message
    {
        get { return _Message; }
        set
        {
            _Message = value;
            RaisePropertyChanged();
        }
    }

    private FlowableState _State;
    [Display(Name = "状态")]
    public FlowableState State
    {
        get { return _State; }
        set
        {
            _State = value;
            RaisePropertyChanged();
        }
    }

    private ImageSource _ResultImageSource;
    public ImageSource ResultImageSource
    {
        get { return _ResultImageSource; }
        set
        {
            _ResultImageSource = value;
            RaisePropertyChanged();
        }
    }

    private string _SrcFilePath;
    public string SrcFilePath
    {
        get { return _SrcFilePath; }
        set
        {
            _SrcFilePath = value;
            RaisePropertyChanged();
        }
    }


    private IResultPresenterNodeData _ResultNodeData;
    public IResultPresenterNodeData ResultNodeData
    {
        get { return _ResultNodeData; }
        set
        {
            _ResultNodeData = value;
            RaisePropertyChanged();
        }
    }

}
