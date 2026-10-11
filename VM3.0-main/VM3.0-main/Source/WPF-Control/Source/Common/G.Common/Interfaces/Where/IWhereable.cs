global using System.Collections;

namespace G.Common.Interfaces.Where;

public interface IWhereable
{
    IEnumerable Where(IEnumerable from);
}
