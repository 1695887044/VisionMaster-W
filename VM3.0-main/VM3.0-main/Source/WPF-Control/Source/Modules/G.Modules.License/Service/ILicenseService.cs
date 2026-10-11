namespace G.Modules.License
{
    public interface ILicenseService
    {
        LicenseOption IsVail(out string error);
        LicenseOption IsVail(string module, out string error);
        LicenseOption TryActive(string module, string lic, out string error);
        string GetHostID();
    }

    public class LicenseProxy : Ioc<ILicenseService>
    {
    }
}