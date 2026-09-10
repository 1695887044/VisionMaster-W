# Buffer(机器人) ↔ WMS(上位机) 任务时序图

> 依据《Buffer-交互表.pdf》流程图整理，报警位定义取自 Excel 附录（两者不一致处已用"注意"标出）。
>
> - 参与方：**WMS 上位机**（写控制区 D4000~D4006）、**Buffer 机器人**（写状态区 D4100~D4114）
> - 通讯：Modbus TCP，WMS 主动轮询，机器人无主动上报能力
> - 本文件为 Mermaid 时序图，在 Trae / VS Code 的 Markdown 预览中可直接渲染

---

## 0. 通用任务生命周期模板

所有任务（100~700）共用同一套交互骨架：

```mermaid
sequenceDiagram
    autonumber
    participant W as WMS 上位机
    participant R as Buffer 机器人

    Note over W,R: 前置条件: Buffer 空闲(RunState=0) 且就绪(SystemState.Bit3=1)
    W->>R: 下发任务码: WMS.OrderControl = 100~700
    W->>R: 下发参数: WMS.PickNum / PutNum / ReadyNum / CheckNum
    R->>R: 任务判断与校验(库位号合法性等)
    R-->>W: 受理反馈: Buffer.RunOrder=任务码, RunState=999(执行中)
    loop WMS 周期轮询(轮询周期未定义)
        R-->>W: RunState / SystemState / Error1~4
    end
    alt 正常完成
        R-->>W: RunState = 对应完成码(100/200/300/400/500/600/700)
        W->>R: 任务清空: WMS.OrderControl = 0
        R-->>W: 清任务: RunState=0, RunOrder=0 (回到空闲)
    else 异常报警
        R-->>W: Error1~4.BITx = 1 (挂起, 转第9节异常处理时序)
    end
```

注意：**任务"被 ErrMode=3 终止"后 RunState 直接回 0，没有独立的"失败终态码"**，WMS 只能靠任务状态被清零来间接推断任务被中止。

---

## 1. 系统控制（启动 / 复位 / 暂停 / 继续）

```mermaid
sequenceDiagram
    autonumber
    participant W as WMS 上位机
    participant R as Buffer 机器人

    Note over W,R: PDF流程图按"位"操作SystemControl, 与附录2的值定义(0启动/1复位/2暂停/3继续/4停止)存在分歧, 需与机器人方确认
    alt 暂停 (运行中 SystemState.Bit4=1)
        W->>R: 暂停请求: WMS.SystemControl.Bit2=1
        R-->>W: 暂停确认: Buffer.SystemState.Bit2=1 (暂停中)
        W->>R: WMS操作清空: SystemControl.Bit2=0
    else 继续 (暂停中 SystemState.Bit2=1)
        W->>R: 继续请求: WMS.SystemControl.Bit3=1
        R-->>W: 恢复运行: SystemState.Bit2=0, Bit4=1
    else 复位 (报错中 SystemState.Bit1=1)
        W->>R: 复位请求: WMS.SystemControl.Bit1=1
        R-->>W: 复位完成: SystemState.Bit1=0, Bit3=1 (就绪)
    else 启动 (就绪 SystemState.Bit3=1 且 Bit1=0)
        W->>R: 启动请求: WMS.SystemControl.Bit0=1
        R-->>W: 运行中: SystemState.Bit4=1
    end
    Note over W: 控制位为脉冲式(置1请求, 完成后清0), 但清零时机PDF未定义, 需确认
```

---

## 2. 出库任务（100）

库位取料 → 对接口放料：

