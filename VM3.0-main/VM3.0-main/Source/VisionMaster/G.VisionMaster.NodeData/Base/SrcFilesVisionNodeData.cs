using G.Common.Attributes;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Controls.Diagram.Presenter.Flowables;
using G.Controls.Form.Attributes;
using G.Controls.Form.PropertyItem.TextPropertyItems;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.Mvvm.Commands;
using G.Services.AppPath;
using G.Services.Message;
using G.Services.Message.Dialog;
using G.Services.Message.IODialog;
using System.IO;
using System.Runtime.Serialization;

namespace G.VisionMaster.NodeData.Base;

[Icon(FontIcons.Photo2)]
public abstract class SrcFilesVisionNodeData<T> : ROINodeData<T>, ISrcFilesVisionNodeData<T> where T : IDisposable
{
    protected SrcFilesVisionNodeData()
    {
        this.UseStart = true;
    }

    private int _pixelWidth;
    [ReadOnly(true)]
    [Display(Name = "图像宽度", GroupName = VisionPropertyGroupNames.ResultParameters)]
    public int PixelWidth
    {
        get { return _pixelWidth; }
        set
        {
            _pixelWidth = value;
            RaisePropertyChanged();
        }
    }

    private int _pixelHeight;
    [ReadOnly(true)]
    [Display(Name = "图像高度", GroupName = VisionPropertyGroupNames.ResultParameters)]
    public int PixelHeight
    {
        get { return _pixelHeight; }
        set
        {
            _pixelHeight = value;
            RaisePropertyChanged();
        }
    }

    private int _imageColorType;
    [ReadOnly(true)]
    [Display(Name = "颜色类型", GroupName = VisionPropertyGroupNames.ResultParameters)]
    public int ImageColorType
    {
        get { return _imageColorType; }
        set
        {
            _imageColorType = value;
            RaisePropertyChanged();
        }
    }

    private bool _useAllImage = false;
    /// <summary>
    /// 获取或设置是否使用所有图像
    /// </summary>
    public bool UseAllImage
    {
        get { return _useAllImage; }
        set
        {
            _useAllImage = value;
            RaisePropertyChanged();
        }
    }

    private bool _useAutoSwitch = true;
    /// <summary>
    /// 获取或设置是否自动切换
    /// </summary>
    public bool UseAutoSwitch
    {
        get { return _useAutoSwitch; }
        set
        {
            _useAutoSwitch = value;
            RaisePropertyChanged();
        }
    }

    private string _srcFilePath;
    [Browsable(false)]
    [Display(Name = "当前文件", GroupName = VisionPropertyGroupNames.RunParameters)]
    [PropertyItem(typeof(OpenFileDialogPropertyItem))]
    public string SrcFilePath
    {
        get { return _srcFilePath; }
        set
        {
            _srcFilePath = value;
            RaisePropertyChanged();
        }
    }

    private ObservableCollection<string> _srcFilePaths = new ObservableCollection<string>();
    public ObservableCollection<string> SrcFilePaths
    {
        get { return _srcFilePaths; }
        set
        {
            _srcFilePaths = value;
            this.SrcFilePath = value?.FirstOrDefault();
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// 清除图像数据命令
    /// </summary>
    public RelayCommand ClearImageDatasCommand => new RelayCommand(async x =>
    {
        await IocMessage.Dialog.ShowDeleteAllDialog(x =>
        {
            this.SrcFilePaths.Clear();
        });
    }, x => this.SrcFilePaths != null && this.SrcFilePaths.Count > 0);

    /// <summary>
    /// 添加图像数据集合命令
    /// </summary>
    public RelayCommand AddImageDatasCommand => new RelayCommand(x =>
    {
        this.AddFiles();
    }, x => this != null);

    protected virtual void AddFiles()
    {
        IocMessage.IOFolderDialog.ShowOpenFolderAction(x =>
        {
            string selectedFolderPath = x;
            IEnumerable<string> images = selectedFolderPath.GetAllImages();
            this.SrcFilePaths.AddRange(images);
            if (this.SrcFilePaths.Count == 0)
                this.SrcFilePath = this.SrcFilePaths.FirstOrDefault();
        });
    }

    /// <summary>
    /// 添加图像数据命令
    /// </summary>
    public RelayCommand AddImageDataCommand => new RelayCommand(x =>
    {
        this.AddFile();
    });

    protected virtual void AddFile()
    {
        IocMessage.IOFileDialog.ShowOpenImageFiles(x =>
        {
            foreach (string item in x)
            {
                this.SrcFilePaths.Add(item);
            }
            if (this.SrcFilePaths.Count == 0)
                this.SrcFilePath = this.SrcFilePaths.FirstOrDefault();
        });
    }

    /// <summary>
    /// 删除图像数据命令
    /// </summary>
    public RelayCommand DeleteImageDataCommand => new RelayCommand(async x =>
    {
        await IocMessage.Dialog.ShowDeleteDialog(x =>
        {
            int index = this.SrcFilePaths.IndexOf(this.SrcFilePath);
            this.SrcFilePaths.Remove(this.SrcFilePath);
            string find = this.SrcFilePaths.ElementAtOrDefault(index);
            this.SrcFilePath = find == null ? this.SrcFilePaths.FirstOrDefault() : find;
        });

    });

    public override void LoadDefault()
    {
        base.LoadDefault();
        IEnumerable<string> imagePaths = AppPaths.Instance.Assets.GetAllImages();
        this.SrcFilePaths.Clear();
        foreach (string imagePath in imagePaths)
        {
            string relatvePath = Path.GetRelativePath(AppDomain.CurrentDomain.BaseDirectory, imagePath);
            this.SrcFilePaths.Add(relatvePath);
        }
        this.SrcFilePath = this.SrcFilePaths?.FirstOrDefault();
    }

    protected override async Task<IFlowableResult> BeforeInvokeAsync(IFlowableLinkData previors, IFlowableDiagramData diagram)
    {
        if (File.Exists(this.SrcFilePath) == false)
        {
            bool? r = await IocMessage.Form?.ShowEdit(this, x => x.Title = $"{this.Name}:请先选择文件", null, x =>
            {
                x.UsePropertyNames = nameof(SrcFilePath);
            });
            if (r != true)
                return this.Error("未设置源文件地址");
        }
        return await base.BeforeInvokeAsync(previors, diagram);
    }

    protected override void OnDeserializing(StreamingContext context)
    {
        base.OnDeserializing(context);
        this.SrcFilePaths.Clear();
        this.SrcFilePath = null;
    }

    public virtual bool IsValid(out string message)
    {
        if (this?.SrcFilePaths == null || this.SrcFilePaths.Count == 0)
        {
            message = "请选择数据源中的图片";
            //await IocMessage.ShowDialogMessage("请选择数据源中的图片");
            return false;
        }
        if (this.SrcFilePath == null)
        {
            this.SrcFilePath = this.SrcFilePaths?.FirstOrDefault();
        }
        this.SrcFilePath = this.SrcFilePath;
        message = null;
        return this.SrcFilePath != null;
    }
}

