using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;

namespace Plugin.DlSegment
{
    /// <summary>分割模型加载后拿到的全部信息。</summary>
    public sealed class SegmentModelInfo
    {
        public required string ModelPath { get; init; }

        /// <summary>输入名（Run 时构造 NamedOnnxValue 要用）。</summary>
        public required string InputName { get; init; }

        /// <summary>输入维度（可能含 -1 动态维）。</summary>
        public required int[] InputDimensions { get; init; }

        /// <summary>输出名（契约认下的那一路）。</summary>
        public required string OutputName { get; init; }

        /// <summary>输出维度（原始，调试用）。</summary>
        public required int[] OutputDimensions { get; init; }

        public required SegmentOutputContract Contract { get; init; }

        /// <summary>模型元数据声明的任务（可能为空）。</summary>
        public string? Task { get; init; }

        /// <summary>输入宽度（静态形状时有效，否则 0）。</summary>
        public int InputWidth => InputDimensions.Length >= 4 ? InputDimensions[3] : 0;

        /// <summary>输入高度（静态形状时有效，否则 0）。</summary>
        public int InputHeight => InputDimensions.Length >= 4 ? InputDimensions[2] : 0;

        public bool HasStaticInputShape =>
            InputDimensions.Length == 4 && InputDimensions[2] > 0 && InputDimensions[3] > 0;

        public string Describe() =>
            $"输入 {InputName} [{string.Join(",", InputDimensions)}]；输出 {OutputName} [{string.Join(",", OutputDimensions)}]；" +
            Contract.Describe() + (string.IsNullOrWhiteSpace(Task) ? "" : $"；task={Task}");

        /// <summary>从已建立的会话读元数据。契约不认时抛 InvalidOperationException（调用方负责析构会话）。</summary>
        public static SegmentModelInfo FromSession(InferenceSession session, string modelPath)
        {
            IReadOnlyList<string> inputNames = session.InputMetadata.Keys.ToArray();
            if (inputNames.Count == 0) throw new InvalidOperationException("模型没有输入。");
            if (inputNames.Count > 1)
            {
                throw new InvalidOperationException(
                    $"模型有 {inputNames.Count} 个输入（{string.Join(",", inputNames)}），本插件只支持单输入分割模型。");
            }

            string inputName = inputNames[0];
            NodeMetadata inputMeta = session.InputMetadata[inputName];
            int[] inputDims = inputMeta.Dimensions?.ToArray() ?? Array.Empty<int>();
            if (inputDims.Length != 4)
            {
                throw new InvalidOperationException(
                    $"输入 {inputName} 是 {inputDims.Length} 维 [{string.Join(",", inputDims)}]，" +
                    "本插件只支持 4 维 NCHW 图像输入。");
            }

            var outputPairs = session.OutputMetadata
                .Select(kv => new KeyValuePair<string, NodeMetadata>(kv.Key, kv.Value))
                .ToArray();

            if (!SegmentPostProcess.TryResolveContract(outputPairs, out SegmentOutputContract? contract, out string error))
            {
                throw new InvalidOperationException(error);
            }

            string outputName = string.Empty;
            int[] outputDims = Array.Empty<int>();
            foreach (KeyValuePair<string, NodeMetadata> pair in outputPairs)
            {
                int[] dims = pair.Value?.Dimensions?.ToArray() ?? Array.Empty<int>();
                if (!SegmentPostProcess.TryReadFeatureDims(dims, out _, out _, out _)) continue;
                outputName = pair.Key;
                outputDims = dims;
                break;
            }

            session.ModelMetadata.CustomMetadataMap.TryGetValue("task", out string? task);
            session.ModelMetadata.CustomMetadataMap.TryGetValue("names", out string? names);

            return new SegmentModelInfo
            {
                ModelPath = modelPath,
                InputName = inputName,
                InputDimensions = inputDims,
                OutputName = outputName,
                OutputDimensions = outputDims,
                Contract = new SegmentOutputContract
                {
                    ClassCount = contract!.ClassCount,
                    Height = contract.Height,
                    Width = contract.Width,
                    ClassNames = SegmentPostProcess.ParseClassNames(names, contract.ClassCount)
                },
                Task = task
            };
        }
    }

