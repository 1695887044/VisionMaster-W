using G.Common;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Threading;

namespace G.Extensions.XmlSerialize
{
    [Display(Name = "元配置数据")]
    public class MetaSetting : LazyInstance<MetaSetting>
    {
        [DefaultValue(DispatcherPriority.SystemIdle)]
        [Display(Name = "元数据配置保存的时机")]
        public DispatcherPriority DispatcherPriority { get; set; } = DispatcherPriority.SystemIdle;
    }
}
