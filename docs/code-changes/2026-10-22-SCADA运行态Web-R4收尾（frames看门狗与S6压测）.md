# SCADA 运行态 Web · R4 收尾：frames 看门狗 + S6 控制通道压测

- 日期：2026-10-22
- 分支：`feature/scada-web-v4-a-prime`
- 前序：R4-1（设计评审）、R4-2（实现后复核）、第二批（`d1c7e26`）

## 1. `scope=frames` 从死协议面接上触发器

R4-2 指出：服务端支持 `view.resync{scope:"frames"}`，但客户端从不发，`lastReceivedFrameSeq`
只写不读。本批补上值帧停滞看门狗：

- `applyFrame` 在**所有 early return 之前**更新 `state.lastFrameMessageAt`（空帧也算）；
- `startFrameWatch()` 每 5 秒检查一次：运行中、有页面、WS open、非后台标签页、
  已有过值帧、连续 5 秒没收到任何 frame → `requestResync('frame-stall','frames')`；
- 服务端只置 `ForceKeyframe`，下一帧 keyframe；**不整份重发结构**（值帧缺口是背压/连接问题，
  不是结构丢失——这是 R4-1 明确否决“frameSeq 跳变触发结构 resync”的原因）。

效果：`scope=frames` 不再是死协议面；正常页面不会触发（浏览器实测 0 次 resync）。

## 2. S6 控制通道压测（16 端 × 3000 图元 × 60s）

同 S1-10% 参数重跑，验证控制通道没有拖垮吞吐/ack：

| 指标 | 控制通道前 | 控制通道后 |
| --- | --- | --- |
| 帧率 | 10.077Hz | **10.064Hz** |
| errors | 0 | **0** |
| delta p95 | 10,154B | **10,154B**（预算 46,484B） |
| ack p95 / max | 90 / 194ms | **117 / 226ms** |
| 首屏 p95 | 1,199ms | **1,440ms** |
| 均值 / 峰值 | 21.4 / 40.6Mbps | 13.8 / 27.9Mbps |

结论：**ack 队头仍在 200ms 内**，帧率/预算不变，`pass=true`。

## 3. 自主决策：分片暂不落地（有实测依据）

R4-1 裁决把分片排在控制通道/resync 之后，并要求先量 ack 队头。S6 的 117ms 说明：

1. 当前规模下 ack 不是瓶颈，分片对“控制消息队头”的收益不可测；
2. 分片不减少字节、不减少一次 `JSON.parse`（R3 已记），不是大页面容量方案；
3. 架构反编译显示 Watson `MaxMessageSize` 只判入站，出站 4MB 上限未被证实——
   分片的立项理由只剩 LOH/可中断性，收益不足以承担客户端组装状态机 + 兼容风险；
4. 3.5MB 硬闸 + 3000 图元实测 1.5-1.8MB 仍安全。

**因此本批不做分片**，保留 3.5MB 硬闸与 R4-1 已冻结的 `struct.begin/chunk/end/abort` 设计；
触发条件是“页面规模或慢链路实测让 ack/首屏触顶”，届时按 R4-1 §二-8/9/10 落地。

## 4. 验证

- `ScadaWebChecks`：**143 PASS / 0 FAIL**（含 B58-B65、M2-17）。
- 浏览器正常页：61 图元、0 占位盒、`structureSeq=1`、**0 次 resync**、`warned` 空。
- 浏览器超大页：1009 终态遮罩，`attempt=0`、无自动重连。
- S6：`docs/performance/evidence/S6-control-channel/`，`pass=true`。

## 5. 仍未覆盖（明确记录）

- 控制插队的真 WS 断言（慢读时 ack 先于大消息）：S6 的 ack p95 是行为证据，不是顺序断言。
- 分片（本批决定不做，见 §3）。
- 真实网卡/交换机/WiFi、16 端 × 3000 图元浏览器绘制、16 端 120 秒长稳、WPF/Web 截图对照。