```mermaid
sequenceDiagram
    autonumber
    participant W as WMS 上位机
    participant R as Buffer 机器人

    W->>R: 下发: WMS.OrderControl=100, WMS.PickNum=出库库位
    R->>R: 任务判断: 取料库位超限则 Error1.Bit7=1 转异常处理
    R-->>W: 受理: RunOrder=100, RunState=999

    rect rgb(232, 244, 255)
        Note over R: 取料段
        R->>R: 移动至取料拍照位
        R->>R: 天车到位检查: 不在位则 Error1.Bit5=1 转异常处理
        R->>R: 取料感应器检查: 无信号则 Error1.Bit6=1 转异常处理
        R->>R: 相机拍照识别
        alt 检测OK
            R-->>W: 上报产品码: Buffer.ID_1 / ID_2 = XXX
        else 位置/ID 组合NG
            R-->>W: Error1.Bit1(位OK码NG)/Bit2(位NG码OK)/Bit3(双NG)=1 转异常处理
        else 相机超时/硬件异常
            R-->>W: Error1.Bit4=1 转异常处理
        end
        R->>R: 到达取料位, 取料夹爪动作
        alt 夹爪检测有料
            R->>R: 取料完成
        else 夹爪异常
            R-->>W: Error1.Bit0=1(夹取异常)/Bit8=1(有料感应异常) 转异常处理
        end
    end

    rect rgb(232, 255, 238)
        Note over R: 放料段
        R->>R: 移动至放料位, 放料类型判断(人工/AMR/天车对接口)
        R->>R: 对接口感应检查: 丢失则 Error2.Bit3=1 转异常处理
        R->>R: 放料夹爪动作
        alt 放料后夹爪无料
            R->>R: 放料完成
        else 放料异常
            R-->>W: Error2.Bit0=1(夹爪打开异常) 转异常处理
        end
    end

    R-->>W: 完成: RunState=100 (出库任务完成)
    W->>R: 任务清空: WMS.OrderControl=0
    R-->>W: 清任务: RunState=0, RunOrder=0
```

---

## 3. 入库任务（200）

对接口取料 → 库位放料：

```mermaid
sequenceDiagram
    autonumber
    participant W as WMS 上位机
    participant R as Buffer 机器人

    W->>R: 下发: WMS.OrderControl=200, WMS.PutNum=入库库位
    R->>R: 任务判断: 放料库位超限则 Error2.Bit4=1 转异常处理
    R-->>W: 受理: RunOrder=200, RunState=999

    rect rgb(232, 244, 255)
        Note over R: 取料段(AMR/人工对接口)
        R->>R: 到达对接口, 对接口感应检查: 丢失则 Error1.Bit6=1 转异常处理
        R->>R: 取料夹爪动作
        alt 夹爪检测有料
            R->>R: 取料完成
        else 夹爪异常
            R-->>W: Error1.Bit0=1(夹取异常)/Bit8=1(有料感应异常) 转异常处理
        end
    end

    rect rgb(232, 255, 238)
        Note over R: 放料段(目标库位)
        R->>R: 到达库位物料检测位
        R->>R: 天车到位检查: 丢失则 Error2.Bit2=1 转异常处理
        alt 目标库位为空(感应器无信号)
            R->>R: 放料夹爪动作
        else 目标库位有料
            R-->>W: Error2.Bit1=1(放料目标库位有料) 转异常处理
        end
        alt 放料后夹爪无料
            R->>R: 放料完成
        else 放料异常
            R-->>W: Error2.Bit0=1(夹爪打开异常) 转异常处理
        end
    end

    R-->>W: 完成: RunState=200 (入库任务完成)
    W->>R: 任务清空: WMS.OrderControl=0
    R-->>W: 清任务: RunState=0, RunOrder=0
```

---

## 4. 提前准备任务（300）

```mermaid
sequenceDiagram
    autonumber
    participant W as WMS 上位机
    participant R as Buffer 机器人

    W->>R: 下发: WMS.OrderControl=300, WMS.ReadyNum=提前准备库位
    R-->>W: 受理: RunOrder=300, RunState=999
    R->>R: 移动至库位准备位
    alt 目标库位超限
        R-->>W: Error2.Bit10=1 转异常处理
    else 任务正常
        R->>R: 到达目标库位拍照位, 触发拍照, 相机检测
        Note over R: 机器人内部完成提前准备(点位/状态)
        R-->>W: 完成: RunState=300 (提前准备完成)
        W->>R: 任务清空: WMS.OrderControl=0
        R-->>W: 清任务: RunState=0, RunOrder=0
    end
    Note over W,R: 注意: PDF中提前准备含拍照环节, 但附录Bit11(相机超时)仅标注"盘点过程中", 提前准备的相机超时报警位未定义
```

---

## 5. 库位盘点任务（400）

全库循环拍照，逐库位更新 ID / NumState：

