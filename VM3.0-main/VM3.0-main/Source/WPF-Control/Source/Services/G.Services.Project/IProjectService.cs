global using G.Common.Interfaces;

namespace G.Services.Project;

public interface IProjectService : ISplashSave, ISplashLoadable
{
    IProjectItem Current { get; set; }
    IProjectItem Create();
    void Add(IProjectItem project);
    void Delete(Func<IProjectItem, bool> func);
    IEnumerable<IProjectItem> Where(Func<IProjectItem, bool> func = null);
    Action<IProjectItem, IProjectItem> CurrentChanged { get; set; }
}