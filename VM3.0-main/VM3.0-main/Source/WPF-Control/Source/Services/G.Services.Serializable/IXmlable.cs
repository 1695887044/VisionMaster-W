using System.Reflection;
using System.Xml;

namespace G.Services.Serializable;

public interface IXmlable
{
    void FromXml(XmlElement xmlEle, XmlDocument cnt, Func<PropertyInfo, object, bool> match = null);

    void ToXml(XmlElement xmlEle, XmlDocument cnt, Func<PropertyInfo, object, bool> match = null);
}