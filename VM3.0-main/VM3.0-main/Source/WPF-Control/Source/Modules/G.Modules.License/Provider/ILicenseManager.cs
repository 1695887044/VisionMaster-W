namespace G.Modules.License
{
    public interface ILicenseManager
    {
        string Decrypt(string source, string key);
        string Encrypt(string source, string key);
        string GetHostID();
        Tuple<string, string> GetPK();
    }
}