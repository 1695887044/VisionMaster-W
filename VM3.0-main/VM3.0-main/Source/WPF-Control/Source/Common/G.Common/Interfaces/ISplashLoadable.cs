namespace G.Common.Interfaces;

/// <summary>
/// 项目启动会预加载的接口
/// </summary>
public interface ISplashLoadable : ILoadable
{
    string Name { get; }
}