using System.Reflection;

namespace G.Services.Common.Upgrade;

public class IocUpgrade : Ioc<IUpgradeService>
{
    public static string UpgradeVersion { get; set; } = Instance?.UpgradeVersion;
    public static bool HasNewVersion { get; set; } = Instance?.CanUpgrade(out string message) != null;
    public static string CurrentVersion { get; set; } = Assembly.GetEntryAssembly().GetName().Version.ToString();
}