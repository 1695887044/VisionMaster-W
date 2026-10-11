using G.Extensions.Common;
using G.Extensions.NewtonsoftJson;
using G.Services.AppPath;
using G.Services.Serializable;
using System.IO;

namespace G.Components.Modbus;

public class SerializableModbusDataService : ModbusDataService, ISerializableModbusDataService
{
    private NewtonsoftJsonSerializerService _serializerService = new NewtonsoftJsonSerializerService();

    public bool Load(out string message)
    {
        try
        {
            var datas = this._serializerService.Load<ModbusTcpDatas>(this.GetFilePath());
            this.Collection = datas ?? new ModbusTcpDatas();
            message = null;
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            IocLog.Instance?.Error(ex);
            return true;
        }
    }

    protected string GetFilePath()
    {
        string path = Path.Combine(AppPaths.Instance.Data, "ModbusDatas.json");
        if (!Directory.Exists(Path.GetDirectoryName(path)))
            Directory.CreateDirectory(Path.GetDirectoryName(path));
        return path;
    }

    public bool Save(out string message)
    {
        try
        {
            this.Stop().Wait();
            this._serializerService.Save(this.GetFilePath(), this.Collection);
            message = null;
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            IocLog.Instance?.Error(ex);
            return true;
        }

    }
}

