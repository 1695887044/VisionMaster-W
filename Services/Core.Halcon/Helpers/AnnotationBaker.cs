using System;
using System.Collections.Generic;
using HalconDotNet;

namespace Core.Halcon.Helpers
{
    /// <summary>
    /// 共享离屏烘焙件：把"图形 + 文字 + HALCON 对象"烧进底图像素，产出一张**全新**的 <see cref="HImage"/>。
    ///
    /// ★ 这是全仓离屏烘焙的**收敛点**。仓库此前有五份同源代码，其中四份已于 2026-10-13 迁入本类，
    ///   禁止再复制新的烘焙件：
    ///   · Plugin.BlobDetect\AnnotationRenderer.cs（已迁：只剩"画哪几层"的 BakePlan 构建）
    ///   · Plugin.CaliperMeasure\AnnotationRenderer.cs（已迁）
    ///   · Plugin.BeadInspect\BeadInspectPlugin.cs（OffscreenRenderer，已迁）
    ///   · Plugin.Matching\MatchingPlugin.cs（RenderAnnotated，已迁）
    ///   · Plugin.ImageScript\HDevDisplayBackend.cs —— **永久例外，不迁**：它不是"一次性把计划烧进底图"，
    ///     而是 HDevEngine 按 dev_* 算子**逐条驱动**的显示后端（窗口状态是 [ThreadStatic] 的、
    ///     还要 HandoutWindow 把窗口交给脚本），"画什么"在跑完之前根本不知道，BakePlan 表达不了。
    /// 迁入前的四份**基础设施逐字相同**（开窗/复用/回读/释放约 90 行），差异只在"画哪几层、什么颜色"——
    /// 那条真实分界就是本类的分工界线：**窗口/回读/释放归这里（共享），"画什么"归调用方的 BakePlan（私有）**。
    ///
    /// ★ 迁移当场抓到的真 bug（不是纯重构）：BlobDetect 与 CaliperMeasure 那两份**没有 SetPart**（违反纪律 3），
    ///   标注底图整片塌成窗口底色——FlowCanvasChecks 实测 `(150,20)=0 (150,180)=0`（期望 100/200）、
    ///   `(470,320)=0`（期望 255）。BlobDetectChecks 里那条"本环境离屏窗口不渲染底图层次"的**环境闸门是误判**，
    ///   迁到本类后同一条断言直接变绿。
    ///
    /// 【为什么必须走离屏窗口，不能用纯算子合成】
    /// HALCON 往图上写字（disp_text）必须依附窗口的字库机制，没有"纯算子写文字"的路子；
    /// paint_region 只能涂色块、画不了字（且它的 Type 只认 "fill"，写 "filled" 直接抛 #1302）。
    ///
    /// 【本类逐条落实的六条硬纪律】（每条都有仓库实测事故，违反任意一条就是事故）
    ///   1. open_window 的 mode **只能 'buffer'**：'invisible'/'visible'/'pixmap' 一律报 #1305。
    ///   2. 窗口**按尺寸常驻复用**，只在尺寸变化或 Dispose 时才关：反复 open/close 会 **#9302 死锁**。
    ///   3. 每轮**无条件** SetPart(win,0,0,h-1,w-1)：否则 part 被撑到参考图全幅 → 底图缩在左上角、
    ///      其余全黑；常驻窗口复用时上一轮的 dev_set_part 还会**串轮污染**，把黑边烙进像素。
    ///      （这条在 docs\code-changes\2026-09-15-…:97 登记在案至今未修，本类把它做成无条件动作）
    ///   4. 文字坐标系**只用 "window"**：实测无显示会话的环境里 buffer 窗口的 image part 不成立，
    ///      "image" 坐标的 disp_text 整段画不出来，"window" 总是可靠。
    ///   5. DispText **显式**给 box + box_color：见 <see cref="DrawTexts"/> 里那条实测冲突的记录。
    ///   6. DumpWindowImage 的源对象**立刻 Dispose**：new HImage(shot) 是独立句柄，但 shot 是源对象，
    ///      HALCON .NET 是引用计数语义，只包装不释放**500 次约漏 160MB**（≈0.32MB/次）。
    /// 外加两条状态纪律：整段 lock 罩住"改窗口状态→画→回读"（窗口状态是全局的，两路绘制会串色）；
    /// SetLineWidth / SetDraw 用完还原（跨渲染保留，不还原就带进下一张）。
    ///
    /// 【失败口径】Bake **永不抛异常**：整体失败返回 null 并回调 warn，单个元素画不出来只丢那一个。
    /// 渲染只是"给人看"，绝不能把一次成功的测量/检测判成失败。
    ///
    /// 【为什么日志是回调而不是 ILogService】Core.Halcon.csproj 全文零 ProjectReference
    /// （它是最底层的 HALCON 封装层），拿不到 Core.Interfaces 的 ILogService；**永远不许为此加引用**
    /// （会把底层库拖进上层依赖网，且触发全仓级联重编）。所以日志出口只能是 <c>Action&lt;string&gt;? warn</c>。
    /// </summary>
    public sealed class AnnotationBaker : IDisposable
    {
        /// <summary>
        /// 允许的 HALCON 色名白名单。绝不把调用方给的字符串直接当色名传下去——
        /// 非法色名会让 SetColor 抛异常，进而（在没有逐元素兜底时）整张标注图丢失。
        /// </summary>
        private static readonly HashSet<string> AllowedColors = new(StringComparer.OrdinalIgnoreCase)
        {
            "green", "red", "orange", "yellow", "cyan", "magenta", "white"
        };

