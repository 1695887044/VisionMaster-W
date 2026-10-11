namespace G.Services.Serializable.Web;

public interface IWebJsonSerializerService
{
    T Load<T>(string uri, out string message);
}