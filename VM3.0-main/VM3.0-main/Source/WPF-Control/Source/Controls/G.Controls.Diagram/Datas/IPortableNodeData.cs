namespace G.Controls.Diagram.Datas;

public interface IPortableNodeData : ILinkDataCreator
{
    public List<IPortData> PortDatas { get; set; }
}
