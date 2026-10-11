using G.VisionMaster.NodeData.PropertyPresenters;

namespace G.VisionMaster.NodeData.Base;

public abstract class ShowPropertyNodeDataBase : VisionNodeDataBase
{
    public override object GetPropertyPresenter()
    {
        return new InvokeCommandsPropertyPresenter(this);
    }

}

