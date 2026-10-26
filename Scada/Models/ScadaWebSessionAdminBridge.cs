namespace VisionMaster.Scada
{
    /// <summary>
    /// 可选 Web 模块与宿主之间的在线会话管理桥。
    /// Web 模块在 Initialize 里把 <see cref="ScadaWebHost"/> 挂上来；宿主注册
    /// <see cref="IScadaWebSessionAdmin"/> 时从这里取。未加载 Web 时保持 null，协调器按"不可用"降级。
    /// </summary>
    public static class ScadaWebSessionAdminBridge
    {
        public static IScadaWebSessionAdmin? Current { get; set; }
    }
}
