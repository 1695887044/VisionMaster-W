using G.Controls.Diagram.Presenter.Extensions;
using G.Extensions.Mvvm.ViewModels.Base;
using G.Mvvm.Commands;
using G.VisionMaster.NodeData.Base;
using System.Threading.Tasks;

namespace G.VisionMaster.DiagramData;
public class RunDiagramDataPresenter : DisplayBindableBase
{
    public RunDiagramDataPresenter(IVisionDiagramData visionDiagramData)
    {
        this._visionDiagramData = visionDiagramData;
    }

    private IVisionDiagramData _visionDiagramData;
    public IVisionDiagramData VisionDiagramData
    {
        get { return _visionDiagramData; }
        set
        {
            _visionDiagramData = value;
            RaisePropertyChanged();
        }
    }

    private bool _stopping;
    public void Stop()
    {
        this._stopping = true;
        this.VisionDiagramData.Stop();
    }

    public RelayCommand StartAllCommand => new RelayCommand(async x =>
    {
        IFlowableNodeData start = await this.VisionDiagramData.TryGetStartNodeData<IFlowableNodeData>();
        if (start == null)
            return;

        if (start is ISrcFilesNodeData visionImageSource)
        {
            foreach (var filePath in visionImageSource.SrcFilePaths)
            {
                if (this._stopping)
                    break;
                visionImageSource.SrcFilePath = filePath;
                await this.StartOne();
                await Task.Delay(500);
            }
        }
        else
        {
            await this.VisionDiagramData.Start();
        }
        this._stopping = false;
    }, x => this.VisionDiagramData.State.CanStart() && this._stopping == false);

    public RelayCommand StartCommand => new RelayCommand(async x =>
    {
        await this.StartOne();
        this._stopping = false;
    }, x => this.VisionDiagramData.State.CanStart() && this._stopping == false);


    public RelayCommand StopCommand => new RelayCommand(x =>
    {
        this.Stop();
    }, x => this.VisionDiagramData.State.CanStop() && this._stopping == false);
    private async Task StartOne()
    {
        IFlowableNodeData start = await this.VisionDiagramData.TryGetStartNodeData<IFlowableNodeData>();
        if (start == null)
            return;
        if (start is ISrcFilesNodeData visionImageSource)
        {
            bool cache = visionImageSource.UseAllImage;
            visionImageSource.UseAllImage = false;
            await this.VisionDiagramData.Start();
            visionImageSource.UseAllImage = cache;
        }
        else
        {
            await this.VisionDiagramData.Start();
        }
    }
}
