using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HalconDotNet;

namespace Plugin.Ocr
{
    /// <summary>
    /// OCR 识别引擎：持有 MLP 分类器句柄，把"一串排好序的字符区域"识别成文本。
    ///
    /// 与 <see cref="CharSegmenter"/> 的分工
    /// ---------
    ///   分割层负责"把字切开"，本层负责"认清每个字"。
    ///   本层不做任何阈值/形态学 —— 那是分割层的职责，混进来会让"改哪个参数"变得说不清。
    ///
    /// 句柄的生命周期
    /// ---------
    /// <c>read_ocr_class_mlp</c> 得到的句柄是进程级非托管资源，绝不能在每帧调用里重复读
    /// （既慢又必然泄漏）。这里按模型路径缓存，<see cref="Dispose"/> 时统一 <c>clear_ocr_class_mlp</c>。
    ///
    /// 为什么句柄可以安全缓存在字段里、不用每帧加锁保护读取
    /// ---------
    ///   ① 插件实例是"每次编译一份"，即每个流程会话独占一个实例；
    ///   ② 同一个会话的执行被启动信号量串行化（RunSessionAsync / RunSessionOnceAsync 都先抢锁），
    ///      所以同一实例的 RunAlgorithm 不会并发进入。
    ///   Dispose 可能与正在跑的一轮撞车（会话被移除时），所以加载/释放这两条路径仍然上锁 ——
    ///   这是廉价的保险，也把"这个字典不是无保护的"写在代码里。
    /// </summary>
    public sealed class OcrEngine : IDisposable
    {
        /// <summary>插件内置的工业字符分类器（0-9 与 A-Z，带拒识）</summary>
        public const string BuiltInModelName = "Industrial_0-9A-Z_Rej";

        private readonly Dictionary<string, HTuple> _handles = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private bool _disposed;

        /// <summary>一次识别的结果</summary>
        public sealed class Recognition
        {
            public string Text { get; init; } = string.Empty;
            public string[] Classes { get; init; } = Array.Empty<string>();
            public double[] Confidences { get; init; } = Array.Empty<double>();

            public int Count => Classes.Length;

            public double AverageConfidence
            {
                get
                {
                    if (Confidences.Length == 0) return 0;
                    double sum = 0;
                    foreach (var c in Confidences) sum += c;
                    return sum / Confidences.Length;
                }
            }

            /// <summary>
            /// 逐字符明细：序号 + 字符 + 置信度。OCR 出错时，这是唯一能定位问题的信息。
            ///
            /// 拒识显示为 '?'：实测带拒识的 ..._Rej 分类器对认不出的区域**返回空格**
            /// 而不是某个特殊类名（也不是空字符串）。若原样拼进明细就是 "2: (0.98)" 这种
            /// 看不出所以然的东西 —— 一个看不见的字符在日志里等于没写。
            /// </summary>
            public string Detail
            {
                get
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < Classes.Length; i++)
                    {
                        if (i > 0) sb.Append(' ');
                        sb.Append(i + 1).Append(':').Append(Display(Classes[i]))
                          .Append('(').Append(Confidences[i].ToString("0.00")).Append(')');
                    }
                    return sb.ToString();
                }
            }

            /// <summary>被分类器拒识的字符数。现场"认不出来几个字"就靠它</summary>
            public int RejectedCount
            {
                get
                {
                    int n = 0;
                    foreach (var c in Classes)
                        if (IsRejected(c)) n++;
                    return n;
                }
            }

            /// <summary>
            /// 判定"这个字符被拒识了"。
            ///
            /// 【为什么不是判空】实测踩过：明细里出现 "2:(0.99)" 这种什么都看不见的项，
            /// 而 RejectedCount 却是 0 —— 说明 HALCON 返回的既不是 null、空串、也不是空格，
            /// 而是一个"看起来什么都没有"的字符（判空/判空白都漏过去了），
            /// 于是拒识被当成"认出了一个字符"混进结果。
            ///
            /// 改用可靠判据：Industrial_0-9A-Z_Rej 这个分类器的输出只可能是 0-9 与 A-Z，
            /// **所以"输出里没有字母也没有数字"等价于"它没认出这个区域"**。
            /// 这个判据不依赖 HALCON 用哪个占位符表示拒识，比猜字符形状稳。
            /// </summary>
            private static bool IsRejected(string? cls)
            {
                if (string.IsNullOrWhiteSpace(cls)) return true;

                foreach (var c in cls!)
                {
                    if (char.IsLetterOrDigit(c)) return false;
                }
                return true;
            }