    /// <summary>
    /// 分割会话：进程级共享 + 引用计数。
    ///
    /// 【判据】同一个模型文件只建一个 InferenceSession；引用计数归零才 Dispose。
    /// 理由：ONNX 会话初始化要几百毫秒到数秒，流程里每个周期都重建会让节拍不可接受；
    /// 但也不能永不释放（用户换模型后旧会话会一直占着显存/内存）。
    /// </summary>
    public sealed class SegmentSession : IDisposable
    {
        private sealed class Entry
        {
            public required InferenceSession Session { get; init; }
            public required SegmentModelInfo Info { get; init; }
            public int RefCount { get; set; }
        }

        private static readonly Dictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object CacheLock = new();

        private readonly Entry _entry;
        private bool _disposed;

        public SegmentModelInfo Info => _entry.Info;

        public InferenceSession Session => _entry.Session;

        /// <summary>当前进程内缓存的会话个数（断言与诊断用）。</summary>
        public static int CachedSessionCount
        {
            get { lock (CacheLock) return Cache.Count; }
        }

        private SegmentSession(Entry entry) => _entry = entry;

        /// <summary>
        /// 取一个会话（可能来自缓存）。失败不抛异常，error 说明原因。
        /// 三条守卫按"最容易误解"的顺序排：没选模型 → 文件不在 → 进程位数不对。
        /// </summary>
        public static bool TryAcquire(string? modelPath, out SegmentSession? session, out string error)
        {
            session = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(modelPath))
            {
                error = "尚未指定模型文件：请在节点配置里选择一个 .onnx 分割模型。";
                return false;
            }

            if (!System.IO.File.Exists(modelPath))
            {
                error = $"模型文件不存在：{modelPath}。请重新选择，或把模型放到该路径。";
                return false;
            }

            if (!Environment.Is64BitProcess)
            {
                error = "当前进程是 32 位，而 ONNX Runtime 原生库只有 x64 版本，无法加载分割模型。";
                return false;
            }

            InferenceSession? createdSession = null;
            SegmentModelInfo? createdInfo = null;

            try
            {
                lock (CacheLock)
                {
                    if (Cache.TryGetValue(modelPath, out Entry? cached))
                    {
                        cached.RefCount++;
                        session = new SegmentSession(cached);
                        return true;
                    }
                }

                var options = new SessionOptions
                {
                    IntraOpNumThreads = 1,
                    InterOpNumThreads = 1,
                    LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
                };

                createdSession = new InferenceSession(modelPath, options);
                createdInfo = SegmentModelInfo.FromSession(createdSession, modelPath);

                string? taskError = null;
                if (!SegmentPostProcess.IsSupportedTask(createdInfo.Task, out taskError))
                {
                    throw new InvalidOperationException(taskError);
                }

                lock (CacheLock)
                {
                    if (Cache.TryGetValue(modelPath, out Entry? raced))
                    {
                        raced.RefCount++;
                        session = new SegmentSession(raced);
                        createdSession.Dispose();
                        createdSession = null;
                        return true;
                    }

                    var entry = new Entry { Session = createdSession, Info = createdInfo, RefCount = 1 };
                    Cache[modelPath] = entry;
                    session = new SegmentSession(entry);
                    createdSession = null;
                    return true;
                }
            }
            catch (InvalidOperationException ex)
            {
                error = ex.Message;
            }
            catch (Exception ex)
            {
                error = $"加载 ONNX 分割模型失败：{ex.Message}。请确认是合法的 .onnx 分割模型，且 onnxruntime.dll 已随插件投递到宿主目录。";
            }
            finally
            {
                // 建会话成功但后续任何一步失败：必须就地释放，否则原生句柄泄漏。
                createdSession?.Dispose();
            }

            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (CacheLock)
            {
                _entry.RefCount--;
                if (_entry.RefCount > 0) return;

                Cache.Remove(_entry.Info.ModelPath);
                _entry.Session.Dispose();
            }
        }
    }
}
