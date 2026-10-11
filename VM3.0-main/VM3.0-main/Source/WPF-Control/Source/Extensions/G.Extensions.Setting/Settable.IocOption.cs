using Microsoft.Extensions.Options;
using System.ComponentModel;

namespace G.Extensions.Setting;

public abstract class IocOptionInstance<Setting> : SettableBase, IOptions<Setting> where Setting : class, new()
{
    public static Setting Instance => Ioc.GetService<IOptions<Setting>>().Value;

    [Browsable(false)]
    [System.Text.Json.Serialization.JsonIgnore]

    [System.Xml.Serialization.XmlIgnore]
    Setting IOptions<Setting>.Value
    {
        get { return Instance; }
    }
}