        /// <summary>坐标的" sane 上限"：只用来挡住天文数字喂给 HALCON，真正的图幅钳制由调用方做</summary>
        private const double CoordLimit = 1e6;

        private readonly object _gate = new();
        private readonly string _background;
        private readonly int _defaultFontSize;

        private HTuple? _window;
        private int _width;
        private int _height;
        private bool _disposed;

        /// <param name="background">离屏窗口底色（HALCON 色名）。默认 "black"：绿/红/青/黄/品红在黑底上都读得清，
        /// 白底会吃掉黄与青。底色只在"底图铺不满画布"时才看得见——正常情况下底图会盖满整幅。</param>
        /// <param name="defaultFontSize">文字未指定字号（≤0）时用的字号</param>
        public AnnotationBaker(string background = "black", int defaultFontSize = 20)
        {
            _background = string.IsNullOrWhiteSpace(background) ? "black" : background.Trim();
            _defaultFontSize = defaultFontSize > 0 ? defaultFontSize : 20;
        }

        /// <summary>
        /// 把 <paramref name="plan"/> 烧进 <paramref name="baseImage"/> 的副本，返回**全新**的标注图。
        /// </summary>
        /// <param name="baseImage">底图。**只读**，本方法不释放它（所有权在调用方）。
        /// 通道数/像素类型不限：4 通道、非 byte 单通道都由 <see cref="NormalizeForDisplay"/> 在内部归一化，
        /// 调用方不需要（也不应该）自己先转一遍</param>
        /// <param name="plan">画什么（图形/对象/文字三类清单）</param>
        /// <param name="warn">告警回调（可为 null）。整体失败、元素被丢、色名回落都会经它上报</param>
        /// <returns>标注图（所有权交调用方）；任何失败返回 <c>null</c>，**不抛异常**</returns>
        public HImage? Bake(HObject baseImage, BakePlan plan, Action<string>? warn = null)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    Warn(warn, "烘焙件已释放，本轮不出标注图");
                    return null;
                }
                if (baseImage == null || !baseImage.IsInitialized())
                {
                    Warn(warn, "底图无效（null 或未初始化），本轮不出标注图");
                    return null;
                }
                plan ??= new BakePlan();

                int w, h;
                try
                {
                    HOperatorSet.GetImageSize(baseImage, out HTuple tw, out HTuple th);
                    w = tw.I;
                    h = th.I;
                }
                catch (Exception ex)
                {
                    Warn(warn, $"读取底图尺寸失败，本轮不出标注图：{ex.Message}");
                    return null;
                }
                if (w <= 0 || h <= 0)
                {
                    Warn(warn, $"底图尺寸非法（{w}x{h}），本轮不出标注图");
                    return null;
                }