```mermaid
sequenceDiagram
    autonumber
    participant W as WMS 上位机
    participant R as Buffer 机器人

    W->>R: 下发: WMS.OrderControl=400
    Note over W,R: 注意: 附录定义 CheckNum=盘点库位(单个), 但PDF流程为全库循环盘点, CheckNum作用需确认
    R-->>W: 受理: RunOrder=400, RunState=999

    loop 从1号库位循环至最后一个库位
        R->>R: 到达当前库位拍照位, 触发拍照
        alt 相机检测超时
            R-->>W: Error2.Bit11=1 转异常处理
        else 库位超限
            R-->>W: Error2.Bit12=1 转异常处理
        else 检测完成, Buffer数据更新
            alt 位置OK 且 ID检测OK
                R-->>W: Buffer.ID_1/ID_2=产品码, NumState=1(有料)
            else 位置OK 且 ID检测NG
                R-->>W: Buffer.ID_1=0, ID_2=0, NumState=1(有料但读码失败)
            else 位置NG 且 ID检测OK
                R-->>W: Buffer.ID_1/ID_2=产品码, NumState=0 (该组合业务含义需确认)
            else 位置NG 且 ID检测NG
                R-->>W: Buffer.ID_1=0, ID_2=0, NumState=0(空库位)
            end
        end
        R->>R: 判断是否最后一个库位: 否则 当前库位号+1, 继续循环
    end

    R-->>W: 完成: RunState=400 (盘点任务完成)
    W->>R: 任务清空: WMS.OrderControl=0
    R-->>W: 清任务: RunState=0, RunOrder=0
```

注意：**ID_1 / ID_2 / NumState 是单一寄存器，盘点过程中被逐库位覆盖更新**。WMS 若要留存全库数据，必须在循环执行期间实时高频轮询，一旦错过即漏读。

---

## 6. 转库任务（500，出库+入库）

```mermaid
sequenceDiagram
    autonumber
    participant W as WMS 上位机
    participant R as Buffer 机器人

    W->>R: 下发: WMS.OrderControl=500, WMS.PickNum=出库库位
    Note over W,R: 注意: PDF下发步骤中只有PickNum, 入库目标库位(PutNum)参数缺失, 需确认
    R-->>W: 受理: RunOrder=500, RunState=999
    alt 出库库位超限
        R-->>W: Error2.Bit13=1 转异常处理
    else 入库库位超限
        R-->>W: Error2.Bit14=1 转异常处理
    else 任务正常
        rect rgb(232, 244, 255)
            Note over R: 出库段: 跳转"出库流程-取料类型判断"
            R->>R: 从 PickNum 库位取料 (同出库任务取料段)
        end
        rect rgb(232, 255, 238)
            Note over R: 入库段: 跳转"入库任务-放料类型判断"
            R->>R: 放料至目标库位 (同入库任务放料段)
        end
        R-->>W: 完成: RunState=500 (转库任务完成)
        W->>R: 任务清空: WMS.OrderControl=0
        R-->>W: 清任务: RunState=0, RunOrder=0
    end
```

---

## 7. 库位循环任务（600）

整库物料逐库位后移（当前库位取料 → 下一个库位放料）：

```mermaid
sequenceDiagram
    autonumber
    participant W as WMS 上位机
    participant R as Buffer 机器人

    W->>R: 下发: WMS.OrderControl=600 (无库位参数)
    R-->>W: 受理: RunOrder=600, RunState=999

    loop 从1号库位循环至最后一个库位
        R->>R: 到达当前库位, 拍照取料 (相机检测 + ID/NumState 更新规则同盘点)
        R->>R: 计算目标库位 = 当前库位号 + 1 (特殊规则除外, 规则未定义)
        R->>R: 到达目标库位检测并放料
        R->>R: 判断是否最后一个库位: 否则 继续循环
    end

    R-->>W: 完成: RunState=600 (库位循环任务完成)
    W->>R: 任务清空: WMS.OrderControl=0
    R-->>W: 清任务: RunState=0, RunOrder=0
    Note over W,R: 注意: 整个任务期间无WMS交互点, WMS只能轮询RunState等待, 中途异常走异常处理时序
```

---

## 8. 自动校库任务（700）

```mermaid
sequenceDiagram
    autonumber
    participant W as WMS 上位机
    participant R as Buffer 机器人

    W->>R: 下发: WMS.OrderControl=700 (无参数)
    R-->>W: 受理: RunOrder=700, RunState=999
    R->>R: 到达目标库位拍照位, 触发拍照
    alt MARK 检测OK
        R->>R: Buffer点位数据更新 (校库补偿存于机器人内部, 不经寄存器交互)
    else MARK 检测失败
        R-->>W: Error2.Bit15=1 转异常处理
    else 相机检测超时
        R-->>W: Error2.Bit16=1 转异常处理
    end
    R-->>W: 完成: RunState=700 (自动校库程序完成)
    W->>R: 任务清空: WMS.OrderControl=0
    R-->>W: 清任务: RunState=0, RunOrder=0
```

---

## 9. 异常处理通用时序（ErrMode = 1 / 2 / 3）

