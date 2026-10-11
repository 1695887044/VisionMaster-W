using G.Extensions.Setting;
using G.Services.Setting;
using System.ComponentModel.DataAnnotations;

namespace G.Extensions.TypeLicense;

[Display(Name = "组件许可设置", GroupName = SettingGroupNames.GroupSystem, Description = "组件许可设置的信息")]
public class TypeLicenseOptions : IocOptionInstance<TypeLicenseOptions>, ITypeLicenseOptions
{

}
