using G.Common.Interfaces;
using G.Controls.FavoriteBox;
using G.Controls.TagBox;
using G.Extensions.Common;
using G.Modules.Login;
using G.Modules.Project;
using G.Services.Common;
using G.Services.Project;
using Microsoft.Extensions.Options;
using System;

namespace G.App.FileManager
{
    public class FileProjectService : ProjectServiceBase<FileProjectItem>, IProjectService, ILoginedSplashLoadable
    {
        private readonly IOptions<TagOptions> _tagOptions;
        private readonly IOptions<FavoriteOptions> _favoriteOptions;
        public FileProjectService(IOptions<ProjectOptions> options, IOptions<TagOptions> tagOptions, IOptions<FavoriteOptions> favoriteOptions) : base(options)
        {
            _tagOptions = tagOptions;
            _favoriteOptions = favoriteOptions;
        }

        public override FileProjectItem Create()
        {
            return new FileProjectItem()
            {
                Title = DateTime.Now.ToString("yyyyMMddHHmmss"),
                Path = this.GetFolderPath(),
                Tags = _tagOptions.Value.Tags.ToObservable(),
                FavoriteItems = _favoriteOptions.Value.FavoriteItems.ToObservable()
            };
        }
    }
}
