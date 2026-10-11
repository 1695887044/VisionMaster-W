using G.Common.Attributes;
using G.Common.Commands;
using G.Components.Modbus.Presenters;

namespace G.Components.Modbus;

[Icon("\xE963")]
[Display(Name = "寄存器管理", Description = "显示寄存器管理")]
public class ShowModbusDataCommand : DisplayMarkupCommandBase
{
    private ModbusDataViewPresenter _modbusDataViewPresenter = new ModbusDataViewPresenter();
    public override async Task ExecuteAsync(object parameter)
    {
        await IocMessage.ShowDialog(this._modbusDataViewPresenter, x =>
        {
            x.HorizontalAlignment = HorizontalAlignment.Stretch;
            x.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            x.VerticalContentAlignment = VerticalAlignment.Stretch;
            x.MinWidth = 600;
            x.MinHeight = 400;
        });
    }

    public override bool CanExecute(object parameter)
    {
        return base.CanExecute(parameter) && Ioc<ISerializableModbusDataService>.Instance != null;
    }
}

