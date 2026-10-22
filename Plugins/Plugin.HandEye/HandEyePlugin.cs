using Core.Interfaces;
using Newtonsoft.Json;
using Plugin.HandEye.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Plugin.HandEye
{
    /// <summary>
    /// 手眼标定插件：由成对的「机器人位姿 ↔ 相机位姿」求 6-DOF 刚体变换 X。
    ///
    /// 为什么单独做一个插件而不是并进「标定」
    /// ---------
    /// 「标定」的三种模式（像素当量 / 九点 / 透视 / 网格）解决的都是**平面到平面**的映射，
    /// 输出装得下 6 元或 9 元数组；手眼要的是**三维刚体变换**（4×4），
    /// 数学与数据来源都不是一回事：
    ///   · 数据来源：九点标定要"机械坐标 ↔ 图像点"，手眼要"机器人位姿 ↔ 相机位姿"，
    ///     后者必须由机器人控制器与视觉各自给出 6-DOF，多一维就多一套采集与校验；
    ///   · 求解方式：九点是线性最小二乘（仿射/射影），手眼是 AX = XB 的旋转-平移分解，
    ///     还多出一整套退化判据（姿态激励不足、旋转轴共线、样本过少）。
    /// 硬塞进一个类只会让两套判据互相污染，也让"没做手眼"和"手眼失败"分不开。
    ///
    /// 纪律
    /// ---------
    /// · **数据不足/退化必须明确失败**，不许回退成单位矩阵或上一次的解——
    ///   抓取点错一个常数在单站调试里看不出来，只有换站位才暴露。
    /// · 残差与组数**一律上端口**：残差小不等于解对（实测过"残差 3.6e-8 而旋转差 1 rad"
    ///   的错方程），所以验收必须是"已知 X 的合成往返"，不能只看残差。
    /// · 求解前先做**姿态激励检查**：机器人只平移、或每次都回到同一姿态时，
    ///   旋转根本不可观，这时算出来的"解"是噪声放大后的垃圾。
    /// </summary>
    [Display(
        Name = "手眼标定",
        GroupName = "标定",
        Description = "由成对的机器人位姿与相机位姿求 6-DOF 刚体变换（眼在手上 / 眼在手外），输出 4×4 与旋转/平移残差",
        ShortName = "\uf140"
    )]
    public partial class HandEyePlugin : VisionPluginBase
    {
        // 本插件**没有输入端口**（2026-10-22 改版）：成对数据住在步骤配置的 Samples 列表里。
        // 原来那个 `SamplesPath` 端口（把 CSV 路径接进来）已删除 —— 数据在磁盘上意味着
        // 方案换台机器就断，而且"改一个错数字"要跑出去改文件。CSV 降级为导入/导出功能，
        // 由视图负责选文件，见 HandEyeCsv。

        #region 输出端口

        /// <summary>标定结果（Kind = HandEye；失败时为 null，下游必须自己判空）</summary>
        public OutputPort<CalibrationTransform?> Transform { get; } = new(
            "Transform", "手眼标定结果（4×4 与残差；失败为 null）");

        /// <summary>旋转残差 RMS（度）——判"姿态数据干不干净"</summary>
        public OutputPort<double> RotationRmsDeg { get; } = new(
            "RotationRmsDeg", "旋转残差 RMS（度）");

        /// <summary>平移残差 RMS（毫米）——判"点位数据干不干净"</summary>
        public OutputPort<double> TranslationRmsMm { get; } = new(
            "TranslationRmsMm", "平移残差 RMS（毫米）");

        /// <summary>实际参与求解的成对数据组数（总组数减 1，首组作基准）</summary>
        public OutputPort<int> UsedPairs { get; } = new("UsedPairs", "参与求解的成对数据组数");

        #endregion

        #region 配置项（[StepConfig]：随步骤落盘；纯配置，不进端口）

        /// <summary>装法：决定输出的 4×4 是 T_gripper←camera 还是 T_base←camera</summary>
        [StepConfig, DefaultValue(HandEyePoseKind.EyeInHand)]
        public partial HandEyePoseKind PoseKind { get; set; }

        /// <summary>
        /// 成对数据表：每行一组「机器人位姿 ↔ 相机位姿」（12 个分量，位置 mm / 角度度 / ZYX 欧拉）。
        /// **数据的家在这里**，不是磁盘上的 csv：
        ///   · 方案（.vms）要能独立拷走，路径指向的文件一换机器就断；
        ///   · 现场最常见的操作是"改一个错数字 / 删一行离群 / 补一组"，不该要求用户去 Excel 改完再回来；
        ///   · 采集时逐组加一行本身就是要留的台账。
        /// CSV 是导入/导出的扩展功能（<see cref="HandEyeCsv"/>），不是主入口。
        /// setter 里 `?? new(...)`：反序列化遇到 null 时不许把列表变成 null，
        /// 否则视图绑定与 RunAlgorithm 都要各自判空（CaliperMeasure 同款写法）。
        /// </summary>
        [StepConfig]
        public ObservableCollection<HandEyeSample> Samples
        {
            get => _samples;
            set
            {
                _samples = value ?? new ObservableCollection<HandEyeSample>();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SampleCountHint));
            }
        }

        private ObservableCollection<HandEyeSample> _samples = new();

        /// <summary>概况文案（界面直接绑）：只陈述事实，不给建议</summary>
        [JsonIgnore]
        public string SampleCountHint
        {
            get
            {
                int n = _samples?.Count ?? 0;
                if (n == 0) return "还没有成对数据";
                return n < HandEyeMath.RecommendedPairs
                    ? $"已录入 {n} 组 · 建议 ≥ {HandEyeMath.RecommendedPairs} 组"
                    : $"已录入 {n} 组 · 达到建议组数";
            }
        }

        /// <summary>平移残差上限（毫米）：超了判失败。默认 1.0 —— 抓取类应用的常用量级</summary>
        [StepConfig, DefaultValue(1.0)]
        public partial double MaxTranslationRmsMm { get; set; }

        /// <summary>旋转残差上限（度）：超了判失败。默认 0.5</summary>
        [StepConfig, DefaultValue(0.5)]
        public partial double MaxRotationRmsDeg { get; set; }

        /// <summary>
        /// true（默认）= 少于 <see cref="HandEyeMath.RecommendedPairs"/> 组就判失败；
        /// false = 只按 <see cref="HandEyeMath.MinPairs"/> 的数学下限放行（残差仍会如实输出）。
        /// 默认从严：3 组能解但几乎不可靠，现场拿 3 组去抓会偏。
        /// </summary>
        [StepConfig, DefaultValue(true)]
        public partial bool RequireRecommendedPairs { get; set; }

        #endregion

        #region 执行核心

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 与基类 Execute 的预置保持一致：绕过 Execute 直接调用（断言/试运行）时语义相同
            Success.Value = true;
            ErrorMessage.Value = string.Empty;

            // 开轮清零：double/int 端口不会被基类清，失败时下游会读到上一轮残留。
            // `Value` 的声明类型是非空 object，置空必须写 `null!`（走 setter 的 value==null 分支 → default(T)，
            // 也就是 CalibrationTransform? 的 null），这是本仓既定写法（Plugin.PreProcessing 的 DisposeOldOutput 同款）。
            Transform.Value = null!;
            RotationRmsDeg.Value = 0;
            TranslationRmsMm.Value = 0;
            UsedPairs.Value = 0;

            if (!TryBuildSamples(out List<HandEyeMath.Sample> samples, out string? loadError))
            {
                Fail(loadError!);
                return;
            }

            int need = RequireRecommendedPairs ? HandEyeMath.RecommendedPairs : HandEyeMath.MinPairs;
            if (samples.Count < need)
            {
                Fail(RequireRecommendedPairs
                    ? $"成对数据只有 {samples.Count} 组，少于建议的 {need} 组：3 组能解但不可靠，请补到 6 组以上（或关掉「要求建议组数」自担风险）"
                    : $"成对数据只有 {samples.Count} 组，少于数学下限 {need} 组");
                return;
            }

            if (!HandEyeMath.SolveAXEqualsXB(samples, out Mat4 x, out HandEyeMath.Quality q, out string? solveError))
            {
                Fail($"手眼求解失败：{solveError}");
                return;
            }

            double rotDeg = q.RotationRmsRad * 180.0 / Math.PI;
            RotationRmsDeg.Value = rotDeg;
            TranslationRmsMm.Value = q.TranslationRms;
            UsedPairs.Value = q.UsedPairs;

            if (rotDeg > MaxRotationRmsDeg)
            {
                Fail($"旋转残差 {rotDeg:0.###}° 超过上限 {MaxRotationRmsDeg:0.###}°：姿态数据可能含错点/角度单位不对/欧拉约定不符");
                return;
            }

            if (q.TranslationRms > MaxTranslationRmsMm)
            {
                Fail($"平移残差 {q.TranslationRms:0.###} mm 超过上限 {MaxTranslationRmsMm:0.###} mm：点位数据可能含错点，或机器人未回零");
                return;
            }

            var transform = new CalibrationTransform
            {
                Kind = CalibrationKind.HandEye,
                HandEyePose = PoseKind,
                HandEyeMatrix = x.ToArray(),
                HandEyeRotationRmsRad = q.RotationRmsRad,
                HandEyeTranslationRmsMm = q.TranslationRms,
                HandEyeSampleCount = q.UsedPairs + 1,
                CreatedAtUtc = DateTime.UtcNow,
                SourceTag = "成对数据表"
            };

            Transform.Value = transform;
        }

        #endregion

        #region 成对数据表 → 数学样本

        /// <summary>
        /// 把配置里的 <see cref="Samples"/> 表转成数学层样本。
        ///
        /// 两条判据各自有独立的失败文案（别合并成一句"数据有问题"）：
        ///   · 表是空的 → 用户还没录，"去表格里加行或导入 CSV"能直接指导动作；
        ///   · 某行 12 个值全为 0 → 加了行但没填。这是最容易发生也最难自查的一步：
        ///     不拦的话它会混进求解，报出来的却是"旋转激励不足"，人只会去怀疑姿态数据。
        ///
        /// 先快照再算：表格由界面线程编辑，求解可能不在同一线程；
        /// 直接遍历 `Samples` 会在用户删行的瞬间抛 InvalidOperationException。
        /// </summary>
        private bool TryBuildSamples(
            out List<HandEyeMath.Sample> samples,
            out string? error)
        {
            samples = new List<HandEyeMath.Sample>();
            error = null;

            HandEyeSample[] rows = _samples is null ? Array.Empty<HandEyeSample>() : _samples.ToArray();

            if (rows.Length == 0)
            {
                error = "还没有成对数据：在表格里加一行，或点「导入 CSV」";
                return false;
            }

            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] is null)
                {
                    error = $"成对数据第 {i + 1} 行是空的：删掉这一行再求解";
                    return false;
                }

                if (rows[i].IsBlank)
                {
                    error = $"成对数据第 {i + 1} 行还没填（12 个值全为 0）：填完或删掉这一行再求解";
                    return false;
                }

                samples.Add(rows[i].ToMathSample());
            }

            return true;
        }

        #endregion
    }
}
