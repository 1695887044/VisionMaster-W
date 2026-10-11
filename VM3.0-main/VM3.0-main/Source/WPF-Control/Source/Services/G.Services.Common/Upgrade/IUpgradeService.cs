namespace G.Services.Common.Upgrade;

public interface IUpgradeService
{
    bool CanUpgrade(out string message);
    bool Upgrade(out string message);
    string UpgradeVersion { get; }
}