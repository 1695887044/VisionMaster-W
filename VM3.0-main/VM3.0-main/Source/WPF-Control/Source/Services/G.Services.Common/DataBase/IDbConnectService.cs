// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Services.Common.DataBase;

public interface IDbConnectService : ISplashLoadable
{
    bool TryConnect(out string message);
}
