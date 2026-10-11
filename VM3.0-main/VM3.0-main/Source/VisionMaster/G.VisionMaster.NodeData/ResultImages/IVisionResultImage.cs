namespace G.VisionMaster.NodeData.ResultImages;

public interface IVisionResultImage<T>
{
    string Name { get; set; }
    T Image { get; set; }
}

