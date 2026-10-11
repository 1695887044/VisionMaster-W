namespace G.Services.Serializable.Web;

public interface IWebXmlSerializerService
{
    T Load<T>(string uri, out string message);
}