所有任务的报警均走此模式（Error1=取料过程，Error2=放料过程，Error3=设备级，Error4 未定义）：

```mermaid
sequenceDiagram
    autonumber
    participant W as WMS 上位机
    participant R as Buffer 机器人

    Note over R: 任务执行中触发报警: Error1~4.BITx=1, RunState保持999, 流程挂起等待
    R-->>W: 报警挂起 (等待WMS写入ErrMode)
    W->>W: 轮询读到报警位, 决策处理方式
    alt ErrMode=1 重试执行
        W->>R: 写入 WMS.ErrMode=1
        R->>R: 跳转回故障点重试 (相机检测/感应器检测/夹爪动作)
        R-->>W: 处理完成: 报警位清零, 任务从故障点继续
    else ErrMode=2 忽略条件继续执行
        W->>R: 写入 WMS.ErrMode=2
        R->>R: 略过当前条件, 继续后续流程
        R-->>W: 处理完成: 报警位清零, 任务继续
    else ErrMode=3 任务终止
        W->>R: 写入 WMS.ErrMode=3
        R->>R: 退出当前库位, ID清空 (Buffer.ID_1=0, ID_2=0)
        R-->>W: 清任务: RunState=0, RunOrder=0 (任务被终止, 无失败终态码)
        W->>R: 任务清空: WMS.OrderControl=0
    end
    Note over W,R: 待确认: 多报警并发时ErrMode的作用对象 / ErrMode与报警位的清零时序 / 重试次数上限 / ErrMode=2允许的报警白名单
```

---

## 附 A. 图中使用的寄存器速查

### WMS → Buffer（控制区）

| 寄存器 | 地址 | 说明 |
|---|---|---|
| WMS.OrderControl | D4000 | 任务指令：0清除/100出库/200入库/300提前准备/400盘点/500转库/600库位循环/700自动校库 |
| WMS.SystemControl | D4001 | 系统指令：启动/复位/暂停/继续（值语义与位语义存在分歧） |
| WMS.ErrMode | D4002 | 异常处理：1重试/2忽略/3终止（与 PickNum 地址冲突，待确认） |
| WMS.PickNum | D4002(?) | 出库库位 |
| WMS.PutNum | D4003 | 入库库位 |
| WMS.ReadyNum | D4004 | 提前准备库位 |
| WMS.CheckNum | D4005 | 盘点库位（作用待确认） |
| WMS.Function | D4006 | 功能屏蔽：bit0相机/bit1夹爪传感器/bit2天车传感器 |

### Buffer → WMS（状态区）

| 寄存器 | 地址 | 说明 |
|---|---|---|
| Buffer.SensorState | D4100 | 感应器状态映射（附录6） |
| Buffer.ClampState | D4101 | 夹爪状态：0无料/1有料 |
| Buffer.RunOrder | D4102 | 当前执行任务码 |
| Buffer.RunState | D4103 | 0空闲/999执行中/100~700对应任务完成 |
| Buffer.ErrModeState | D4104 | 异常处理模式反馈（与 SystemState 地址冲突，待确认） |
| Buffer.SystemState | D4104(?) | bit0急停/bit1报错/bit2暂停/bit3就绪/bit4运行 |
| Buffer.NumState | D4105 | 库位状态（附录缺失定义） |
| Buffer.ID_1 / ID_2 | D4106 / D4107 | 产品二维码（4字节容量，字节序未定义） |
| Buffer.Error1 | D4111 | 取料过程报警 bit0~8 |
| Buffer.Error2 | D4112 | 放料过程报警 bit0~5, bit10~16 |
| Buffer.Error3 | D4113 | 设备级：bit0急停/bit1~2机械手/bit3~7安全门/bit8~11光栅 |
| Buffer.Error4 | D4114 | 未定义 |

## 附 B. 整理过程中确认的存疑点（与机器人方对齐）

1. D4002、D4104 两处地址冲突（大概率是表格漏移位）
2. SystemControl 按位操作（PDF）还是按值操作（附录2）
3. 任务500 下发参数缺 PutNum
4. CheckNum 单库位定义 vs 盘点全库循环流程
5. 盘点"位置NG & ID检测OK"组合的 ID/NumState 写入规则
6. 提前准备任务的相机超时报警位未定义
7. 库位循环任务"+1 特殊规则"内容未定义
8. 异常处理四要素：并发报警对象、ErrMode 清零时机、重试上限、ErrMode=2 白名单
9. 任务终止无失败终态码，WMS 无法区分"完成"与"中止"
