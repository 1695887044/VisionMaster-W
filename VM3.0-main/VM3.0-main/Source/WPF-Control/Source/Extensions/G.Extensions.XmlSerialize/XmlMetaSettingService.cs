using G.Services.AppPath;
using G.Services.Common.Serialize.Meta;
using System.IO;

namespace G.Extensions.XmlSerialize
{
    public class XmlMetaSettingService : IMetaSettingService
    {
        [Obsolete]
        private ISerializerService XmlSerializer => new XmlSerializerService();

        [Obsolete]
        public T Deserilize<T>(string id)
        {
            string path = Path.Combine(AppPaths.Instance.Cache, typeof(T).Name, id + ".xml");

            if (!File.Exists(path)) return default(T);

            return this.XmlSerializer.Load<T>(path);
        }

        [Obsolete]
        public void Serilize(object setting, string id)
        {
            Application.Current.Dispatcher.BeginInvoke(MetaSetting.Instance.DispatcherPriority, new Action(() =>
                       {
                           string path = Path.Combine(AppPaths.Instance.Cache, setting.GetType().Name, id + ".xml");

                           this.XmlSerializer.Save(path, setting);
                       }));

        }
    }

}
