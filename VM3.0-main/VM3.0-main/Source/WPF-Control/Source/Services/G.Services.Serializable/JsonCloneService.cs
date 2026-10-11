using System.Text.Json;

namespace G.Services.Serializable;

public class JsonCloneService : ICloneService
{
    public object Clone(object o)
    {
        string txt = JsonSerializer.Serialize(o);
        return JsonSerializer.Deserialize(txt, o.GetType());
    }
}