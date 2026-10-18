// See https://aka.ms/new-console-template for more information
using HslCommunication.ModBus;

Console.WriteLine("开始检测-D155信号");
ModbusTcpNet plc = new ModbusTcpNet();
plc.Port = 502;
plc.IpAddress = "192.168.1.10";
plc.DataFormat = HslCommunication.Core.DataFormat.CDAB;
plc.ConnectServer();
CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

await Task.Run(async() => {
    while (!cancellationTokenSource.Token.IsCancellationRequested)
    {
        await Task.Delay(50);
        var result = plc.ReadInt16("155");
        if (result.IsSuccess && result.Content == 1)
        {
            Console.WriteLine($"收到PLC-D155-触发信号---{DateTime.Now}");
            await Task.Delay(300);
            Console.WriteLine($"开始写入-清空---{DateTime.Now}");
            plc.Write("155", 11);

        }
    }

},cancellationTokenSource.Token);
Console.ReadKey();