                var sink = new WarnSink(warn);
                var temp = new List<HObject>();
                try
                {
                    // 归一化必须在**这里**做而不是留给调用方：五份旧实现里 Caliper 那份就漏了
                    // "单通道非 byte 先线性拉伸"这一步，12/16 位相机的底图整片死白（见 NormalizeForDisplay）。
                    // 收进共享件 = 这条纪律不可能再被某个调用方忘掉。
                    HObject displayBase = NormalizeForDisplay(baseImage, temp);

                    EnsureWindow(w, h);
                    var win = _window!;

                    // 纪律 3：无条件 SetPart。这一行不能省、也不能加 if —— 它是"底图缩在左上角"
                    // 与"上一轮黑边烙进像素"两个事故的同一味解药。
                    HOperatorSet.SetPart(win, 0, 0, h - 1, w - 1);

                    // 底图铺满整幅画布，后面所有笔画都叠在它上面（铺满 = 天然无上一轮残留）
                    HOperatorSet.SetDraw(win, "margin");
                    HOperatorSet.SetColor(win, "white");
                    HOperatorSet.DispObj(displayBase, win);

                    DrawShapes(win, plan, w, h, sink);
                    DrawObjects(win, plan, sink);
                    DrawTexts(win, plan, w, h, sink);

                    // 状态纪律：线宽/绘制模式是窗口级的，画完还原，别带进下一张
                    HOperatorSet.SetLineWidth(win, 1);
                    HOperatorSet.SetDraw(win, "margin");

                    // 纪律 6：dump 出的 shot 是源对象，包装成独立句柄后**立刻**释放
                    HOperatorSet.DumpWindowImage(out HObject shot, win);
                    HImage result;
                    try { result = new HImage(shot); }
                    finally { shot.Dispose(); }
                    return result;
                }
                catch (Exception ex)
                {
                    sink.Add($"离屏烘焙整体失败，本轮不出标注图：{ex.Message}");
                    return null;
                }
                finally
                {
                    foreach (var o in temp)
                    {
                        try { o.Dispose(); } catch { /* 中间对象释放失败不阻断 */ }
                    }
                    sink.Flush();
                }
            }
        }

        /// <summary>
        /// 把任意底图归一化成"HALCON 能 disp_obj 的 byte 图"。
        ///
        /// 返回值的两种可能：① 就是 <paramref name="src"/> 本身（已经是 ≤3 通道 byte，零拷贝）；
        /// ② 登记进 <paramref name="temp"/> 的新对象（**由调用方负责释放**）。本方法从不释放 <paramref name="src"/>。
        ///
        /// 两步各自挡一个已付过学费的坑：
        ///   · **4 通道先丢 alpha** —— disp_obj 只稳妥支持 1/3 通道，4 通道直接画会报"通道数不对"，
        ///     整张标注图就没了（Plugin.BlobDetect\BlobDetectPlugin.cs:1879-1886 的原始注释）。
        ///   · **单通道非 byte 先按实际灰度范围线性拉伸再转** —— convert_image_type(...,'byte') 是
        ///     **截断/饱和**不是缩放：实测 uint2 灰度 1632 转出来就是 255，12/16 位相机的底图整片死白、
        ///     底图信息全丢（同上 :1899-1901）。多通道非 byte 不参与灰度度量，直接转。
        /// </summary>
        public static HObject NormalizeForDisplay(HObject src, List<HObject> temp)
        {
            HOperatorSet.CountChannels(src, out HTuple channels);

            HObject displaySource = src;
            if (channels.I == 4)
            {
                displaySource = DropAlphaChannel(src, temp);
                HOperatorSet.CountChannels(displaySource, out channels);
            }

            HOperatorSet.GetImageType(displaySource, out HTuple type);
            if (type.S == "byte") return displaySource;

            if (channels.I != 1)
            {
                HOperatorSet.ConvertImageType(displaySource, out HObject direct, "byte");
                temp.Add(direct);
                return direct;
            }

            HOperatorSet.GetDomain(displaySource, out HObject domain);
            temp.Add(domain);
            HOperatorSet.MinMaxGray(domain, displaySource, 0, out HTuple minT, out HTuple maxT, out HTuple _);
            double gMin = minT.Length > 0 ? minT[0].D : 0;
            double gMax = maxT.Length > 0 ? maxT[0].D : 0;

            if (double.IsNaN(gMin) || double.IsNaN(gMax) || gMax - gMin < 1e-9)
            {
                // 退化（均匀图/空图）：拉伸没有意义，直接转（反正没有层次可保留）
                HOperatorSet.ConvertImageType(displaySource, out HObject flat, "byte");
                temp.Add(flat);
                return flat;
            }

            double k = 255.0 / (gMax - gMin);
            HOperatorSet.ScaleImage(displaySource, out HObject scaled, k, -gMin * k);
            temp.Add(scaled);
            HOperatorSet.ConvertImageType(scaled, out HObject converted, "byte");
            temp.Add(converted);
            return converted;
        }

        /// <summary>
        /// 取前 3 通道组成一张 3 通道图（丢掉第 4 通道 alpha）。
        /// HALCON 没有一步到位的"去通道"算子，用 append_channel 两步拼出来；
        /// 全部中间对象登记进 temp，由调用方统一释放。
        /// </summary>
        private static HObject DropAlphaChannel(HObject src, List<HObject> temp)
        {
            HOperatorSet.AccessChannel(src, out HObject c1, 1);
            HOperatorSet.AccessChannel(src, out HObject c2, 2);
            HOperatorSet.AccessChannel(src, out HObject c3, 3);
            temp.Add(c1);
            temp.Add(c2);
            temp.Add(c3);

            HOperatorSet.AppendChannel(c1, c2, out HObject c12);
            temp.Add(c12);
            HOperatorSet.AppendChannel(c12, c3, out HObject rgb);
            temp.Add(rgb);
            return rgb;
        }

        // ────────────────────────── 三类元素 ──────────────────────────

        private void DrawShapes(HTuple win, BakePlan plan, int w, int h, WarnSink sink)
        {
            foreach (var s in plan.Shapes)
            {
                var p = s.P;
                if (p == null || p.Length == 0)
                {
                    sink.Add($"图形元素 {s.Kind} 缺参数数组，已跳过");
                    continue;
                }
                try
                {
                    string color = NormalizeColor(s.Color, sink);
                    int lw = s.LineWidth <= 0 ? 1 : Math.Clamp(s.LineWidth, 1, 20);

                    switch (s.Kind)
                    {
                        case BakeShapeKind.Line:
                            if (!Usable(p, 4, "Line", sink)) continue;
                            HOperatorSet.SetLineWidth(win, lw);
                            HOperatorSet.SetColor(win, color);
                            HOperatorSet.DispLine(win,
                                Sane(p[0]), Sane(p[1]), Sane(p[2]), Sane(p[3]));
                            break;

                        case BakeShapeKind.Circle:
                            if (!Usable(p, 3, "Circle", sink)) continue;
                            if (!(p[2] > 0)) { sink.Add($"圆半径非法（{p[2]}），已跳过"); continue; }
                            HOperatorSet.SetLineWidth(win, lw);
                            HOperatorSet.SetColor(win, color);
                            HOperatorSet.DispCircle(win, Sane(p[0]), Sane(p[1]), Sane(p[2]));
                            break;

                        case BakeShapeKind.Ellipse:
                            if (!Usable(p, 5, "Ellipse", sink)) continue;
                            if (!(p[3] > 0) || !(p[4] > 0))
                            {
                                sink.Add($"椭圆半轴非法（ra={p[3]}, rb={p[4]}），已跳过");
                                continue;
                            }
                            HOperatorSet.SetLineWidth(win, lw);
                            HOperatorSet.SetColor(win, color);
                            // P[2] 是**度**（与 GeometryParam.Phi 同口径）；HALCON disp_ellipse 要**弧度**
                            HOperatorSet.DispEllipse(win, Sane(p[0]), Sane(p[1]),
                                p[2] * Math.PI / 180.0, Sane(p[3]), Sane(p[4]));
                            break;

                        case BakeShapeKind.Cross:
                            // 2 参 = 只给中心，十字大小按图幅自适应；3 参 = 显式给大小
                            if (p.Length != 2 && p.Length != 3)
                            {
                                sink.Add($"Cross 参数个数非法（{p.Length}，应为 2 或 3），已跳过");
                                continue;
                            }
                            if (!AllFinite(p, "Cross", sink)) continue;
                            double size = p.Length == 3 && p[2] > 0
                                ? Sane(p[2])
                                : Math.Max(6, Math.Min(w, h) * 0.02);
                            HOperatorSet.SetLineWidth(win, lw);
                            HOperatorSet.SetColor(win, color);
                            HOperatorSet.DispCross(win, Sane(p[0]), Sane(p[1]), size, 0);
                            break;

                        default:
                            sink.Add($"未知图形类型 {s.Kind}，已跳过");
                            continue;
                    }
                }
                catch (Exception ex)
                {
                    sink.Add($"画 {s.Kind} 失败（已跳过该元素）：{ex.Message}");
                }
            }
            HOperatorSet.SetLineWidth(win, 1);
        }

        private void DrawObjects(HTuple win, BakePlan plan, WarnSink sink)
        {
            foreach (var o in plan.Objects)
            {
                try
                {
                    if (o.Obj == null || !o.Obj.IsInitialized())
                    {
                        sink.Add("对象元素未初始化，已跳过");
                        continue;
                    }
                    if (!HasContent(o.Obj)) continue;   // 空对象集：画它是合法的（什么都不画），直接过

                    string color = NormalizeColor(o.Color, sink);
                    int lw = o.LineWidth <= 0 ? 1 : Math.Clamp(o.LineWidth, 1, 20);
                    // 只认 margin / fill：写错（如 "filled"）HALCON 会抛，这里先归一化
                    string draw = string.Equals(o.Draw, "fill", StringComparison.OrdinalIgnoreCase)
                        ? "fill" : "margin";

                    HOperatorSet.SetDraw(win, draw);
                    HOperatorSet.SetLineWidth(win, lw);
                    HOperatorSet.SetColor(win, color);
                    HOperatorSet.DispObj(o.Obj, win);
                }
                catch (Exception ex)
                {
                    sink.Add($"画对象失败（已跳过该元素）：{ex.Message}");
                }
            }
            HOperatorSet.SetLineWidth(win, 1);
            HOperatorSet.SetDraw(win, "margin");
        }

        /// <summary>
        /// 纪律 5：DispText 的底色框**显式**给 box + box_color。
        ///
        /// 这里有一处仓库内的**实测冲突**，记录在案以免后人反复踩：
        ///   · Plugin.CaliperMeasure\AnnotationRenderer.cs:94-95 的注释断言"只给 box_color 不会画框，
        ///     必须同时给 box,'true'"；Plugin.BlobDetect\AnnotationRenderer.cs:92-93 只给了 box_color，
        ///     注释却写着"加白底框"——两者必有一错。
        ///   · Plugin.ImageScript\HDevDisplayBackend.cs:514-518 有**量化实测**：box **默认就是开的**
        ///     （默认落墨 18303px vs ['box'],['false'] 3754px），所以写 ['box'],['true'] 属冗余。
        /// 处置：**一律显式传 box**（要框传 "true"、不要框传 "false"）。这样在两种解释下都正确 ——
        /// 若默认是开，显式 "false" 才关得掉（BlobDetect 的缺陷编号正是要关框，否则白框会把小缺陷整个盖住）；
        /// 若默认是关，显式 "true" 才开得了。断言 B2 用像素采样给这条冲突下最终裁决。
        ///
        /// 另：['font'] / ['size'] 这两个 GenParam 会让算子**直接失败**（同上实测），
        /// 改字号只有窗口级 SetFont 一条路 —— 所以字号在 DispText 之前设。
        /// </summary>
        private void DrawTexts(HTuple win, BakePlan plan, int w, int h, WarnSink sink)
        {
            foreach (var t in plan.Texts)
            {
                try
                {
                    if (string.IsNullOrEmpty(t.Text)) continue;   // 空文本不算错误，静默跳过
                    if (!double.IsFinite(t.Row) || !double.IsFinite(t.Col))
                    {
                        sink.Add($"文字「{Trunc(t.Text)}」坐标非有限值（row={t.Row}, col={t.Col}），已跳过");
                        continue;
                    }
                    string color = NormalizeColor(t.Color, sink);
                    int size = t.FontSize > 0 ? Math.Clamp(t.FontSize, 6, 200) : _defaultFontSize;

                    // 文字用 "window" 坐标系（纪律 4），单位 = 像素；锚点夹进画布，否则整字被裁掉
                    double row = Math.Clamp(t.Row, 0, h - 1);
                    double col = Math.Clamp(t.Col, 0, w - 1);

                    TrySetFont(win, size);

                    // 右/中对齐必须由本类换算：文字宽度取决于**窗口当前字体**，调用方拿不到窗口（纪律 4/5 归本类）。
                    // 夹回 [0,w-1] 后最坏是"贴左边画"，宁可贴边也不能让整行字跑到画布外看不见。
                    if (t.HAlign != BakeHAlign.Left)
                    {
                        double textWidth = TryMeasureStringWidth(win, t.Text, sink);
                        if (textWidth > 0)
                        {
                            col = Math.Clamp(
                                t.HAlign == BakeHAlign.Right ? col - textWidth : col - textWidth / 2.0,
                                0, w - 1);
                        }
                    }

                    HOperatorSet.DispText(
                        win,
                        t.Text,
                        "window",
                        row,
                        col,
                        color,
                        new HTuple("box", "box_color"),
                        new HTuple(t.Box ? "true" : "false", "white"));
                }
                catch (Exception ex)
                {
                    sink.Add($"写文字「{Trunc(t.Text)}」失败（已跳过该元素）：{ex.Message}");
                }
            }
        }

        // ────────────────────────── 窗口与工具 ──────────────────────────

        /// <summary>
        /// 纪律 2：确保存在 w×h 的离屏窗口；尺寸不变就复用，变了才关旧开新（HALCON 不支持改窗口尺寸）。
        /// 纪律 1：mode 只能 'buffer'。
        /// </summary>
        private void EnsureWindow(int w, int h)
        {
            if (_window != null && w == _width && h == _height) return;

            CloseWindow();

            // open_window 参数：行, 列, 宽, 高, 父窗口(底色), 模式, 机器名
            HOperatorSet.OpenWindow(0, 0, w, h, _background, "buffer", "local", out HTuple win);
            _window = win;
            _width = w;
            _height = h;
        }

        /// <summary>
        /// 设置字号（Windows 面 HALCON 的字号串形如 -字体-字号-*-0-*-*-1-）。
        /// 失败直接吞掉：字号是"锦上添花"，绝不能因为它让整张标注图渲染不出来。
        /// </summary>
        private static void TrySetFont(HTuple win, int size)
        {
            try { HOperatorSet.SetFont(win, $"-Consolas-{size}-*-0-*-*-1-"); }
            catch { /* 用当前字号即可 */ }
        }

        /// <summary>
        /// 量一行文字在当前字号下的**像素宽度**（get_string_extents 吃窗口当前字体，所以必须在 TrySetFont 之后调）。
        /// 右/中对齐要把"右边缘/中心"换成 disp_text 要的左上锚点，只能实测不能估——等宽假设在混排
        /// （数字 + 中文 + 冒号）下一定偏，偏了就是"右下对齐看着没对齐"。
        /// 量不到（算子在不带显示会话的 buffer 窗口上失败、或返回非正值）就返回 0，调用方按左对齐画：
        /// **对齐是锦上添花，绝不能因为它把字丢掉**（同 <see cref="TrySetFont"/> 的口径）。
        /// </summary>
        private static double TryMeasureStringWidth(HTuple win, string text, WarnSink sink)
        {
            try
            {
                HOperatorSet.GetStringExtents(win, new HTuple(text), out _, out _, out HTuple width, out _);
                double v = width.Length > 0 ? width[0].D : 0;
                return double.IsFinite(v) && v > 0 ? v : 0;
            }
            catch (Exception ex)
            {
                sink.Add($"量文字「{Trunc(text)}」宽度失败，该行退回左对齐：{ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 空对象集没有"类型"可言：HALCON 画空对象是合法的（什么都不画），但取类型会抛，
        /// 所以先数个数再决定画不画。
        /// </summary>
        private static bool HasContent(HObject o)
        {
            if (o == null || !o.IsInitialized()) return false;
            HOperatorSet.CountObj(o, out HTuple number);
            return number.Length > 0 && number[0].I > 0;
        }

        /// <summary>
        /// 色名归一化：白名单外一律回落 "green" 并告警一次。
        /// 绝不把调用方（最终是用户配置）的字符串直接当 HALCON 色名传下去。
        /// </summary>
        private static string NormalizeColor(string? color, WarnSink sink)
        {
            if (!string.IsNullOrWhiteSpace(color) && AllowedColors.Contains(color.Trim()))
                return color.Trim().ToLowerInvariant();
            sink.Add($"非法色名「{Trunc(color)}」，已回落 green");
            return "green";
        }

        /// <summary>参数个数 + 全有限值校验。任一不满足就是"这个元素画不了"，丢掉它而不是丢整张图</summary>
        private static bool Usable(double[] p, int expect, string what, WarnSink sink)
        {
            if (p.Length != expect)
            {
                sink.Add($"{what} 参数个数非法（{p.Length}，应为 {expect}），已跳过");
                return false;
            }
            return AllFinite(p, what, sink);
        }

        private static bool AllFinite(double[] p, string what, WarnSink sink)
        {
            for (int i = 0; i < p.Length; i++)
            {
                if (!double.IsFinite(p[i]))
                {
                    sink.Add($"{what} 第 {i} 个参数非有限值（{p[i]}），已跳过");
                    return false;
                }
            }
            return true;
        }

        /// <summary>把坐标夹进"HALCON 能安全接受"的范围。图幅内的钳制是调用方（画什么）的责任</summary>
        private static double Sane(double v) => Math.Clamp(v, -CoordLimit, CoordLimit);

        private static string Trunc(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "(空)";
            return s!.Length <= 40 ? s : s.Substring(0, 40) + "…";
        }

        private static void Warn(Action<string>? warn, string message)
        {
            if (warn == null) return;
            try { warn(message); } catch { /* 告警回调自己抛，不能反过来弄挂渲染 */ }
        }

        private void CloseWindow()
        {
            if (_window == null) return;
            try { HOperatorSet.CloseWindow(_window); } catch { /* 关窗失败不阻断回收 */ }
            _window = null;
            _width = 0;
            _height = 0;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                CloseWindow();
            }
        }

        /// <summary>
        /// 告警收敛：一次 Bake 里同类问题可能成百上千条（噪声图上几千个坏元素），
        /// 逐条回调会把日志刷爆。前 8 条原文上报，其余只报计数。
        /// </summary>
        private sealed class WarnSink
        {
            private const int MaxDetail = 8;
            private readonly Action<string>? _warn;
            private readonly Dictionary<string, int> _counts = new();
            private int _emitted;

            public WarnSink(Action<string>? warn) => _warn = warn;

            public void Add(string message)
            {
                _counts.TryGetValue(message, out int n);
                _counts[message] = n + 1;
                if (_emitted < MaxDetail)
                {
                    _emitted++;
                    Warn(_warn, message);
                }
            }

            public void Flush()
            {
                if (_emitted <= MaxDetail) return;
                int total = 0, kinds = 0;
                foreach (var kv in _counts) { total += kv.Value; kinds++; }
                Warn(_warn, $"本次烘焙另有 {total - _emitted} 条同类问题未逐条上报（共 {kinds} 类）");
            }
        }
    }

    /// <summary>
    /// 一次烘焙要画的全部内容。**只管"画什么"，不管"怎么画"**——窗口/回读/释放/钳制/色名归一化
    /// 全在 <see cref="AnnotationBaker"/> 里，调用方不需要（也不应该）碰 HALCON 窗口状态。
    /// </summary>
    public sealed class BakePlan
    {
        /// <summary>用 disp_* 显示算子直画的几何（零中间 HObject，因此零 Dispose 泄漏面）</summary>
        public List<BakeShape> Shapes { get; } = new();

        /// <summary>上游本来就给的 HRegion / HXLD 等对象 → DispObj。**所有权在调用方**，本类不释放</summary>
        public List<BakeObj> Objects { get; } = new();

        /// <summary>文字（DispText）</summary>
        public List<BakeText> Texts { get; } = new();

        public int ElementCount => Shapes.Count + Objects.Count + Texts.Count;

        /// <summary>
        /// 追加一个"左上角多行文字块"（四个插件的判定横幅都是这一种形状）：每行一条 <see cref="BakeText"/>，
        /// 行距 = max(字号+4, 字号×1.3)。空行跳过但不占行距（横幅里偶有空串，不该撑出一个空档）。
        ///
        /// 为什么不是"把多行拼成一个带 '\n' 的字符串"：disp_text 的多行契约是**字符串元组**
        /// （元组有几个元素就画几行），单个字符串里的 '\n' 在 buffer 窗口里没有换行保证。
        /// 拆成多条 BakeText = 行距由调用方算得出、也就断言得了。
        /// </summary>
        public void AddTextBlock(IEnumerable<string>? lines, string color, double row, double col, int fontSize, bool box)
        {
            if (lines == null) return;
            double lineHeight = Math.Max(fontSize + 4, fontSize * 1.3);
            double r = row;
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                Texts.Add(new BakeText(line, r, col, color, fontSize, box));
                r += lineHeight;
            }
        }
    }

    public enum BakeShapeKind { Line, Circle, Ellipse, Cross }

    /// <summary>
    /// 一行文字的**水平锚点含义**（默认 Left = 现有全部调用点的行为）。
    /// 只有烘焙能量宽度（<see cref="AnnotationBaker"/> 独占窗口），所以对齐是"烘焙件的能力"而不是"调用方的算术"。
    /// </summary>
    public enum BakeHAlign { Left, Center, Right }

    /// <summary>
    /// 一条几何笔画。<paramref name="P"/> 的长度按 Kind 约定：
    /// Line=4 (row1,col1,row2,col2) / Circle=3 (row,col,radius) /
    /// Ellipse=5 (row,col,**phi 度**,ra,rb) / Cross=2 (row,col) 或 3 (row,col,size)。
    /// 坐标单位 = 像素，原点左上角；图幅内的钳制由调用方负责，本类只挡非有限值与天文数字。
    /// </summary>
    public readonly record struct BakeShape(BakeShapeKind Kind, double[] P, string Color, int LineWidth);

    /// <summary>一个要 DispObj 的 HALCON 对象（区域/轮廓）。<paramref name="Draw"/> 只认 "margin"（描边）/ "fill"</summary>
    public readonly record struct BakeObj(HObject Obj, string Color, int LineWidth, string Draw);

    /// <summary>
    /// 一行文字。坐标系固定 "window"（= 像素，原点左上角）；<paramref name="FontSize"/> ≤0 用烘焙件默认值；
    /// <paramref name="Box"/> = 是否画白底框（复杂背景上要开，小特征旁的编号要关，否则白框把特征整个盖住）。
    ///
    /// <paramref name="Row"/>/<paramref name="Col"/> 的**含义随 <c>HAlign</c> 变**：Left 时 Col 是左边缘，
    /// Center 时是中心，Right 时是右边缘（烘焙件实测宽度后换算）。默认 Left，所以四份已迁插件与
    /// <c>AddTextBlock</c> 的既有调用点一个字节都不用改。多行块的对齐由调用方逐行给同一个 HAlign
    /// （块高 = 行数 × 行距 调用方自己知道，垂直锚点因此留在调用方算；水平锚点才必须落到这里）。
    /// </summary>
    public readonly record struct BakeText(string Text, double Row, double Col, string Color, int FontSize, bool Box)
    {
        /// <summary>水平锚点含义，默认 Left（= 历史行为）。垂直方向没有这个属性：行距由调用方算，本类只见单行。</summary>
        public BakeHAlign HAlign { get; init; } = BakeHAlign.Left;
    }
}
