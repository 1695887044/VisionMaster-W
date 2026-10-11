namespace G.Modules.License;

public interface ILicenseOptions
{
    string Contract { get; set; }
    string FilePath { get; set; }
    int TrialDays { get; set; }
    bool UseTipTrialOnLoad { get; set; }
    bool UseTrial { get; set; }
    bool UseVailLicenceOnLoad { get; set; }
}