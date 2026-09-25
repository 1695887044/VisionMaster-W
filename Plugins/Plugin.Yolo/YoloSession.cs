using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;

namespace Plugin.Yolo
{
    /// <summary>
    /// 一次加载好的 ONNX 推理会话，带**进程级共享 + 引用计数**。
    ///
    /// 【为什么必须是进程级共享，而不是缓存在插件实例字段里】
    /// 插件实例是"每次编译一份"，也就是**每个流程会话一个实例**。若把会话缓存在实例字段里
    /// （像 Plugin.Ocr / Plugin.CodeReader 那样做），同一个模型被 5 个流程引用就会加载 5 份：
    ///   · CPU 版：每个会话几十 MB 内存，5 份就是几百 MB；
    ///   · 将来接 GPU：**显存是按会话占的**，5 份直接把显卡吃光。
    /// OCR/CodeReader 没这个问题（模型小、纯 CPU），YOLO 完全不能照抄那套写法。
    ///
    /// 【为什么用引用计数而不是简单单例】
    /// 流程会被删除、图纸会被重新编译。会话不能"第一次加载就活到进程结束"——那样反复打开
    /// 不同图纸会持续堆积。用引用计数：谁 Acquire 谁 Release，**归零才真正释放**。
    ///
    /// 【线程安全】
    /// InferenceSession.Run 本身是线程安全的，所以多个流程共享同一个会话没问题。
    /// 但本类的缓存字典必须有锁 —— Acquire / Dispose 会来自不同线程（编译线程、UI 线程、HTTP 线程）。
    /// </summary>
    public sealed class YoloSession : IDisposable
    {
        /// <summary>缓存键 = 模型文件全路径（大小写不敏感，Windows 语义）</summary>
        private static readonly Dictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object CacheLock = new();

        private sealed class Entry
        {
            public required InferenceSession Session { get; init; }
            public required YoloModelInfo Info { get; init; }
            public int RefCount;
        }

        private readonly Entry _entry;
        private bool _disposed;

        /// <summary>模型的输入/输出与元数据信息（建模时读一次，之后复用）</summary>
        public YoloModelInfo Info => _entry.Info;

        /// <summary>底层会话。调用方只管 Run，不要释放它 —— 生命周期归本类管</summary>
        public InferenceSession Session => _entry.Session;

        private YoloSession(Entry entry) => _entry = entry;

        /// <summary>
        /// 获取（或首次加载）指定模型的会话，引用计数 +1。失败时 <paramref name="error"/> 说明原因，不抛异常。
        /// </summary>
        public static bool TryAcquire(string? modelPath, out YoloSession? session, out string error)
        {
            session = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(modelPath))
            {
                error = "尚未指定模型文件：请在节点配置里选择一个 .onnx 模型";
                return false;
            }

            if (!File.Exists(modelPath))
            {
                error = $"模型文件不存在：{modelPath}。请重新选择，或把模型放到该路径";
                return false;
            }

            // 原生库是 win-x64 的。32 位进程加载它会抛一句难以理解的 DllNotFoundException，
            // 所以在这里先判掉，给出能直接照做的提示。
            if (!Environment.Is64BitProcess)
            {
                error = "当前进程是 32 位，而 ONNX Runtime 原生库只有 x64 版本。"
                      + "请把宿主程序改为 64 位（x64）后重试";
                return false;
            }

            string key = Path.GetFullPath(modelPath);

            lock (CacheLock)
            {
                if (Cache.TryGetValue(key, out var existing))
                {
                    existing.RefCount++;
                    session = new YoloSession(existing);
                    return true;
                }

                try
                {
                    var options = new SessionOptions
                    {
                        // 单线程执行的确定性问题：工业现场更在意"同一张图给同样的结果"，
                        // 而不是榨干 CPU。线程数做成配置后，这里的默认值取 1。
                        IntraOpNumThreads = 1,
                        InterOpNumThreads = 1,

                        // 【实测踩到】ORT 默认按 WARNING 级别往控制台/日志刷内部消息：
                        // 实测加载一个 60MB 的老模型会刷出十几条 "Initializer xxx appears in graph inputs..."
                        // —— 那是**模型导出质量**的提醒，与我们的用法无关，却会把界面上的真实日志彻底淹没。
                        // 工业软件里日志是排障的唯一线索，不能被这类噪声占据，所以压到 ERROR。
                        LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
                    };

                    var onnxSession = new InferenceSession(key, options);
                    var info = YoloModelInfo.FromSession(onnxSession, key);

                    var entry = new Entry { Session = onnxSession, Info = info, RefCount = 1 };
                    Cache[key] = entry;
                    session = new YoloSession(entry);
                    return true;
                }
                catch (Exception ex)
                {
                    // 常见的两类失败都要能被现场读懂：文件不是合法 ONNX、或原生库缺失
                    error = $"加载 ONNX 模型失败：{ex.Message}。"
                          + "请确认：① 文件是合法的 .onnx 模型；② onnxruntime.dll 已随插件投递到宿主 exe 目录（32 位进程无法加载）";
                    return false;
                }
            }
        }

