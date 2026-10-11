using Newtonsoft.Json;

namespace G.Extensions.NewtonsoftJson;
public interface INewtonsoftJsonOptions
{
    JsonSerializerSettings JsonSerializerSettings { get; set; }
}