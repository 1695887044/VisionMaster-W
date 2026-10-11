using G.Common.Interfaces;
using G.Controls.Diagram.Datas;
using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Controls.Diagram.Presenter.Extensions;
using G.Controls.Diagram.Presenter.NodeDatas.Base;
using G.Controls.ZoomBox;
using G.Extensions.Common;
using G.Mvvm.Commands;
using G.Services.Message;
using G.Services.Message.Dialog;
using G.VisionMaster.NodeData;
using G.VisionMaster.NodeData.Base;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace G.VisionMaster.DiagramData;

[Display(Name = "图片处理", GroupName = "机器视觉", Order = 0)]
public class VisionDiagramDataBase : NodeDataGroupsDiagramDataBase, IVisionDiagramData
{
    [JsonIgnore]
    [Display(Name = "流程标题", GroupName = VisionPropertyGroupNames.DisplayParameters, Order = -99)]
    public string Title
    {
        get { return base.Name; }
        set
        {
            base.Name = value;
            RaisePropertyChanged();
        }
    }

    private ImageSource _resultImageSource;
    /// <summary>
    /// 获取或设置结果图像源
    /// </summary>
    [JsonIgnore]
    public ImageSource ResultImageSource
    {
        get { return _resultImageSource; }
        set
        {
            _resultImageSource = value;
            RaisePropertyChanged();
        }
    }

    private IResultPresenterNodeData _resultNodeData;
    [JsonIgnore]
    public IResultPresenterNodeData ResultNodeData
    {
        get { return _resultNodeData; }
        set
        {
            _resultNodeData = value;
            RaisePropertyChanged();
        }
    }

    private ISrcFilesNodeData _srcImageNodeData;
    [JsonIgnore]
    public ISrcFilesNodeData SrcImageNodeData
    {
        get { return _srcImageNodeData; }
        set
        {
            _srcImageNodeData = value;
            RaisePropertyChanged();
        }
    }

    private string _resultType;

    /// <summary>
    /// 获取或设置结果类型
    /// </summary>
    [JsonIgnore]
    public string ResultType
    {
        get { return _resultType; }
        set
        {
            _resultType = value;
            RaisePropertyChanged();
        }
    }

    private ObservableCollection<IVisionMessage> _messages = new ObservableCollection<IVisionMessage>();

    /// <summary>
    /// 获取或设置消息集合
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<IVisionMessage> Messages
    {
        get { return _messages; }
        set
        {
            _messages = value;
            RaisePropertyChanged();
        }
    }

    private IVisionMessage _currentMessage;

    /// <summary>
    /// 获取或设置当前消息
    /// </summary>
    [JsonIgnore]
    public IVisionMessage CurrentMessage
    {
        get { return _currentMessage; }
        set
        {
            _currentMessage = value;
            RaisePropertyChanged();
        }
    }