        /// <summary>当前缓存里的会话数（供断言与排障；正常应等于"正在被引用的不同模型数"）</summary>
        public static int CachedSessionCount
        {
            get { lock (CacheLock) return Cache.Count; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (CacheLock)
            {
                _entry.RefCount--;

                if (_entry.RefCount <= 0)
                {
                    // 归零才真正释放：Dispose 会连带释放原生会话
                    try { _entry.Session.Dispose(); }
                    catch { /* 释放失败不打断整体回收 */ }

                    Cache.Remove(_entry.Info.ModelPath);
                }
            }
        }
    }

    /// <summary>
    /// 模型自描述信息：输入输出名字与形状、类别名、任务类型、输入尺寸。
    ///
    /// 【为什么必须读这些，而不是让用户手填】
    /// 读错布局的后果是"结果全错但不报错"——框会偏、类别会错位，而运行一切正常。
    /// Ultralytics 导出的 ONNX 会把类别名、任务类型、输入尺寸写进模型元数据，
    /// 所以能自动读的就自动读，读不到的才要求手填，并**明确告诉用户哪一项没读到**。
    /// </summary>
    public sealed class YoloModelInfo
    {
        public required string ModelPath { get; init; }
        public required string InputName { get; init; }
        public required int[] InputDimensions { get; init; }
        public required string OutputName { get; init; }
        public required int[] OutputDimensions { get; init; }

        /// <summary>模型元数据里的类别名（Ultralytics 导出会带）；没有则为空数组</summary>
        public required string[] ClassNames { get; init; }

        /// <summary>模型元数据里的任务类型（detect / segment / pose / obb / classify）</summary>
        public string Task { get; init; } = string.Empty;

        /// <summary>输入尺寸（边长）。形状里取不到动态维度时为 0，由调用方用 letterbox 目标尺寸兜底</summary>
        public int InputWidth => InputDimensions.Length >= 4 ? InputDimensions[3] : 0;

        public int InputHeight => InputDimensions.Length >= 4 ? InputDimensions[2] : 0;

        public bool HasStaticInputShape => InputWidth > 0 && InputHeight > 0;

        public static YoloModelInfo FromSession(InferenceSession session, string modelPath)
        {
            var input = session.InputMetadata.First();
            var output = session.OutputMetadata.First();

            var meta = session.ModelMetadata?.CustomMetadataMap;
            string[] classNames = Array.Empty<string>();
            string task = string.Empty;

            if (meta != null)
            {
                if (meta.TryGetValue("names", out var namesText))
                    classNames = ParseNames(namesText);

                if (meta.TryGetValue("task", out var taskText))
                    task = taskText?.Trim() ?? string.Empty;
            }

            return new YoloModelInfo
            {
                ModelPath = modelPath,
                InputName = input.Key,
                InputDimensions = input.Value.Dimensions?.ToArray() ?? Array.Empty<int>(),
                OutputName = output.Key,
                OutputDimensions = output.Value.Dimensions?.ToArray() ?? Array.Empty<int>(),
                ClassNames = classNames,
                Task = task,
            };
        }

        /// <summary>
        /// 解析 Ultralytics 写进元数据的 names 字段。
        ///
        /// 【为什么不能直接当 JSON 解析】实测该字段是 Python 风格的字典字面量，
        /// 形如 <c>{0: 'person', 1: 'bicycle'}</c> —— 键没有引号，不是合法 JSON。
        /// 硬套 JSON 解析器会抛异常，然后表现为"读不到类别名"，
        /// 让人误以为模型元数据缺失。所以按 '{' '}' 与 ',' 手动切。
        /// </summary>
        internal static string[] ParseNames(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();

            var result = new List<string>();
            var text = raw!.Trim().Trim('{', '}');

            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = part.IndexOf(':');
                if (colon < 0) continue;

                string name = part[(colon + 1)..].Trim().Trim('\'', '"');
                if (name.Length > 0) result.Add(name);
            }

            return result.ToArray();
        }

        /// <summary>形状的可读描述，用于日志与错误信息</summary>
        public string DescribeShapes() =>
            $"输入 {InputName}{Format(InputDimensions)}，输出 {OutputName}{Format(OutputDimensions)}";

        private static string Format(int[] dims) =>
            dims.Length == 0 ? "[]" : "[" + string.Join(",", dims.Select(d => d < 0 ? "N" : d.ToString())) + "]";
    }
}
