using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;

namespace Plugin.DlClassify
{
    /// <summary>
    /// 一次加载好的分类 ONNX 推理会话，带**进程级共享 + 引用计数**。
    ///
    /// 【为什么与 Plugin.Yolo 用同一套会话策略，而不是像 OCR 那样缓存在实例字段里】
    /// 插件实例是"每次编译一份"，也就是每个流程会话一个实例。若把会话缓存在实例字段里，
    /// 同一个模型被 5 个流程引用就会加载 5 份：CPU 版每份几十 MB；将来接 GPU 时
    /// **显存是按会话占的**，5 份直接把显卡吃光。所以会话必须进程级共享。
    ///
    /// 【为什么用引用计数而不是简单单例】流程会被删除、图纸会被重新编译。会话不能
    /// "第一次加载就活到进程结束"——那样反复打开不同图纸会持续堆积。用引用计数：
    /// 谁 Acquire 谁 Release，**归零才真正释放**。
    ///
    /// 【线程安全】InferenceSession.Run 本身是线程安全的；但缓存字典必须有锁 ——
    /// Acquire / Dispose 会来自不同线程（编译线程、UI 线程、HTTP 线程）。
    ///
    /// 【与 Plugin.Yolo 的关系】策略同源，实现各自独立（插件之间不互相引用）。
    /// 待第三个深度学习插件落地时，可把这份会话抽成共享库（见 docs/深度学习 方案说明书）。
    /// </summary>
    public sealed class ClassifySession : IDisposable
    {
        /// <summary>缓存键 = 模型文件全路径（大小写不敏感，Windows 语义）</summary>
        private static readonly Dictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object CacheLock = new();

        private sealed class Entry
        {
            public required InferenceSession Session { get; init; }
            public required ClassifyModelInfo Info { get; init; }
            public int RefCount;
        }

        private readonly Entry _entry;
        private bool _disposed;

        public ClassifyModelInfo Info => _entry.Info;

        /// <summary>底层会话。调用方只管 Run，不要释放它 —— 生命周期归本类管</summary>
        public InferenceSession Session => _entry.Session;

        private ClassifySession(Entry entry) => _entry = entry;

        /// <summary>获取（或首次加载）指定模型的会话，引用计数 +1。失败时 error 说明原因，不抛异常。</summary>
        public static bool TryAcquire(string? modelPath, out ClassifySession? session, out string error)
        {
            session = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(modelPath))
            {
                error = "尚未指定模型文件：请在节点配置里选择一个 .onnx 分类模型";
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
                error = "当前进程是 32 位，而 ONNX Runtime 原生库只有 x64 版本。请把宿主程序改为 64 位（x64）后重试";
                return false;
            }

            string key = Path.GetFullPath(modelPath);

            lock (CacheLock)
            {
                if (Cache.TryGetValue(key, out var existing))
                {
                    existing.RefCount++;
                    session = new ClassifySession(existing);
                    return true;
                }

                try
                {
                    using var options = new SessionOptions
                    {
                        // 单线程执行的确定性问题：工业现场更在意"同一张图给同样的结果"，而不是榨干 CPU。
                        IntraOpNumThreads = 1,
                        InterOpNumThreads = 1,

                        // ORT 默认按 WARNING 级别往日志刷内部消息（模型导出质量的提醒），
                        // 会把界面上的真实日志淹没。工业软件里日志是排障的唯一线索，压到 ERROR。
                        LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
                    };

                    var onnxSession = new InferenceSession(key, options);
                    try
                    {
                        var info = ClassifyModelInfo.FromSession(onnxSession, key);

                        var entry = new Entry { Session = onnxSession, Info = info, RefCount = 1 };
                        Cache[key] = entry;
                        session = new ClassifySession(entry);
                        return true;
                    }
                    catch
                    {
                        // 建会话成功、但读元数据/建 Entry 失败：会话未入缓存，必须就地释放，否则原生句柄泄漏
                        onnxSession.Dispose();
                        throw;
                    }
                }
                catch (Exception ex)
                {
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
    /// 【为什么必须读这些，而不是让用户手填】读错布局的后果是"结果全错但不报错"——
    /// 类别会错位、置信度无意义，而运行一切正常。Ultralytics 导出的 ONNX 会把类别名、
    /// 任务类型、输入尺寸写进模型元数据，能自动读的就自动读，读不到的才要求手填。
    /// </summary>
    public sealed class ClassifyModelInfo
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

        /// <summary>输入尺寸（边长）。形状里取不到动态维度时为 0，由调用方兜底</summary>
        public int InputWidth => InputDimensions.Length >= 4 ? InputDimensions[3] : 0;

        public int InputHeight => InputDimensions.Length >= 4 ? InputDimensions[2] : 0;

        public bool HasStaticInputShape => InputWidth > 0 && InputHeight > 0;

        public static ClassifyModelInfo FromSession(InferenceSession session, string modelPath)
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

            return new ClassifyModelInfo
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
        /// 【为什么不能直接当 JSON 解析】实测该字段是 Python 风格的字典字面量，
        /// 形如 <c>{0: 'cat', 1: 'dog'}</c> —— 键没有引号，不是合法 JSON。
        /// 硬套 JSON 解析器会抛异常，然后表现为"读不到类别名"，让人误以为模型元数据缺失。
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
