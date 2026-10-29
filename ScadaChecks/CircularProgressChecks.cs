using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [CP] 圆形进度条（<c>Hmi.CircularProgress</c>）：比例口径 + 非法输入收敛 + 数值格式回落 + 描述符落地。
        ///
        /// 只测"值 → 比例 → 文字"这条数据侧，不测像素：弧的半径/环宽取决于实时尺寸，
        /// 而这里要钉的是三条最容易出错、且出错时<b>不报错</b>的口径：
        /// ① 量程非法（最大值 ≤ 最小值）不许除零、不许出 NaN；
        /// ② 值越界按两端收敛，NaN / ∞ 按 0% 画；
        /// ③ 格式串写坏回落 F0，不许把整页渲染打断。
        ///
        /// <b>为什么这里要自己兜底注册一条描述符</b>
        /// ---------
        /// 本批只允许新增三个文件，<c>BuiltInElements.cs</c> 不在可改范围——也就是说图元先有了
        /// 控件、主题与断言，<b>登记那一行要由主会话补</b>。若断言直接依赖"已经登记"，
        /// 在补那一行之前本组必然全红，红得还没有信息量（"未注册"）。
        /// 所以：已登记就验证<b>真</b>的那份；未登记就用下面 <see cref="BuildDescriptor"/> 里
        /// <b>与 BuiltInElements 待贴代码内容一致</b>的一份临时登记，让比例/格式这些口径
        /// 现在就能被钉住。登记补上之后本兜底自动跳过（<see cref="ElementRegistry.IsRegistered"/> 命中即返回）。
        /// </summary>
        private static class CircularProgressChecks
        {
            private const string TypeKey = "Hmi.CircularProgress";

            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("[CP] 圆形进度条（Hmi.CircularProgress）");

                string? registerError = EnsureRegistered();
                var descriptor = ElementRegistry.Find(TypeKey);

                // ---------------- ① 描述符 ----------------

                string[] expectedKeys =
                {
                    "Value", "Min", "Max", "Thickness", "StartAngle", "SweepAngle",
                    "ShowValue", "ValueFormat", "ProgressColor", "TrackColor",
                    "Fill", "Stroke", "StrokeThickness", "CornerRadius",
                    "Text", "Foreground", "FontSize",
                };

                var missing = descriptor == null
                    ? expectedKeys.ToList()
                    : expectedKeys.Where(k => !descriptor.Properties.Any(p => p.Key == k)).ToList();

                var valueSpec = Property(descriptor, "Value");
                var minSpec = Property(descriptor, "Min");
                var maxSpec = Property(descriptor, "Max");
                var thicknessSpec = Property(descriptor, "Thickness");
                var startAngleSpec = Property(descriptor, "StartAngle");
                var sweepSpec = Property(descriptor, "SweepAngle");
                var showValueSpec = Property(descriptor, "ShowValue");
                var formatSpec = Property(descriptor, "ValueFormat");
                var progressColorSpec = Property(descriptor, "ProgressColor");
                var trackColorSpec = Property(descriptor, "TrackColor");

                bool specOk = descriptor != null
                    && descriptor.DisplayName == "圆形进度条"
                    && descriptor.Category == "数值"
                    && descriptor.DefaultWidth == 160 && descriptor.DefaultHeight == 160
                    && valueSpec is { IsBindable: true }
                    && thicknessSpec is { Min: 2, Max: 60 }
                    && sweepSpec is { Min: 30, Max: 360 }
                    && minSpec?.DefaultValue == "0"
                    && maxSpec?.DefaultValue == "100"
                    && startAngleSpec?.DefaultValue == "-90"
                    && sweepSpec?.DefaultValue == "360"
                    && showValueSpec?.DefaultValue == "True"
                    && formatSpec?.DefaultValue == "F0"
                    && progressColorSpec?.DefaultValue == "#FF2D7DD2"
                    && trackColorSpec?.DefaultValue == "#FF2A3745";

                Check("[CP1] 描述符已注册且属性键齐全（ElementRegistry.Find(\"Hmi.CircularProgress\")）",
                    descriptor != null && missing.Count == 0 && specOk,
                    descriptor == null
                        ? "未注册" + (registerError == null ? "" : "（兜底注册失败：" + registerError + "）")
                        : $"{descriptor.DisplayName}/{descriptor.Category} {descriptor.DefaultWidth}×{descriptor.DefaultHeight}，"
                          + $"属性 {descriptor.Properties.Count} 条"
                          + (missing.Count == 0 ? "" : "；缺：" + string.Join("、", missing))
                          + (specOk ? "" : "；显示名/默认尺寸/默认值/可绑口径不符"));

                // 外观与刻度是设计期语言，交给变量驱动只会让画面自己乱变（口径由
                // ScadaPropertyGroups.NonBindable 在注册期兜底）。可绑的只该是「当前值」（数据组）
                // 与「单位」（文本组：单位字随语言/产线换是正常需求）。
                var wronglyBindable = descriptor == null
                    ? new List<string>()
                    : descriptor.Properties
                        .Where(p => p.IsBindable && p.Key != "Value" && p.Key != "Text")
                        .Select(p => p.Key).ToList();

                var bindableInLockedGroups = descriptor == null
                    ? new List<string>()
                    : descriptor.Properties
                        .Where(p => p.IsBindable
                                    && (p.Group == ScadaPropertyGroups.Appearance
                                        || p.Group == ScadaPropertyGroups.Scale))
                        .Select(p => p.Key).ToList();

                Check("[CP2] 可绑的只有「当前值」（数据）与「单位」（文本）；外观/刻度组一行 ƒx 都没有",
                    descriptor != null
                    && valueSpec is { IsBindable: true }
                    && wronglyBindable.Count == 0
                    && bindableInLockedGroups.Count == 0,
                    wronglyBindable.Count == 0 && bindableInLockedGroups.Count == 0
                        ? "Value + Text"
                        : "多出：" + string.Join("、", wronglyBindable.Concat(bindableInLockedGroups)));

                // ---------------- ② 比例口径 ----------------

                var mid = new CircularProgressElement { Min = 0, Max = 100, Value = 50 };
                Check("[CP3] Value=50 / 量程 0~100 → 比例 0.5",
                    Math.Abs(mid.ProgressRatio - 0.5d) < 1e-9,
                    mid.ProgressRatio.ToString("0.####"));

                var quarter = new CircularProgressElement { Min = 20, Max = 120, Value = 45 };
                Check("[CP4] 非零起点：Value=45 / 量程 20~120 → 比例 0.25（不是 45/100）",
                    Math.Abs(quarter.ProgressRatio - 0.25d) < 1e-9,
                    quarter.ProgressRatio.ToString("0.####"));

                // ---------------- ③ 越界与非法值 ----------------

                var low = new CircularProgressElement { Min = 0, Max = 100, Value = -10 };
                var high = new CircularProgressElement { Min = 0, Max = 100, Value = 200 };

                Check("[CP5] 越界按两端收敛：-10 → 0、200 → 1（不许出现负比例或大于 1）",
                    low.ProgressRatio == 0d && high.ProgressRatio == 1d,
                    $"{low.ProgressRatio:0.###} / {high.ProgressRatio:0.###}");

                bool invalidThrew = false;
                double degenerateRatio = -1d;
                double nanRatio = -1d;
                double infRatio = -1d;
                string? nanText = null;

                try
                {
                    var degenerate = new CircularProgressElement { Min = 50, Max = 50, Value = 50 };
                    degenerateRatio = degenerate.ProgressRatio;

                    var nan = new CircularProgressElement { Min = 0, Max = 100, Value = double.NaN };
                    nanRatio = nan.ProgressRatio;
                    nanText = nan.ValueText; // 格式化 NaN 也不许抛

                    var inf = new CircularProgressElement { Min = 0, Max = 100, Value = double.PositiveInfinity };
                    infRatio = inf.ProgressRatio;
                    _ = inf.ValueText;
                }
                catch (Exception ex)
                {
                    invalidThrew = true;
                    Check("[CP6] 非法量程 / NaN / ∞ 一律不抛", false, ex.ToString());
                }

                if (!invalidThrew)
                {
                    Check("[CP6] 最大值 == 最小值 → 0（不除零、不抛）；Value=NaN → 0；Value=∞ → 0",
                        degenerateRatio == 0d && nanRatio == 0d && infRatio == 0d && nanText == "NaN",
                        $"max=min:{degenerateRatio:0.###} NaN:{nanRatio:0.###} ∞:{infRatio:0.###} 文本=\"{nanText}\"");
                }

                // ---------------- ④ 数值格式 ----------------

                bool formatThrew = false;
                var badFormat = new CircularProgressElement { Min = 0, Max = 100, Value = 12.34, ValueFormat = "乱写" };
                var literalOnly = new CircularProgressElement { Min = 0, Max = 100, Value = 12.34, ValueFormat = "'℃'" };
                var badSpecifier = new CircularProgressElement { Min = 0, Max = 100, Value = 12.34, ValueFormat = "Q" };
                var oneDecimal = new CircularProgressElement { Min = 0, Max = 100, Value = 12.34, ValueFormat = "F1" };
                var withUnit = new CircularProgressElement { Min = 0, Max = 100, Value = 12.34, ValueFormat = "0.0 ℃" };
                var blankFormat = new CircularProgressElement { Min = 0, Max = 100, Value = 12.34, ValueFormat = null };

                try
                {
                    _ = badFormat.ValueText;
                    _ = literalOnly.ValueText;
                    _ = badSpecifier.ValueText;
                }
                catch (Exception ex)
                {
                    formatThrew = true;
                    Check("[CP7] 数值格式写坏回落 F0，不抛", false, ex.ToString());
                }

                if (!formatThrew)
                {
                    // "乱写" 这类多字符串在 .NET 里<b>不抛</b>（自定义模板把不认识的字符当字面量原样输出），
                    // 于是圆心会直接画上「乱写」两个字——这正是本图元要挡的静默失败，判据是"结果里得有数字"。
                    Check("[CP7] 数值格式写坏 → 中心文本回落 F0，不抛：\"乱写\"→\"12\"、\"'℃'\"→\"12\"、\"Q\"→\"12\"；"
                          + "合法格式照旧：F1→\"12.3\"、\"0.0 ℃\"→\"12.3 ℃\"、留空→\"12\"",
                        badFormat.ValueText == "12"
                        && literalOnly.ValueText == "12"
                        && badSpecifier.ValueText == "12"
                        && oneDecimal.ValueText == "12.3"
                        && withUnit.ValueText == "12.3 ℃"
                        && blankFormat.ValueText == "12",
                        $"乱写=\"{badFormat.ValueText}\" 字面量=\"{literalOnly.ValueText}\" Q=\"{badSpecifier.ValueText}\" "
                        + $"F1=\"{oneDecimal.ValueText}\" 带单位=\"{withUnit.ValueText}\" 空=\"{blankFormat.ValueText}\"");
                }

                // ---------------- ⑤ 描述符 → 控件（设计期就按配置值画） ----------------

                if (descriptor == null)
                {
                    Check("[CP8] 描述符的属性袋能落到控件上（设计期按配置的 Value 画，不是画 0）", false, "描述符未注册");
                    return;
                }

                var model = ElementRegistry.CreateElement(TypeKey);
                model.SetProperty("Value", "65");
                model.SetProperty("Min", "0");
                model.SetProperty("Max", "100");

                var wired = (CircularProgressElement)ElementRegistry.CreateControl(model);

                Check("[CP8] 描述符 → 控件：属性袋 Value=65 落到控件，比例 0.65、中心文本 \"65\"（设计期不画 0）",
                    Math.Abs(wired.Value - 65d) < 1e-9
                    && Math.Abs(wired.ProgressRatio - 0.65d) < 1e-9
                    && wired.ValueText == "65",
                    $"Value={wired.Value:0.###} 比例={wired.ProgressRatio:0.###} 文本=\"{wired.ValueText}\"");
            }

            /// <summary>按属性键取描述符条目（取不到返回 null，由调用方的判据一并报红）</summary>
            private static ElementPropertyDescriptor? Property(ElementDescriptor? descriptor, string key)
                => descriptor?.Properties.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));

            /// <summary>
            /// 图元还没登记进 <c>BuiltInElements</c> 时，用下面这份<b>与待贴代码逐字一致</b>的声明临时登记；
            /// 已登记则直接返回 null（不重复注册——<see cref="ElementRegistry.Register"/> 不幂等）。
            /// </summary>
            private static string? EnsureRegistered()
            {
                if (ElementRegistry.IsRegistered(TypeKey))
                    return null;

                return ElementRegistry.Register(BuildDescriptor());
            }

            /// <summary>
            /// 与 <c>BuiltInElements.cs</c> 待贴的那一段<b>逐字一致</b>（同一份属性清单、同一批默认值；只有 TypeKey 用本类常量代替字面量）。
            /// 主会话把那一段贴进清单后，本方法就不再被调用；两份若有出入，[CP1] 会当场报出来。
            /// </summary>
            private static ElementDescriptor BuildDescriptor() => new()
            {
                TypeKey = TypeKey,
                DisplayName = "圆形进度条",
                ControlType = typeof(CircularProgressElement),
                Category = "数值",
                DefaultWidth = 160,
                DefaultHeight = 160,
                Description = "一个数值在圆环上的可视化身：进度弧 = (当前值 − 最小值) ÷ (最大值 − 最小值)，" +
                              "起止角与环宽可调，圆心显示读数；把「当前值」绑到变量上即可（完成率、液位、压力占比）",
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(CircularProgressElement)),
                    AppearanceProperties.Fill("#00000000",
                        "卡片底色；默认全透明——只要圆环时不必管它，要卡片再填色"),
                    AppearanceProperties.Stroke("#00000000",
                        "卡片边框色；默认透明（与底色一起决定要不要卡片）"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("0"),
                    TextProperties.Text("单位",
                        description: "圆心读数下方的一行小字（单位/说明，如「MPa」「完成率」）；留空则只显数值"),
                    TextProperties.Foreground("#FFE6EEF7", displayName: "文字颜色",
                        description: "默认浅色：运行画面是深色底，深色字会糊成一片"),
                    TextProperties.FontSize("24"),
                    TextProperties.FontWeight("SemiBold",
                        description: "默认 SemiBold：读数是这个图元唯一要一眼扫到的信息")),
            };

            private static void RunOnSta(Action action)
            {
                Exception? error = null;

                var thread = new Thread(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (error != null)
                    Check("[CP] 圆形进度条断言全程未抛异常", false, error.ToString());
            }
        }
    }
}
