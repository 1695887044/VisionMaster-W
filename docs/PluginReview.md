
---

<a name="细粒度审查"></a>
## 7. 细粒度审查（关键文件提炼）

> 下面以部分关键插件中最常修改的文件为例，列出典型问题点与改进方案，供后续逐文件彻底化检查时参考。

### 7.1 Plugin.ColorCheck/ColorCheck.cs

| 行号 | 问题 | 影响 | 改进建议 |
|------|------|------|-----------|
| 42 | 未捕获 `ArgumentException`，导致无效阈值抛出程序崩溃 | 用户自定义阈值时易致崩 | 在阈值赋值前加入范围校验；异常捕获后给出友好提示 |
| 87 | 使用魔法值 `thresh = 0.85` 未解释 | 可维护性差，调参困难 | 将阈值抽取为 `const double DefaultThreshold = 0.85;` 并在配置文件中映射 |
| 112 | 缺少 XML 注释说明 `GetColorRegion` 行为 | 代码可读性差 | 为公共方法添加 `<summary>` 与 `<param>` 注释 |

### 7.2 Plugin.Matching/MatchEngine.cs

| 行号 | 问题 | 影响 | 改进建议 |
|------|------|------|-----------|
| 64 | 共享列表 `parallelResults` 未加锁 | 线程安全缺陷 | 对列表操作包装 `lock(parallelResults)` 或使用 `ConcurrentBag` |
| 135 | `foreach (var item in sources)` 无异常处理 | 任何异常直接退出 | 包含 `try/catch` 并记录错误来源 |
| 182 | 注释 `// TODO: 支持 GPU 加速` | 代码缺陷未记录 | 添加 Issue 追踪链接或已完成实现 |

### 7.3 Plugin.Camera.Hikvision/CameraStreamHandler.cs

| 行号 | 问题 | 影响 | 改进建议 |
|------|------|------|-----------|
| 28 | `socket.Connect()` 直接使用 IP/Port 字符串，未统一证书验证 | 安全性不足 | 在连接前验证 SSL 证书或使用配置参数传递密钥 |
| 99 | `Received += OnFrameReceived` 未处理取消订阅 | 资源泄露，UI 崩溃 | 在对象 Dispose 时显式 `Received -= OnFrameReceived` |
| 147 | 日志级别固定为 Debug | 性能低下 | 按配置切换为 Info/Warning |

### 7.4 Plugin.Ocr/OCRProcessor.cs

| 行号 | 问题 | 影响 | 改进建议 |
|------|------|------|-----------|
| 48 | 对输入图片尺寸无检查，超大尺寸导致内存爆炸 | 稳定性低 | 在方法入口判断 `image.Width * image.Height > MaxAllowedPixels` 并裁剪 |
| 76 | `ConfigurePaddleOCR` 未检查 `modelsPath` 是否存在 | 运行时错误 | 加入文件系统检查并返回可读错误码 |
| 108 | 结果列表 `List<string>` 直接返回，未提供透明度信息 | 业务功能不完整 | 在结果中加入置信度/置信区间 |

> 以上仅为示例，完整检查请在代码编辑器中逐文件打开运行 `linters` 或手工逐行审阅。若需对其它插件目录进行同类细致排查，请告知，我将继续扩展。