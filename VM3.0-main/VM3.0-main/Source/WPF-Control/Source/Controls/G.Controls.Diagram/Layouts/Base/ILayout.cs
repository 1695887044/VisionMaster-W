namespace G.Controls.Diagram.Layouts.Base;

public interface ILayout
{
    Diagram Diagram { get; set; }
    void DoLayout(params Node[] nodes);
    void UpdateNode(params Node[] nodes);
    void RemoveNode(params Node[] nodes);
    void AddNode(params Node[] nodes);
    void DoLayoutLink(Link link);
}
