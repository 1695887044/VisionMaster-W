using System.Collections;

namespace G.Extensions.Tree;

public interface ITree
{
    IEnumerable GetChildren(object parent);
}
