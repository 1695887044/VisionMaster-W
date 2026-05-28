
// 服务器实例列表，用于统一管理启动和停止
using HslCommunication.ModBus;
using HslCommunication.Profinet.Siemens;

List<ModbusTcpServer> modbusServers = new List<ModbusTcpServer>();
List<SiemensS7Server> s7Servers = new List<SiemensS7Server>();

try
{
    // ====================== 启动 Modbus TCP 服务器 (端口502-505) ======================
    Console.WriteLine("===== 正在启动 Modbus TCP 服务器 =====");
    for (int port = 502; port <= 505; port++)
    {
        ModbusTcpServer server = new ModbusTcpServer();
        server.Port = port;
        server.ServerStart();
    }

    // ====================== 启动 S7 模拟服务器 (端口103-105) ======================
    Console.WriteLine("\n===== 正在启动 S7 模拟服务器 =====");
    for (int port = 103; port <= 105; port++)
    {
        SiemensS7Server server = new SiemensS7Server();
        server.Port = port;


       server.ServerStart();
    }

    // ====================== 服务器运行中 ======================
    Console.WriteLine("\n=====================================");
    Console.WriteLine("所有服务器已启动，按任意键停止...");
    Console.WriteLine("=====================================");
    Console.ReadKey();
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ 程序发生致命错误: {ex.Message}");
}
