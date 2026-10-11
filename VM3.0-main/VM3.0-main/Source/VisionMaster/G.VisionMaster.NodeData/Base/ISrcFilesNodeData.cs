using G.Controls.Diagram.Presenter.Flowables;

namespace G.VisionMaster.NodeData.Base;

public interface ISrcFilesNodeData : IFlowableNodeData
{
    bool UseAllImage { get; set; }
    bool UseAutoSwitch { get; set; }

    string SrcFilePath { get; set; }
    ObservableCollection<string> SrcFilePaths { get; set; }

    bool IsValid(out string message);
}

public static class SrcFilesNodeDataExtension
{

    public static int MoveNext(this ISrcFilesNodeData srcFilesNodeData)
    {
        int index = srcFilesNodeData.SrcFilePaths.IndexOf(srcFilesNodeData.SrcFilePath);
        index = index < srcFilesNodeData.SrcFilePaths.Count - 1 ? index + 1 : 0;
        srcFilesNodeData.SrcFilePath = srcFilesNodeData.SrcFilePaths[index];
        return index;
    }
}

