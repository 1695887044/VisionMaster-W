using G.Common.Interfaces;

namespace G.Services.Setting;

public interface ISettable : INameable, IOrderable, IGroupable
{
    bool IsVisibleInSetting { get; set; }
}