    private bool? _runModeResult;
    public bool? RunModeResult
    {
        get { return _runModeResult; }
        set
        {
            _runModeResult = value;
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// 开始处理
    /// </summary>
    /// <returns>处理结果</returns>
    public override async Task<bool?> Start()
    {
        this.Messages.Clear();
        //this.SelectedImageTabIndex = 1;
        this.CurrentMessage = null;
        bool? result = true;
        this.RunModeResult = null;
        IFlowableNodeData imageSourceNode = await this.GetStartNodeData();
        if (imageSourceNode == null)
            return false;
        if (imageSourceNode is ISrcFilesNodeData visionImageSource)
        {
            this.SrcImageNodeData = visionImageSource;
            if (visionImageSource.UseAllImage)
            {
                result = await this.InvokeState(async () =>
                {
                    foreach (string item in this.SrcImageNodeData.SrcFilePaths)
                    {
                        if (this.State == DiagramFlowableState.Canceling)
                            return null;
                        this.RunModeResult = null;
                        if (visionImageSource.UseAllImage == false)
                            break;
                        this.ResultImageSource = item.ToImageSource();
                        if (visionImageSource.UseAutoSwitch)
                            this.SrcImageNodeData.SrcFilePath = item;
                        this.Wait();
                        visionImageSource.SrcFilePath = item;
                        bool? r = await visionImageSource.Start(this);
                        this.RunModeResult = r == true;
                        await Task.Delay(1000);
                        if (r == null)
                            return null;
                        if (r == false)
                            return false;
                        IVisionMessage message = this.Messages.LastOrDefault();
                        ImageSource rimage = message?.ResultImageSource;
                        //item.ResultImageSource = rimage;
                        this.ResultNodeData = message?.ResultNodeData;
                    }
                    return true;
                });
            }
            else
            {
                if (!visionImageSource.IsValid(out string vmessage))
                {
                    await IocMessage.ShowDialogMessage(vmessage);
                    return null;
                }
                this.ResultImageSource = visionImageSource.SrcFilePath.ToImageSource();
                result = await this.InvokeState(async () =>
                {
                    return await visionImageSource.Start(this);
                });
                this.RunModeResult = result == true;
                IVisionMessage message = this.Messages.LastOrDefault();
                ImageSource rimage = message?.ResultImageSource;
                //this.SelectedImageData.ResultImageSource = rimage;
                this.ResultNodeData = message?.ResultNodeData;
            }
        }
        else
        {
            result = await base.Start();
        }
        this.LogCurrentMessage();
        return result;
    }

    /// <summary>
    /// 记录当前消息
    /// </summary>
    public void LogCurrentMessage()
    {
        long totalTimeSpan = this.Messages.Sum(x => x.TimeSpan.Ticks);
        ImageSource rimage = this.Messages.LastOrDefault()?.ResultImageSource;
        this.CurrentMessage = new VisionMessage()
        {
            TimeSpan = TimeSpan.FromTicks(totalTimeSpan),
            Message = this.Message,
            ResultImageSource = rimage,
        };
    }

    /// <summary>
    /// 图像文件选择更改命令
    /// </summary>
    [JsonIgnore]
    public RelayCommand ImageFileSelectionChangedCommand => new RelayCommand(l =>
    {
        if (this.SrcImageNodeData?.SrcFilePath == null)
            return;
        if (this.State == DiagramFlowableState.Running)
            return;
        this.ResultImageSource = this.SrcImageNodeData.SrcFilePath.ToImageSource();
        this.ResultType = $"图像源<{this.SrcImageNodeData?.SrcFilePath.GetFileNameWithoutExtension()}>";
    });

    /// <summary>
    /// 选中部分更改时调用
    /// </summary>
    protected override void OnSelectedPartChanged()
    {
        base.OnSelectedPartChanged();
        if (this.State == DiagramFlowableState.Running)
            return;
        if (this.SelectedPartData is INodeData data)
        {
            if (data is ISrcFilesNodeData srcImageNodeData)
                this.SrcImageNodeData = srcImageNodeData;
            else
            {
                //  ToDo：待完善，查找选择的数据源
                this.SrcImageNodeData = data.GetSelectedFromNodeDatas(this).OfType<ISrcFilesNodeData>().FirstOrDefault();
            }
            if (data is IVisionNodeData openCVNodeData)
            {
                this.ResultImageSource = openCVNodeData.ResultImageSource;
                this.ResultNodeData = openCVNodeData;
            }

            if (data is ITextable textable)
                this.ResultType = $"输出结果<{textable.Text}>";
            //if (data is IFilePathable filePathable)
            //{
            //    IImageData find = this.ImageDatas.FirstOrDefault(x => x.FilePath == filePathable.SrcFilePath);
            //    if (find != null)
            //        this.SelectedImageData = find;
            //}
            //this.SelectedImageTabIndex = 1;
        }
        else
        {
            this.SrcImageNodeData = null;
            this.ResultImageSource = null;
            this.ResultNodeData = null;
        }
    }

    private object _selectedImageData;
    public object SelectedImageData
    {
        get { return _selectedImageData; }
        set
        {
            _selectedImageData = value;
            RaisePropertyChanged();
        }
    }

    private object _selectedPart;
    public object SelectedPart
    {
        get { return _selectedPart; }
        set
        {
            _selectedPart = value;
            RaisePropertyChanged();
        }
    }

    private int _selectedImageTabIndex;

    /// <summary>
    /// 获取或设置选中的图像选项卡索引
    /// </summary>
    [JsonIgnore]
    public int SelectedImageTabIndex
    {
        get { return _selectedImageTabIndex; }
        set
        {
            _selectedImageTabIndex = value;
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// 部分调用后执行
    /// </summary>
    /// <param name="partData">部分数据</param>
    public override void OnInvokedPart(IPartData partData)
    {
        IVisionNodeData openCVNodeData = partData as IVisionNodeData;
        if (openCVNodeData == null)
            return;
        if (!openCVNodeData.UseInvokedPart)
            return;
        base.OnInvokedPart(partData);
        //视频部分异步触发需要用主线程生成ImageSource对象
        Application.Current.Dispatcher.Invoke(() =>
            {
                this.ResultImageSource = openCVNodeData.ResultImageSource;
                this.ResultNodeData = openCVNodeData;

                //if (openCVNodeData is IVideoCaptureNodeData videoCaptureNodeData)
                //{
                var find = this.Messages.Where(x => x.ResultNodeData == openCVNodeData).FirstOrDefault();
                if (find != null)
                {
                    find.ResultImageSource = openCVNodeData.ResultImageSource;
                    find.TimeSpan = openCVNodeData.TimeSpan;
                    find.Message = openCVNodeData.Message;
                    find.State = openCVNodeData.State;
                    this.LogCurrentMessage();
                    return;
                }
                //}

                VisionMessage message = new VisionMessage()
                {
                    Index = this.Messages.Count + 1,
                    Message = openCVNodeData.Message,
                    State = openCVNodeData.State,
                    TimeSpan = openCVNodeData.TimeSpan,
                    SrcFilePath = this.GetStartNodeDatas().OfType<ISrcFilesNodeData>().FirstOrDefault()?.SrcFilePath,
                    ResultImageSource = openCVNodeData.ResultImageSource,
                    ResultNodeData = openCVNodeData
                };
                if (openCVNodeData is INameable nameable)
                    message.Type = nameable.Name;
                if (openCVNodeData is ITextable textable)
                {
                    this.ResultType = $"输出结果<{textable.Text}>";
                    message.Type = textable.Text;
                }
                this.Messages.Add(message);
                this.LogCurrentMessage();
            });
    }

    /// <summary>
    /// 选中消息更改命令
    /// </summary>
    public RelayCommand SelectedMessageChangedCommand => new RelayCommand(x =>
    {
        if (x is SelectionChangedEventArgs t)
        {
            if (t.AddedItems.OfType<IVisionMessage>().FirstOrDefault() is IVisionMessage message)
            {
                this.ResultType = $"输出结果<{message.Type}>";
                //this.SelectedImageTabIndex = 1;
                this.SetResultNodeData(message);
            }
        }
    });
        public RelayCommand ImageSourceUpdatedCommand => new RelayCommand(x =>
    {
        if (this.State.CanStop())
            return;
        if (x is Zoombox zoombox)
            zoombox.FitToBounds();
    });

    private void SetResultNodeData(IVisionMessage message)
    {
        this.ResultImageSource = message.ResultImageSource;
        this.ResultNodeData = message.ResultNodeData;
    }

    /// <summary>
    /// 序列化时调用
    /// </summary>
    protected override void OnSerializing()
    {
        base.OnSerializing();
    }

    /// <summary>
    /// 反序列化后调用
    /// </summary>
    protected override void OnDeserialized()
    {
        base.OnDeserialized();
        //  ToDo：这个地方应该可以去掉，已经保存了序列化操作
        //this.SrcImageNodeData.SrcFilePath = this.SrcImageNodeData.SrcFilePaths.FirstOrDefault();
    }
}
