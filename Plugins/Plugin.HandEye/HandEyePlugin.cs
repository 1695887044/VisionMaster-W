using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;

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
        #region 输入端口

        /// <summary>
        /// 成对数据文件路径（可选）：接了就用端口值，不接用配置里的 <see cref="SamplesFilePath"/>。
        /// 做成端口是为了让"同一套流程跑不同站位"不必改步骤配置。
        /// </summary>
        public InputPort<string> SamplesPath { get; } = new(
            "SamplesPath",
            "",
            "成对数据文件路径（可选；不接则用步骤配置里的路径）")
        { IsRequired = false };

        #endregion

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
        /// 成对数据文件（CSV，UTF-8）：12 列
        /// robotX,robotY,robotZ,robotRx,robotRy,robotRz,camX,camY,camZ,camRx,camRy,camRz
        /// 位置单位 mm、角度单位度、姿态按 **ZYX 欧拉**；首行可写表头（非数字开头即跳过），
        /// 空行与 `#` 开头的行忽略。
        /// </summary>
        [StepConfig, DefaultValue("")]
        public partial string SamplesFilePath { get; set; }

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

            string path = SamplesPath.ActualValue;
            if (string.IsNullOrWhiteSpace(path))
                path = SamplesFilePath ?? string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                Fail("没有成对数据文件：请填写「成对数据文件」路径，或把上游的路径接到 SamplesPath 端口");
                return;
            }

            if (!File.Exists(path))
            {
                Fail($"成对数据文件不存在：{path}");
                return;
            }

            if (!TryLoadSamples(path, out List<HandEyeMath.Sample> samples, out string? loadError))
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
                SourceTag = Path.GetFileName(path)
            };

            Transform.Value = transform;
        }

        #endregion

        #region CSV 读取

        /// <summary>
        /// 读 12 列 CSV。**逐行给出行号与原始文本**：现场最常见的错是"某一行少一列"
        /// 或"角度单位混用"，只说"格式错误"会让人从头数到尾。
        /// </summary>
        private static bool TryLoadSamples(
            string path,
            out List<HandEyeMath.Sample> samples,
            out string? error)
        {
            samples = new List<HandEyeMath.Sample>();
            error = null;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex)
            {
                error = $"成对数据文件读不出来：{ex.Message}";
                return false;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                string[] cells = line.Split(',');
                if (cells.Length != 12)
                {
                    // 表头（首列不是数字）跳过；其余按错误报
                    if (samples.Count == 0 && i < 3 && !LooksNumeric(cells[0]))
                        continue;

                    error = $"成对数据第 {i + 1} 行有 {cells.Length} 列，应为 12 列：{line}";
                    return false;
                }

                var v = new double[12];
                bool ok = true;
                for (int c = 0; c < 12; c++)
                {
                    if (!double.TryParse(cells[c].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v[c]))
                    {
                        // 表头：首列不是数字
                        if (samples.Count == 0 && c == 0 && i < 3)
                        {
                            ok = false;
                            break;
                        }

                        error = $"成对数据第 {i + 1} 行第 {c + 1} 列不是数字：「{cells[c].Trim()}」";
                        return false;
                    }
                }

                if (!ok)
                    continue;

                var robot = HandEyeMath.PoseFromXyzEulerZyxDeg(v[0], v[1], v[2], v[3], v[4], v[5]);
                var camera = HandEyeMath.PoseFromXyzEulerZyxDeg(v[6], v[7], v[8], v[9], v[10], v[11]);
                samples.Add(new HandEyeMath.Sample(robot, camera));
            }

            if (samples.Count == 0)
            {
                error = "成对数据文件里没有有效数据行（应为 12 列 CSV，空行与 # 开头的行会被忽略）";
                return false;
            }

            return true;
        }

        private static bool LooksNumeric(string text)
            => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        #endregion
    }
}
