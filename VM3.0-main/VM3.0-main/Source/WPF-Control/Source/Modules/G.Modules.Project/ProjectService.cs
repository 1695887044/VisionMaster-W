global using Microsoft.Extensions.Options;

namespace G.Modules.Project;

public class ProjectService : ProjectServiceBase<ProjectItem>, IProjectService
{
    public ProjectService(IOptions<ProjectOptions> options) : base(options)
    {

    }

    public override ProjectItem Create()
    {
        return new ProjectItem();
    }
}
