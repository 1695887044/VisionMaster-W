namespace G.Controls.Diagram.Datas;

public interface ILinkData : IPartData
{
    string FromNodeID { get; set; }
    string ToNodeID { get; set; }
    string FromPortID { get; set; }
    string ToPortID { get; set; }
}
