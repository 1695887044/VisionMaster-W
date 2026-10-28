# 读码插件码框标注（MeasureImage，2026-10-27）

> **已被 `2026-10-27-读码码框改为覆盖层（方案1）.md` 取代**：`MeasureImage` 已移除，
> 改为插件输出 `SymbolContours`、`结果显示` 的 `OverlayContours` 走覆盖层。
> 本文保留为中间方案记录，最终实现以新文档为准。

## 一、当前状态

- `Plugin.CodeReader` 新增输出端口 `MeasureImage`（`HImage`）：
  原图 + 识别到的码轮廓（3 通道画绿色、单通道画白色）。
- `码读取_Code128` / `二维码读取_QR` 两条流程的「结果显示」`SrcImage`
  已从采集原图改接 `Code128读码.MeasureImage` / `QR码读取.MeasureImage`；
  图像窗口能直接看到码被框在哪。
- 插件构建 0 错误；`audit_artifacts.ps1` 40 个插件工程 OK；`HelpProbe` exit 0。
- 临时探针实测：
  - `qr_workpiece_01.png` → `codes=[12345678901234567890]`，`symbols=有`，`measure=有`，`maxDiff=255`；
  - `code12801.png` → `codes=[MVTec Software GmbH]`，`maxDiff=221`。
- `VmsCompileProbe` 重编译后跑 `解决方案\视觉样例对拍\视觉样例对拍.vms`：5 条流程全部 `[OK]`，`RESULT: OK`。

## 二、已做

1. `BarCodeEngine.TryDecode` 新增带 `out HObject? symbolContours` 的重载：
   `find_bar_code` 返回的是 Region，用 `GenContourRegionXld(..., "border")` 转成 XLD 再交给调用方；
   旧重载保留，调用后自行释放。
2. `DataCodeEngine.TryDecode` 同样新增重载：`find_data_code_2d` 的 `SymbolXLDs` 直接转交，
   旧重载保留并释放。
3. `CodeReaderPlugin`：
   - 新增 `MeasureImage` 输出端口；
   - `TryRead` 增加 `out HObject? symbols`；
   - `RunAlgorithm` 用 `BuildMeasureImage` 把轮廓画到原图副本上；
   - `BuildMeasureImage` 按通道数选颜色（1 通道 255、3 通道 0/255/0、4 通道 0/255/0/255），
     没码时返回原图副本，画框失败时返回 null（不把读码结果带崩）。
4. `.vms`：两条读码流程的 `显示检测结果.SrcImage` 改接 `MeasureImage`。
5. `FlowCanvasChecks/CodeReaderChecks.cs`：
   - `CheckResultDisplay` 期望端口改为 `MeasureImage`；
   - 新增 `CheckMeasureImage`：断言 `MeasureImage` 非空，并用 `AbsDiffImage + MinMaxGray`
     证明与输入图有像素差（框真的画上去了，而不是空拷贝）。
6. `Plugins\Plugin.CodeReader\Help\02-界面与端口.md` 增加 `MeasureImage` 行，
   并把「明确不输出位置」改成「没有 Row/Column 端口，但 MeasureImage 已把码框画在图上」。
7. 重新构建 `Plugin.CodeReader`（`Modules\Plugin.CodeReader.dll` 已刷新）。

## 三、未做

- **没有单独输出码的行/列/角度端口**：`MeasureImage` 只解决“一眼看到在哪”，
  数值定位仍要上游定位节点或脚本。
- 没有逐码文字标签（多个码时框都画，但不逐框标内容）；`ResultDisplay` 的 `Text` 项已显示全部内容。
- **`FlowCanvasChecks` 本轮没编译通过**：不是本次改动导致，而是另一会话正在写的
  `FlowCanvasChecks\DeformableMatchingChecks.cs` 有 9 条 `CS1503`（object→string/HObject），
  以及并发构建的 `RG1000`/`Plugin.HandEye` 锁文件。等对方修好后重跑即可。
- 没有在真机重启 `VisionMaster.exe` 复验（运行中的 exe 仍加载旧 `Plugin.CodeReader.dll`）。

## 四、下一步

1. 重启 `VisionMaster.exe`，打开 `解决方案\视觉样例对拍\视觉样例对拍.vms`，跑
   `二维码读取_QR`：图像窗口应显示二维码照片 + 码外框 + 「码个数/码内容」。
2. 等 `DeformableMatchingChecks.cs` 编译通过后，重跑 `FlowCanvasChecks`；
   新增的 `CheckMeasureImage` 应出现在通过列表里。
3. 若要逐码标注，可在 `MeasureImage` 上按 `ResultHandles` 顺序给每个框加编号/内容；
   这需要 `AnnotationBaker` 的文本烘焙，另开批次。

## 五、踩过的坑

1. **一维/二维返回的对象类型不同**：`find_bar_code` 返回 Region，`find_data_code_2d`
   返回 XLD；统一转 XLD 后 `PaintXld` 才能同时处理。
2. **`PaintXld` 的 Grayval 是元组**：3 通道要传 `(0,255,0)`，单通道传 `(255)`；
   传字符串颜色不会按预期上色。
3. **`MeasureImage` 失败要清空**：`RunAlgorithm` 开头置 `null`，无码时返回原图副本；
   不要沿用上一帧的标注图（否则会看到上一张图的框）。
4. **帮助改动也算产物脏**：改了 `02-界面与端口.md` 必须重建插件，
   否则 `Modules` 里的 DLL 仍是旧内嵌资源（R42/R62）。
