using G.Common.Attributes;
using G.Controls.Diagram.Presenter.Extensions;
using G.Controls.Diagram.Presenter.NodeDatas.Base;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.Mvvm.Commands;
using G.Mvvm.ViewModels.Base;
using G.Services.Message;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace G.VisionMaster.NodeData.Base;

public interface IBase64MatchingNodeData : IDiagramableNodeData, IResultImageSourceNodeData
{
    string Base64String { get; set; }
}
[Icon(FontIcons.Crop)]
public abstract class Base64MatchingNodeData<T> : VisionNodeData<T>, IBase64MatchingNodeData where T : IDisposable
{
    protected Base64MatchingNodeData()
    {
        this.CropImagePropertyPresenter = new CropImagePropertyPresenter(this);
    }

    private int _matchingCountResult;
    [ReadOnly(true)]
    [Display(Name = "匹配数量", GroupName = VisionPropertyGroupNames.ResultParameters, Description = "结果参数，此结果可应用再条件分支等作为判断参数")]
    public int MatchingCountResult
    {
        get { return _matchingCountResult; }
        set
        {
            _matchingCountResult = value;
            RaisePropertyChanged();
        }
    }
    private double _confidence;
    [ReadOnly(true)]
    [Display(Name = "置信度", GroupName = VisionPropertyGroupNames.ResultParameters)]
    public double Confidence
    {
        get { return _confidence; }
        set
        {
            _confidence = value;
            RaisePropertyChanged();
        }
    }

    private string _base64String;
    public string Base64String
    {
        get { return _base64String; }
        set
        {
            _base64String = value;
            RaisePropertyChanged();
        }
    }

    [JsonIgnore]
    [Display(Name = "模板图片", GroupName = VisionPropertyGroupNames.RunParameters, Order = 1000)]
    public CropImagePropertyPresenter CropImagePropertyPresenter { get; private set; }
}

public class CropImagePropertyPresenter : BindableBase
{
    private readonly IBase64MatchingNodeData _nodeData;
    public CropImagePropertyPresenter(IBase64MatchingNodeData nodeData)
    {
        _nodeData = nodeData;
    }

    public string Base64String
    {
        get { return _nodeData.Base64String; }
        set
        {
            _nodeData.Base64String = value;
            RaisePropertyChanged();
        }
    }
    public RelayCommand CropCommand => new RelayCommand(async x =>
    {
        CropImagePresenter cropImagePresenter = new CropImagePresenter(this._nodeData);
        bool? r = await IocMessage.Dialog.Show(cropImagePresenter);
        if (r != true)
            return;
        if (this._nodeData.ResultImageSource is BitmapSource bitmapSource)
        {
            Int32Rect int32Rect = cropImagePresenter.GetInt32Rect();
            this.Base64String = bitmapSource.ToCroppedImageBase64String(int32Rect);
        };
    });

    public RelayCommand DeleteCommand => new RelayCommand(x =>
    {
        this.Base64String = null;
    });
}

public class CropImagePresenter : BindableBase
{
    private readonly IBase64MatchingNodeData _nodeData;
    public CropImagePresenter(IBase64MatchingNodeData nodeData)
    {
        _nodeData = nodeData;
        //nodeData.GetAllFromNodeDatas<ImageSrcNodeData>
        IResultImageSourceNodeData from = nodeData.GetFromNodeDatas().OfType<IResultImageSourceNodeData>()?.FirstOrDefault();
        this.ImageSource = from.ResultImageSource;
    }

    public ImageSource ImageSource { get; set; }
    public Rect Rect { get; set; } = Rect.Empty;

    public Int32Rect GetInt32Rect()
    {
        return new Int32Rect((int)this.Rect.X, (int)this.Rect.Y, (int)this.Rect.Width, (int)this.Rect.Height);
    }

}

