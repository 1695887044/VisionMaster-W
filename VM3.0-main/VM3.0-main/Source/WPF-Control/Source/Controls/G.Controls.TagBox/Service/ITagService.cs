global using G.Common.Interfaces;

namespace G.Controls.TagBox
{
    public interface ITagService : IDataSource<ITag>, ISplashLoadable, ISplashSave
    {
        ITag Create();
        string ConvertToCheck(string value, ITag tag);
        string ConvertToUnCheck(string value, ITag tag);
        bool ContainTag(string name, ITag tag);
        IEnumerable<ITag> ToTags(string name);
    }

    public class IocTagService : Ioc<ITagService>
    {

    }
}
