namespace G.Common.Interfaces;

/// <summary>
/// 项目关闭会预保存的接口
/// </summary>
public interface ISplashSave : ISaveable
{
    string Name { get; }
}