            /// <summary>把一个类别名转成可显示的字符：拒识统一显示成 '?'</summary>
            private static string Display(string? cls) =>
                IsRejected(cls) ? "?" : cls!.Trim();
        }

        /// <summary>
        /// 解析模型路径。依次找：显式配置 → 程序目录\Assets\ocr → %HALCONROOT%\ocr → 交给 HALCON 按短名找。
        ///
        /// 【显式配了却不存在时必须报错，不许静默退回内置模型】
        /// 否则用户填了一个中文模型路径、实际却拿内置的 0-9A-Z 分类器在跑，
        /// 现场只会觉得"这 OCR 连中文都认不出来"，而没有任何提示指向真因。
        /// </summary>
        public static bool TryResolveModel(string? configured, out string modelPath, out string error)
        {
            modelPath = string.Empty;
            error = string.Empty;

            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (File.Exists(configured))
                {
                    modelPath = configured;
                    return true;
                }

                error =
                    $"指定的 OCR 模型文件不存在：{configured}。请修正路径，或清空该配置以使用内置模型"
                    + $"（{BuiltInModelName}）。";
                return false;
            }

            string builtIn = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets",
                "ocr",
                BuiltInModelName + ".omc"
            );
            if (File.Exists(builtIn))
            {
                modelPath = builtIn;
                return true;
            }

            string? halconRoot = Environment.GetEnvironmentVariable("HALCONROOT");
            if (!string.IsNullOrWhiteSpace(halconRoot))
            {
                string fromHalcon = Path.Combine(halconRoot!, "ocr", BuiltInModelName);
                if (File.Exists(fromHalcon + ".omc"))
                {
                    modelPath = fromHalcon + ".omc";
                    return true;
                }
                if (File.Exists(fromHalcon))
                {
                    modelPath = fromHalcon;
                    return true;
                }
            }

            // 都没找到也不报死：把短名交回 HALCON，由它自己的搜索路径兜底。
            // 这样"装了 HALCON 但插件资源没投递成功"的机器仍能跑，只在真找不到时才由 read 抛错。
            modelPath = BuiltInModelName;
            return true;
        }

        /// <summary>
        /// 识别已切好的字符。失败时 <paramref name="error"/> 说明原因，不抛异常。
        /// </summary>
        public bool TryRecognize(
            CharSegmenter.SegmentResult? segment,
            string modelPath,
            out Recognition? recognition,
            out string error
        )
        {
            recognition = null;
            error = string.Empty;

            if (segment == null)
            {
                error = "没有可识别的字符（分割阶段未产出结果）";
                return false;
            }

            HTuple handle;
            try
            {
                handle = GetOrLoadHandle(modelPath);
            }
            catch (Exception ex)
            {
                error =
                    $"加载 OCR 模型失败（{modelPath}）：{ex.Message}。"
                    + "请确认模型文件存在且与当前 HALCON 版本兼容";
                return false;
            }

            try
            {
                // 识别调用也在锁内：配置界面的「试算」与流程运行可能并发碰到同一个实例，
                // 而"同一个分类器句柄能否被多线程同时使用"没有实测依据。
                // 锁的粒度无所谓 —— 插件实例是每会话一份，同实例本来就不会有真正的并发。
                lock (_lock)
                {
                    // 输入字符必须已排序：返回 Class 的顺序与输入区域的顺序一一对应，
                    // 没排序就会拼出一串乱序文本，而且不报错。分割层的 sort_region 就是为这条服务的。
                    HOperatorSet.DoOcrMultiClassMlp(
                        segment.Chars,
                        segment.GrayImage,
                        handle,
                        out HTuple classes,
                        out HTuple confidences
                    );

                    int n = classes.Length;
                    var classArray = new string[n];
                    var confidenceArray = new double[n];
                    var sb = new StringBuilder();

                    for (int i = 0; i < n; i++)
                    {
                        classArray[i] = classes[i].S ?? string.Empty;
                        confidenceArray[i] = confidences[i].D;
                        sb.Append(classArray[i]);
                    }

                    recognition = new Recognition
                    {
                        Text = sb.ToString(),
                        Classes = classArray,
                        Confidences = confidenceArray,
                    };
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"字符识别失败：{ex.Message}";
                return false;
            }
        }

        private HTuple GetOrLoadHandle(string modelPath)
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_handles.TryGetValue(modelPath, out var cached))
                    return cached;

                HOperatorSet.ReadOcrClassMlp(new HTuple(modelPath), out HTuple handle);
                _handles[modelPath] = handle;
                return handle;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;

                foreach (var handle in _handles.Values)
                {
                    try { HOperatorSet.ClearOcrClassMlp(handle); }
                    catch { /* 释放失败不打断整体回收 */ }
                }
                _handles.Clear();
            }
        }
    }
}
