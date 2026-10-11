namespace G.Modules.Upgrade;

public interface IUpgradeOptions
{
    bool AutomaticUpgrade { get; set; }
    bool CheckUpdateOnStart { get; set; }
    string LoadFormat { get; set; }
    bool NotifyUpgrade { get; set; }
    string SavePath { get; set; }
    string Uri { get; set; }
    bool UseIEDownload { get; set; }
}