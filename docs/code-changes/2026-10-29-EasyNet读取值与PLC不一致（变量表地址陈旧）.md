# 2026-10-29 EasyNet 读取值与 PLC 不一致（根因：变量表地址陈旧，不是驱动缺陷）

> 现场反馈：「你新加的 Easy Net 读取的和程序变量对不上，Alarm3[79] 是 True，程序没读取对」，被测对象 `127.0.0.1:12939`（AutoShop 自带 H5U 模拟器 `h5u_daemon.exe`，工程 `C:\Users\16958\Desktop\歌尔现场\小车\PLC`）。

## 一、当前状态

**结论：EasyNet 驱动读取正确；导入 VisionMaster 的变量表用的是旧地址。**

同一位点在两个地址上的实测值（原始协议帧，绕开本仓代码）：

| 变量 | 表里的地址 | 实测 | 编译产物里的地址 | 实测 |
|---|---|---|---|---|
| Alarm3[79] | `U210019F` | **0（False）** | `U210118F` | **1（True）** |
| PLCVersion | `U501FE0` | 0.0 | `U2100000` | **1.0** |
| CyCurrentPage | `U501F40` | 0 | `U2100020` | 0 |

整段校验：按编译地址读 `Alarm3[0..99]`（`U2101140` 起 100 位），**只有下标 79 为 1**——与现场看到的「Alarm3[79] 是 True」逐位吻合；按表里的地址读 `U2100150` 起 100 位，全 0。

VisionMaster 侧存的确实是旧地址（`Autosave\recovered\视觉样例对拍-20261029-180724.vms`）：

```json
"Name": "Alarm3[79]", "ConnectionName": "Conn_172334", "Protocol": "EasyNet",
"Area": "U", "Offset": "210019F"
```

## 二、根因

1. 变量表来自 `C:\Users\16958\Desktop\12.csv`（汇川变量导出，**文件内时间戳 2026-10-29 01:40:47**），由 `tools\oneoff\EasyNetCsvImport` 转成 `Plc_变量表.xlsx`（08:19）。
2. PLC 工程在 01:40 之后重新编译过（`PLC.hcp`/`VarAddrMap.am`/`VarNameAndAddr.csv` 均为 **16:46**），AutoShop 对未显式指定地址的全局变量**重新分配了地址**，并改动了部分变量结构（当前工程已无 `RECIPES` / `RAIL_*` / `STATICE` / `SHOWSTR` 这些根名）。
3. 于是表里 2357 条变量中，能在当前编译符号表里按名找到的 **437 条地址全部对不上**（437/437 不一致，0 条相同）；其余 1920 条的变量本身在当前工程里已不存在或属于另一台 PLC。
4. 驱动侧无缺陷：报文、位序（LSB-first）、批量段合并、U 区十六进制地址都已用原始帧验证（`U2100000` 读出 float `1.0` = PLCVersion，位序唯一解）。

**权威地址源**（按可信度排序，三者一致）：

| 来源 | 内容 |
|---|---|
| `歌尔现场\小车\PLC\VarAddrMap.am` | 编译器输出，9649 条符号（含数组元素/结构体字段）→ 地址，格式 `u16 长度 + 名称 + NUL + u32 LE 地址` |
| `歌尔现场\小车\PLC\VarNameAndAddr.csv` | 编译器输出，622 条全局变量（UTF-16LE，`名称/0X地址/描述/位数`） |
| `歌尔现场\现场触摸屏\obj\Tags.dat` | HMI 工程标签（432 条，`名称…39#十进制地址#-1`），与上面两者逐条一致 |

## 三、已做

