global using G.Common.Attributes;
global using G.Common.Commands;
using G.Services.AppPath;
using G.Services.Message;
using System.ComponentModel.DataAnnotations;

namespace G.Extensions.AppPath;

[Icon("\xE77F")]
[Display(Name = "清空缓存", Description = "清空当前应用程序所保存的缓存数据")]
public class ClearCacheDataCommand : DisplayMarkupCommandBase
{
    public override async Task ExecuteAsync(object parameter)
    {
        var r = AppPaths.Instance.ClearCache(out string message);
        if (r == false)
        {
            await IocMessage.Dialog.Show(message, x => x.Title = "清空缓存失败");
            return;
        }
        await base.ExecuteAsync(parameter);
    }
}
