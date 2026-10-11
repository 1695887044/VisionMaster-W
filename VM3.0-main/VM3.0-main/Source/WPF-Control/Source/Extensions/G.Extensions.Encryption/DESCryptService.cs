using G.Extensions.Encryption.String;
using G.Services.Common.Crypt;

namespace G.Extensions.Encryption;

public class DESCryptService : ICryptService
{
    public string Decrypt(string value)
    {
        return DESExtension.DecryptDES(value);
    }

    public string Encrypt(string value)
    {
        return DESExtension.EncryptDES(value);
    }
}
