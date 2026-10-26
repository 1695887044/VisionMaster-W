# VirtualLineConsole（智能装配检测线虚拟设备）

外置控制台程序，给 `解决方案\智能装配检测线` 提供可真实读写的虚拟设备：

| 服务 | 端口 | 地址示例 |
| --- | --- | --- |
| Modbus TCP PLC | 503 | HR0、HR1、线圈 0/1/2、HR4/HR6/HR8 |
| Modbus TCP Meter | 504 | IR0 温度、IR10 压力、IR20 扭矩（CDAB） |
| Siemens S7-1200 | 1102 | DB1.0、M0.0、M1.0 |
| MES HTTP | 19001 | `POST /api/upload`、`GET /health` |

## 运行

```powershell
cd tools\VirtualLineConsole
dotnet build -m:1 -nodeReuse:false
bin\Debug\net48\VirtualLineConsole.exe
```

控制台命令：`auto` 自动触发（每 2 秒）、`manual` 手动、`trigger` 手动触发一次、`reset` 复位、`status` 查看上报次数与平均节拍、`q` 退出。

每次 `/api/upload` 会打印 `sn / result / yield / defect / time`，并累计上报次数与平均间隔，用于流程性能测试。

## 与方案对应关系

- `PLC_Line` → 503
- `Meter_Station` → 504
- `PLC_S7_Sim` → 1102（端口避开 102 特权端口）
- `Main.结果上报` → `http://127.0.0.1:19001/api/upload`

## 注意

- 503/504/1102/19001 等端口如被占用，先关闭占用进程再启动；启动会检查并打印。
- 服务与方案里的连接配置必须同时修改；不要只改一边。
