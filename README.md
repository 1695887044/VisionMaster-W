# VisionMaster 通讯模块

基于 HslCommunication 的工业通讯框架，支持 Modbus TCP/RTU、西门子 S7 等协议。

## 核心组件

### 配置类
- **ConnectionConfigBase** - 配置基类
- **ModbusTcpConfig** - Modbus TCP 配置
- **SiemensS7Config** - 西门子 S7 配置  
- **SerialConfig** - 串口 RTU 配置
- **CommunicationConfig** - 完整连接配置

### 连接类
- **ICommunicationConnection** - 连接接口
- **ModbusTcpConnection** - Modbus TCP 连接实现
- **SiemensS7Connection** - 西门子 S7 连接实现
- **SerialConnection** - 串口连接实现

### 管理类
- **AdvancedCommunicationManager** - 通讯管理器
- **ConnectionFactoryManager** - 工厂管理器

## 快速开始

### 1. 创建配置

```csharp
// Modbus TCP
var modbus = CommunicationConfig.CreateModbusTcp("PLC_1", "192.168.1.100", 502);

// 西门子 S7
var s7 = CommunicationConfig.CreateSiemensS7("S7_PLC", "192.168.1.10", "S1200");

// 串口 RTU
var rtu = CommunicationConfig.CreateModbusRtu("Sensor", "COM3", 115200);
```

### 2. 使用管理器

```csharp
var manager = new AdvancedCommunicationManager();

// 添加连接
manager.AddConnection(config);

// 连接
manager.Connect("PLC_1");

// 读取数据
var value = manager.Read<short>("PLC_1", "HR0");

// 写入数据
manager.Write("PLC_1", "HR0", (short)100);

// 断开
manager.Disconnect("PLC_1");

// 保存配置
await manager.SaveConfigAsync();
```

### 3. 使用连接工厂

```csharp
var manager = ConnectionFactoryManager.Instance;
var factory = manager.GetFactory(CommunicationType.ModbusTcp);
var connection = manager.CreateConnection(config);
```

## 配置属性

### ModbusTcpConfig
- `IpAddress` - IP 地址
- `Port` - 端口号 (默认502)
- `TimeoutMs` - 超时时间
- `RetryCount` - 重试次数
- `EnableKeepAlive` - 启用 KeepAlive

### SiemensS7Config
- `IpAddress` - IP 地址
- `Port` - 端口号 (默认102)
- `S7CpuType` - CPU类型 (Smart200/S1200/S1500/S300/S400)
- `Rack` - 机架号
- `Slot` - 插槽号

### SerialConfig
- `PortName` - 串口号 (COM1/COM2...)
- `BaudRate` - 波特率
- `DataBits` - 数据位
- `Parity` - 校验位
- `StopBits` - 停止位

## 数据类型支持

- `bool` - 布尔值
- `byte` - 字节
- `short/ushort` - 16位整数
- `int/uint` - 32位整数
- `float` - 浮点数
- `double` - 双精度浮点数

## 事件

```csharp
manager.ConnectionStateChanged += (s, e) => 
{
    Console.WriteLine($"{e.ConnectionName}: {e.OldState} -> {e.NewState}");
};

manager.ConnectionError += (s, e) =>
{
    Console.WriteLine($"{e.ConnectionName}: {e.Exception.Message}");
};
```

## 特性

- ✅ 多协议支持 (Modbus/西门子S7/串口)
- ✅ 工厂模式架构
- ✅ 自动重连
- ✅ 配置持久化
- ✅ 线程安全
- ✅ IDisposable 资源管理

## 测试数据

测试配置文件位于: `communications_test_data.json`

导入测试数据:
```csharp
await manager.ImportConfigAsync("communications_test_data.json");
```
