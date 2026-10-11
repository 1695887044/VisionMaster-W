global using G.Controls.Diagram.Presenter.Flowables;
global using G.Controls.Form.Attributes;
global using G.Services.Message;
using System.Threading.Tasks;
namespace G.VisionMaster.OpenCV.Base;

[Icon(FontIcons.BrowsePhotos)]
public abstract class OpenCVFileTemplateMatchingNodeDataBase : OpenCVDetectorNodeDataBase
{
    private string _templateFilePath;
    [Required]
    [Display(Name = "模板图片", GroupName = VisionPropertyGroupNames.RunParameters)]
    [PropertyItem(typeof(OpenFileDialogPropertyItem))]
    public string TemplateFilePath
    {
        get { return _templateFilePath; }
        set
        {
            _templateFilePath = value;
            RaisePropertyChanged();
        }
    }

    protected override async Task<IFlowableResult> BeforeInvokeAsync(IFlowableLinkData previors, IFlowableDiagramData diagram)
    {
        if (File.Exists(this.TemplateFilePath) == false)
        {
            bool? r = await IocMessage.Form?.ShowEdit(this, x => x.Title = $"{this.Name}:请先选择文件", null, x =>
            {
                x.UsePropertyNames = nameof(TemplateFilePath);
            });
            if (r != true)
                return this.Error("未设置模板图片地址");
        }
        return await base.BeforeInvokeAsync(previors, diagram);
    }
}

