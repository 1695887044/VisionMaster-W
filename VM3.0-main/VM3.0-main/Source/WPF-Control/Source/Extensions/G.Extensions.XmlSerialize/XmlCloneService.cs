using System.IO;
using System.Xml.Serialization;

namespace G.Extensions.XmlSerialize
{
    public class XmlCloneService : ICloneService
    {
        public object Clone(object realObject)
        {
            using (Stream stream = new MemoryStream())
            {
                XmlSerializer serializer = new XmlSerializer(realObject.GetType());
                serializer.Serialize(stream, realObject);
                stream.Seek(0, SeekOrigin.Begin);
                return serializer.Deserialize(stream);
            }
        }
    }
}