1. **诊断与取证**（全部原始帧探针，落 `artifacts\logs\20261029\`）：
   - `easynet_bitprobe.txt`：按旧表 5 个 U|B 段回放，段内解码与单点读一致（证明批读/解码无错）。
   - `easynet_scan.txt` / `easynet_scan2.txt`：U 区全域扫描；`0x210118F` 是唯一与报警相关的置位点。
   - `am_map.txt` / `fix_table_report.txt`：`VarAddrMap.am` 解析 + 旧表比对（437/437 不一致）。
   - `final_validate.txt`：修正前后逐点实测对照表。
2. **生成修正表**：`artifacts\render\20261029\Plc_变量表_修正地址.xlsx`（437 行，20 列格式与原表一致，仅「偏移量」改为编译地址；导入侧按变量名更新，不动其它行）。
   - `Alarm1[0]` `2100070` → `2101060`；`Alarm3[79]` `210019F` → `210118F`；`PLCHeart` `100064` → `2100340`；`PLCVersion` `501FE0` → `2100000`。
   - 逐行越界自检 0 条（每行都落在所属基址的编译位数内）。
3. **一次性工具**（`tools\oneoff\`，R166）：`easynet_varprobe.py`、`easynet_scan.py`、`easynet_scan2.py`、`parse_am_full.py`、`make_fixed_table.py`、`final_validate.py`、`vms_check.py`。

## 四、未做

1. **其余 1920 条没有修正**（当前工程里找不到同名符号）：
   - `Recipes` 1220 条（该区域现属 `Sv_Cmd`）、`Rail_*` 283 条（属于另一台 PLC「切轨PLC」的连接，本连接读不到）、`sHmi_Sv.*` 78 条（现为 `sHmi_Servo_Cmd`/`sHmi_Servo_State`/`Sv_Cmd`）、`CurrentRecipe`/`sa`/`Statice`/`保存记忆`/`ShowStrs` 等。
   - 这些必须**从当前工程重新导出**后再导入，不能靠改名猜。
2. **驱动侧未改任何代码**：本轮定位不到需要改的驱动逻辑；如果后续确认 U 区存在「非字对齐批量读」之类的协议边界，再单独立项。

## 五、下一步

1. 用 `Plc_变量表_修正地址.xlsx` 在「变量管理 → 按连接导入」重新导入（按名更新 437 条），确认 Alarm3[79] 变 True。
2. 变量表的生产流程要改：**每次 PLC 重新编译后重新导出变量表**；导出文件里带时间戳（12.csv 第 2 行），导入前先核对它是否晚于 `VarAddrMap.am` 的 `LastWriteTime`。
3. 需要一次性重建全表时，以 `VarAddrMap.am` 为准（`parse_am_full.py` 已能解析），类型/元素个数从旧表或 `VarNameAndAddr.csv` 的位数推。
4. 「切轨PLC」那 283 条要单独建连接，不能混在本连接的变量表里。

## 六、踩过的坑

1. **`Get-Content` 读 UTF-16 的 `VarNameAndAddr.csv` 会"看起来正常"**：PowerShell 会按 BOM 自动识别，但 Python 直接 `open(encoding='utf-8')` 会在第 0 字节 `0xFF` 报错；解析前必须先判 BOM。
2. **`12.csv` 是 GB18030 无 BOM**：按 UTF-8 硬解不会抛异常，只会把中文读成 `?`（本轮第一次 dump 就是这样），必须 `decode('gb18030')`。
3. **`VarAddrMap.am` 是 `u16 长度 + 名称 + NUL + u32 小端地址` 的紧凑记录**，长度含 NUL；`VarNameAndAddr.csv` 只到全局变量（622 条），`.am` 才有数组元素与结构体字段（9649 条）。
4. **不要用「同名即同变量」推断地址迁移**：本次 437 条同名变量的地址全部变了（0 条相同），而不同名（`Rail_Servo_Cmd` vs `Sv_Cmd`）的反而可能是同一份数据——名字和地址两个维度都得独立核。
5. **模拟器内存里"看起来像业务数据"的置位点会误导**：`0x2102110` 一带的 `0xFFFF`、`0x21030F2` 等是模拟器默认值，与变量表无关；判定必须以「编译符号表 + 现场值」双证。
