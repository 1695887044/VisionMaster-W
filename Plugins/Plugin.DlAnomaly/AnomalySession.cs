using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;

namespace Plugin.DlAnomaly
{
    /// <summary>异常检测模型的自描述信息：输入、全部输出、按秩识别出的输出契约。</summary>
    public sealed class AnomalyModelInfo
    {
        public required string ModelPath { get; init; }

        public required string InputName { get; init; }

        public required int[] InputDimensions { get; init; }

        /// <summary>模型声明的全部输出名（顺序即声明顺序）。</summary>
        public required string[] OutputNames { get; init; }

        /// <summary>全部输出的形状（与 <see cref="OutputNames"/> 同序）。</summary>
        public required int[][] OutputDimensions { get; init; }

        /// <summary>按秩识别出的契约。</summary>
        public required AnomalyOutputContract Contract { get; init; }

        /// <summary>模型元数据里的任务类型（Anomalib 导出通常不带；带了就顺手读）。</summary>
        public string Task { get; init; } = string.Empty;

        public int InputWidth => InputDimensions.Length >= 4 && InputDimensions[3] > 0 ? InputDimensions[3] : 0;

        public int InputHeight => InputDimensions.Length >= 4 && InputDimensions[2] > 0 ? InputDimensions[2] : 0;

        public bool HasStaticInputShape => InputWidth > 0 && InputHeight > 0;

        /// <summary>模型路径 + 输入/输出形状 + 契约的可读摘要，直接上配置界面。</summary>
        public string Describe()
        {
            string outputs = string.Join("，", OutputNames.Select((name, i) =>
                $"{name}[{string.Join(",", OutputDimensions[i].Select(d => d < 0 ? "N" : d.ToString()))}]"));
            string input = string.Join(",", InputDimensions.Select(d => d < 0 ? "N" : d.ToString()));
            return $"输入 {InputName}[{input}]；输出 {outputs}；契约 {Contract.Describe()}";
        }

        /// <summary>
        /// 从会话读元数据并嗅探契约。**契约不认就抛**（调用方转成 error），不构造半可用对象。
        /// </summary>
        public static AnomalyModelInfo FromSession(InferenceSession session, string modelPath)
        {
            KeyValuePair<string, NodeMetadata> input = session.InputMetadata.First();

            string[] outputNames = session.OutputMetadata.Keys.ToArray();
            int[][] outputDims = session.OutputMetadata.Values
                .Select(v => v.Dimensions?.ToArray() ?? Array.Empty<int>())
                .ToArray();

            // NodeMetadata 不带输出名，名字只存在于字典的 Key —— 必须成对传进去，
            // 否则只能自己编名字，加载期认出的契约名会和 Run() 返回的名字对不上（静默取不到值）。
            var outputPairs = session.OutputMetadata
                .Select(kv => new KeyValuePair<string, NodeMetadata>(kv.Key, kv.Value))
                .ToArray();
            if (!AnomalyPostProcess.TryResolveContract(outputPairs, out AnomalyOutputContract? contract, out string error))
            {
                throw new InvalidOperationException(error);
            }

            string task = string.Empty;
            var meta = session.ModelMetadata?.CustomMetadataMap;
            if (meta != null && meta.TryGetValue("task", out string? taskText))
            {
                task = taskText?.Trim() ?? string.Empty;
            }

            return new AnomalyModelInfo
            {
                ModelPath = modelPath,
                InputName = input.Key,
                InputDimensions = input.Value.Dimensions?.ToArray() ?? Array.Empty<int>(),
                OutputNames = outputNames,
                OutputDimensions = outputDims,
                Contract = contract!,
                Task = task,
            };
        }
    }

    /// <summary>
    /// 一次加载好的异常检测 ONNX 推理会话，**进程级共享 + 引用计数**。
    ///
    /// 【为什么与 DlClassify / Yolo 用同一套策略】插件实例是"每次编译一份"，
    /// 会话缓存在实例字段里会让同一个模型被 N 个流程加载 N 份（CPU 版每份几十 MB）。
    /// 【为什么引用计数而不是单例】流程会被删、图纸会重编译，会话不能活到进程结束。
    /// 【为什么不与 DlClassify 共用一份实现】方案书 §5.1：各插件自持会话实现是刻意的重复，
    /// 不改已验证的 Yolo/DlClassify；抽公共库属契约级改动，要走架构评审（§5.2）。
    /// </summary>
    public sealed class AnomalySession : IDisposable
    {
        private static readonly Dictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object CacheLock = new();

        private sealed class Entry
        {
            public required InferenceSession Session { get; init; }
            public required AnomalyModelInfo Info { get; init; }
            public int RefCount;
        }

        private readonly Entry _entry;
        private bool _disposed;

        public AnomalyModelInfo Info => _entry.Info;

        /// <summary>底层会话。调用方只管 Run，不要释放它 —— 生命周期归本类管。</summary>
        public InferenceSession Session => _entry.Session;

        private AnomalySession(Entry entry) => _entry = entry;

        /// <summary>获取（或首次加载）指定模型的会话，引用计数 +1。失败时 error 说明原因，不抛异常。</summary>
        public static bool TryAcquire(string? modelPath, out AnomalySession? session, out string error)
        {
            session = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(modelPath))
            {
                error = "尚未指定模型文件：请在节点配置里选择一个 .onnx 异常检测模型";
                return false;
            }

            if (!File.Exists(modelPath))
            {
                error = $"模型文件不存在：{modelPath}。请重新选择，或把模型放到该路径";
                return false;
            }

            if (!Environment.Is64BitProcess)
            {
                error = "当前进程是 32 位，而 ONNX Runtime 原生库只有 x64 版本。请把宿主程序改为 64 位（x64）后重试";
                return false;
            }

            string key = Path.GetFullPath(modelPath);

            lock (CacheLock)
            {
                if (Cache.TryGetValue(key, out Entry? existing))
                {
                    existing.RefCount++;
                    session = new AnomalySession(existing);
                    return true;
                }

                try
                {
                    using var options = new SessionOptions
                    {
                        // 单线程执行的确定性问题：同一张图要拿到同样的分数。
                        IntraOpNumThreads = 1,
                        InterOpNumThreads = 1,
                        // 压到 ERROR：ORT 默认会刷大量内部提醒，把界面日志淹没。
                        LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
                    };

                    var onnxSession = new InferenceSession(key, options);
                    try
                    {
                        AnomalyModelInfo info = AnomalyModelInfo.FromSession(onnxSession, key);

                        var entry = new Entry { Session = onnxSession, Info = info, RefCount = 1 };
                        Cache[key] = entry;
                        session = new AnomalySession(entry);
                        return true;
                    }
                    catch
                    {
                        // 建会话成功但读元数据失败：未入缓存，必须就地释放，否则原生句柄泄漏。
                        onnxSession.Dispose();
                        throw;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    // 契约不认：把「读到什么 + 为什么不认」原样交给用户，不加工成泛泛的"加载失败"。
                    error = ex.Message;
                    return false;
                }
                catch (Exception ex)
                {
                    error = $"加载 ONNX 模型失败：{ex.Message}。"
                          + "请确认：① 文件是合法的 .onnx 模型；② onnxruntime.dll 已随插件投递到宿主 exe 目录（32 位进程无法加载）";
                    return false;
                }
            }
        }

        /// <summary>当前缓存里的会话数（供断言与排障）。</summary>
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
}
