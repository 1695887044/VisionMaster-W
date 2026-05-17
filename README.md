# VisionMaster

工业视觉检测与流程自动化平台 - 基于 WPF 的现代化机器视觉软件框架

## 目录

- [项目概述](#项目概述)
- [系统架构](#系统架构)
- [核心模块](#核心模块)
  - [通信模块](#通信模块)
  - [流程引擎](#流程引擎)
  - [插件系统](#插件系统)
  - [全局变量系统](#全局变量系统)
  - [性能监控](#性能监控)
- [快速开始](#快速开始)
- [API 参考](#api-参考)
- [扩展开发](#扩展开发)

---

## 项目概述

VisionMaster 是一个功能完整的工业视觉检测与流程自动化平台，采用现代化的 WPF + Prism 架构设计，支持：

- **多协议工业通讯**：Modbus TCP/RTU、西门子 S7 系列 PLC、三菱、欧姆龙等
- **可视化流程编排**：拖拽式流程设计，支持条件分支、循环等控制结构
- **插件化架构**：灵活的插件系统，支持自定义功能扩展
- **实时性能监控**：CPU、内存、线程等系统资源监控
- **全局变量管理**：支持本地变量和通讯变量的统一管理
- **高级数据处理**：数据转换、阈值报警、历史记录、统计等

### 技术栈

| 技术 | 版本 | 用途 |
|------|------|------|
| .NET | 9.0 | 运行时框架 |
| WPF | - | UI 框架 |
| Prism | 9.0 | MVVM 框架 |
| HslCommunication | - | 工业通讯库 |
| NLog | 6.0 | 日志系统 |
| DynamicExpresso | 2.19 | 表达式解析 |

---

## 系统架构

```
VisionMaster/
├── Communications/                  # 通讯模块
│   ├── DeviceAddressBase.cs        # 地址配置基类 + 数据转换管道
│   ├── DataPoint.cs                # 数据点类
│   ├── DataPointManager.cs         # 数据点管理器
│   ├── DataPointAlarm.cs           # 阈值报警
│   ├── DataPointHistory.cs         # 历史数据记录
│   ├── DataPointConfigurationManager.cs  # 配置持久化
│   ├── ConnectionStatistics.cs     # 通讯质量统计
│   ├── ConnectionHealthCheck.cs    # 连接健康检查
│   ├── BaseConnection.cs           # 连接泛型基类
│   ├── ModbusTcpConnection.cs      # Modbus TCP 连接
│   ├── SiemensS7Connection.cs      # 西门子 S7 连接
│   ├── SerialConnection.cs         # 串口连接
│   ├── AdvancedCommunicationManager.cs  # 通讯管理器
│   └── ConnectionFactory.cs        # 连接工厂
├── Services/                       # 服务层
│   ├── FlowCompiler.cs             # 流程编译器
│   ├── FlowEngineService.cs        # 流程执行引擎
│   └── PluginService.cs            # 插件服务
├── Models/                         # 数据模型
│   ├── FlowModel.cs                # 流程模型
│   └── GlobalVariableModel.cs      # 全局变量模型
└── Core/                           # 核心组件
    ├── PerformanceMonitor.cs       # 性能监控
    └── ObjectPool.cs               # 对象池

UI/Controls/                        # UI 控件库
├── CustomControl/
│   └── CommunicationTextBox.cs     # 双向绑定通讯文本框
```

---

## 核心模块

### 通信模块

工业通讯模块提供统一的设备通讯接口，支持多种工业协议。采用三层架构设计：

1. **DeviceAddressBase** - 地址配置层，包含数据转换管道
2. **DataPoint** - 数据点层，存储运行时数据
3. **DataPointManager** - 数据点管理层，负责地址-数据关联

#### 支持的协议

| 协议 | 类型 | 说明 |
|------|------|------|
| Modbus TCP | `CommunicationType.ModbusTcp` | TCP/IP 上的 Modbus 协议 |
| Modbus RTU | `CommunicationType.ModbusRtu` | 串口 Modbus 协议 |
| 西门子 S7 | `CommunicationType.SiemensS7` | S7-200Smart/S7-1200/S7-1500 等 |

---

## 高级功能

### 1. 数据转换管道 ⭐

在地址配置中支持原始值到工程值的自动转换：

```csharp
var address = new ModbusAddress
{
    Offset = "0",
    EnableConversion = true,
    Scale = 0.01,           // 缩放系数
    EngineeringOffset = 0,   // 工程偏移
    Unit = "°C",            // 工程单位
    DecimalPlaces = 2        // 小数位数
};

// 原始值 1234 -> 工程值 12.34 °C
var engineeringValue = address.ConvertToEngineering(1234); // 12.34
var rawValue = address.ConvertToRaw(12.34);               // 1234
var formatted = address.FormatEngineeringValue(1234);     // "12.34 °C"
```

**支持的属性**：
- `EnableConversion` - 是否启用转换
- `Scale` - 缩放系数（如 0.01 表示除以100）
- `EngineeringOffset` - 工程偏移（加减）
- `Unit` - 工程单位（如 "°C"、"MPa"）
- `DecimalPlaces` - 显示小数位数（0-10）

---

### 2. 阈值报警机制 ⭐

支持四级报警：上上限、上限、下限、下下限：

```csharp
var alarm = new DataPointAlarm
{
    IsEnabled = true,
    
    HighHighEnabled = true,
    HighHigh = 100,
    
    HighEnabled = true,
    High = 80,
    
    LowEnabled = true,
    Low = 20,
    
    LowLowEnabled = true,
    LowLow = 10
};

var level = alarm.Check(95.5);  // AlarmLevel.High
var message = alarm.GetAlarmMessage(105); // "上上限报警: 105 >= 100"
```

**报警级别**：
- `Normal` - 正常
- `HighHigh` - 上上限（红色报警）
- `High` - 上限（橙色警告）
- `Low` - 下限（橙色警告）
- `LowLow` - 下下限（红色报警）

---

### 3. 通讯质量统计 ⭐

实时监控每个连接的通讯质量：

```csharp
var stats = new ConnectionStatistics { ConnectionName = "PLC_1" };

// 记录读取成功
stats.RecordReadSuccess(15.5);  // 15.5ms

// 记录读取失败
stats.RecordReadFailure("Connection timeout");

// 查看统计
Console.WriteLine($"成功率: {stats.ReadSuccessRate:F2}%");
Console.WriteLine($"平均响应时间: {stats.AverageReadTimeMs:F2}ms");
Console.WriteLine($"最大响应时间: {stats.MaxReadTimeMs:F2}ms");
Console.WriteLine($"连续失败次数: {stats.ConsecutiveFailures}");
Console.WriteLine($"是否健康: {stats.IsHealthy}");
```

**统计指标**：
- 总/成功/失败读取次数
- 总/成功/失败写入次数
- 平均/最大响应时间
- 最后读取/写入时间
- 连续失败次数
- 最后错误时间和信息

---

### 4. 连接健康检查 ⭐

自动监控连接状态，支持定时健康检查：

```csharp
var healthCheck = new ConnectionHealthCheck(commManager);

// 单次检查
var result = await healthCheck.CheckHealthAsync("PLC_1");
Console.WriteLine($"是否健康: {result.IsHealthy}");
Console.WriteLine($"响应时间: {result.ResponseTimeMs}ms");

// 启动定时监控
healthCheck.StartHealthMonitor("PLC_1", 60000); // 每60秒检查一次

// 订阅健康检查事件
healthCheck.HealthCheckCompleted += (s, e) => {
    Console.WriteLine($"[{e.ConnectionName}] 健康状态: {e.IsHealthy}");
};

// 获取上次检查结果
var lastResult = healthCheck.GetLastResult("PLC_1");
```

---

### 5. 历史数据记录 ⭐

记录数据点的历史变化，便于分析和回溯：

```csharp
var history = new DataPointHistory
{
    DataPointName = "Temperature",
    ConnectionName = "PLC_1",
    MaxHistorySize = 1000
};

// 记录数据
history.Record(25.5);
history.Record(26.1);
history.Record(25.8);

// 获取最近10条
var recent = history.GetRecent(10);

// 获取时间范围内的数据
var range = history.GetHistory(
    DateTime.Now.AddHours(-1),
    DateTime.Now);

// 获取统计信息
var stats = history.GetStatistics(
    DateTime.Now.AddHours(-1),
    DateTime.Now);

Console.WriteLine($"最小值: {stats.Min:F2}");
Console.WriteLine($"最大值: {stats.Max:F2}");
Console.WriteLine($"平均值: {stats.Average:F2}");
Console.WriteLine($"数据点: {stats.Count}");

// 清空历史
history.Clear();
```

---

### 6. 配置持久化 ⭐

支持导入/导出数据点配置：

```csharp
var configManager = new DataPointConfigurationManager(dpManager);

// 添加配置
configManager.AddConfiguration(new DataPointConfiguration
{
    Name = "Temperature",
    ConnectionName = "PLC_1",
    EnableConversion = true,
    Scale = 0.01,
    Unit = "°C",
    EnableAlarm = true
});

// 导出到文件
await configManager.ExportToFileAsync("configs.json");

// 从文件导入
await configManager.ImportFromFileAsync("configs.json");

// 获取所有配置
var configs = configManager.GetAllConfigurations();
```

---

### 7. 双向绑定控件 (CommunicationTextBox)

WPF 双向绑定控件，支持与 PLC 实时交互：

```xml
<Window xmlns:customControl="clr-namespace:UI.Controls.CustomControl">
    <StackPanel>
        <customControl:CommunicationTextBox
            Address="HR0"
            ConnectionName="PLC_1"
            DataType="{x:Type sys:Int16}"
            WriteOnEnter="True"
            WriteOnLostFocus="True"
            IsReadOnlyWhenWriting="True"
            AutoRefresh="True"
            RefreshIntervalMs="1000"/>
    </StackPanel>
</Window>
```

**功能特性**：
- ✅ 初始化同步 - 控件加载时自动读取 PLC
- ✅ 自动更新 - 数据源变化时 UI 自动刷新
- ✅ 手动写入 - 回车/失去焦点时自动写入 PLC
- ✅ 写入反馈 - 写入成功显示新值，失败恢复原值
- ✅ 冲突处理 - 用户输入时数据源更新不打断

**支持的数据类型**：
- bool, short, int, float, double
- byte, ushort, uint

---

## API 参考

### DeviceAddressBase - 数据转换

```csharp
public abstract class DeviceAddressBase
{
    // 数据转换配置
    public bool EnableConversion { get; set; }
    public double? Scale { get; set; }
    public double EngineeringOffset { get; set; }
    public string? Unit { get; set; }
    public int DecimalPlaces { get; set; }
    
    // 转换方法
    public object? ConvertToEngineering(object? rawValue);
    public object? ConvertToRaw(object? engineeringValue);
    public string FormatEngineeringValue(object? rawValue);
}
```

### DataPointAlarm - 报警

```csharp
public class DataPointAlarm
{
    public bool IsEnabled { get; set; }
    public bool HighHighEnabled { get; set; }
    public double HighHigh { get; set; }
    public bool HighEnabled { get; set; }
    public double High { get; set; }
    public bool LowEnabled { get; set; }
    public double Low { get; set; }
    public bool LowLowEnabled { get; set; }
    public double LowLow { get; set; }
    
    public AlarmLevel Check(double value);
    public AlarmLevel Check(object? value);
    public string GetAlarmMessage(double value);
}
```

### ConnectionStatistics - 统计

```csharp
public class ConnectionStatistics
{
    public string ConnectionName { get; set; }
    public int TotalReads { get; set; }
    public int SuccessReads { get; set; }
    public int FailedReads { get; set; }
    public double ReadSuccessRate { get; }
    public double AverageReadTimeMs { get; set; }
    public double MaxReadTimeMs { get; set; }
    public int ConsecutiveFailures { get; set; }
    public bool IsHealthy { get; }
    
    public void RecordReadSuccess(double responseTimeMs);
    public void RecordReadFailure(string errorMessage);
    public void RecordWriteSuccess(double responseTimeMs);
    public void RecordWriteFailure(string errorMessage);
    public void Reset();
}
```

### DataPointHistory - 历史

```csharp
public class DataPointHistory
{
    public string DataPointName { get; set; }
    public string ConnectionName { get; set; }
    public int MaxHistorySize { get; set; }
    public int CurrentCount { get; }
    
    public void Record(object value);
    public IReadOnlyList<DataPointHistoryItem> GetHistory(DateTime start, DateTime end);
    public IReadOnlyList<DataPointHistoryItem> GetRecent(int count);
    public DataPointStatistics GetStatistics(DateTime start, DateTime end);
    public void Clear();
}
```

### ConnectionHealthCheck - 健康检查

```csharp
public interface IConnectionHealthCheck
{
    Task<HealthCheckResult> CheckHealthAsync(string connectionName);
    void StartHealthMonitor(string connectionName, int intervalMs = 60000);
    void StopHealthMonitor(string connectionName);
    HealthCheckResult? GetLastResult(string connectionName);
}

public class HealthCheckResult
{
    public bool IsHealthy { get; set; }
    public double ResponseTimeMs { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string StatusMessage { get; set; }
    public DateTime CheckedTime { get; set; }
}
```

---

## 更新日志

### v3.0.0
- ✅ **数据转换管道** - 原始值到工程值的自动转换
- ✅ **阈值报警机制** - 四级报警支持
- ✅ **通讯质量统计** - 实时监控读写成功率和响应时间
- ✅ **连接健康检查** - 自动监控连接状态
- ✅ **历史数据记录** - 数据点历史变化记录
- ✅ **配置持久化** - 导入/导出数据点配置
- ✅ **双向绑定控件** - CommunicationTextBox 简化版

### v2.0.0
- 新增 DeviceAddressBase 地址配置层
- 新增 DataPoint 数据点层
- 新增 DataPointManager 数据点管理器
- 支持批量读写优化
- 支持智能数据缓存

### v1.0.0
- 初始版本发布
- 支持 Modbus TCP/RTU、西门子 S7 协议
- 实现流程编排和执行引擎
- 插件化架构设计
- 性能监控和全局变量系统

---

## 许可证

本项目仅供学习和研究使用。
