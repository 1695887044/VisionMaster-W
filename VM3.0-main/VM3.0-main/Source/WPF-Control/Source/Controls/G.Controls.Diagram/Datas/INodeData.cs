namespace G.Controls.Diagram.Datas;

public interface INodeData : IPartData
{
    string ID { get; set; }
    Point Location { get; set; }
}

