namespace G.Extensions.DataBase.Repository
{
    public interface ITreePath
    {
        string GetFullPath();
        void SetPath(string path);
    }
}
