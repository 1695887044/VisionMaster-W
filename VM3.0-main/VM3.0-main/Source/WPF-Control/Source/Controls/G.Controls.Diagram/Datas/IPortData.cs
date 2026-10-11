namespace G.Controls.Diagram.Datas;

public interface IPortData : ILinkInitializer, IData
{
    string ID { get; set; }
    string NodeID { get; set; }
    string Name { get; set; }
    Dock Dock { get; set; }
    PortType PortType { get; set; }
    Thickness PortMargin { get; set; }
}
