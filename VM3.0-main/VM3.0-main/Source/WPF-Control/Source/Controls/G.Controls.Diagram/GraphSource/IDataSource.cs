namespace G.Controls.Diagram.GraphSource;

public interface IDataSource<NodeDataType, LinkDataType>
{
    List<NodeDataType> GetNodeDatas();

    List<LinkDataType> GetLinkDatas();
}
