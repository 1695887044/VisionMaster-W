namespace G.Services.Common.Crypt;

public interface ICryptService
{
    string Encrypt(string value);
    string Decrypt(string value);
}