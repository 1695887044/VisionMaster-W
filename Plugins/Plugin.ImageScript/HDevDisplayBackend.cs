using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Text;

namespace Plugin.ImageScript
{
    /// <summary>
    /// 效果图绘制（HDevEngine 显示后端）过程中发生的异常。
    /// 与普通 Halcon 算子错误区分开，便于给用户"这是画图环节的问题"的明确提示。
    /// </summary>
    public class DisplayOpException : Exception
    {
        public DisplayOpException(string message) : base(message) { }
    }

    /// <summary>
    /// HDevelop 显示算子（dev_display / dev_disp_text / dev_set_* …）的宿主实现。
    ///
    /// 为什么要它：HDevEngine 默认没有显示环境，脚本里的 dev_* 系列算子要么编译不过、
    /// 要么被插件预处理抹空，导致"在图上贴文字/画框"完全做不到。
    /// 通过 HDevEngine.SetHDevOperators(后端) 注册本类后，引擎会在运行期把每个 dev_* 调用
    /// 回调到这里的 C# 方法，由宿主把它画进一个离屏 buffer 窗口，最后 DumpWindowImage 回读成效果图。
    ///
    /// 实测依据（Halcon 23.05，独立探针）：
    ///   · open_window 的 mode 参数在本版本只接受 'buffer'（离屏，不会闪出窗口），
    ///     'invisible'/'visible'/'pixmap' 一律报 #1305 Wrong value of control parameter 5；
    ///   · dev_disp_text 必须是 7 参完整形式，5 参简写编译不过；
    ///   · if / for 分支内、'W=' + Val$'.2f' 动态拼接、中文文本均可正常回调；
    ///   · 注册后端后引擎的原生 open_window/set_color/disp_text 反而变为编译期非法
    ///     —— 所以必须配合 <see cref="LegacyDisplayShim"/> 做拼写垫片；
    ///   · 与 execute_procedures_jit_compiled='true' 共存，单次"画框+写字"约 1ms。
    ///
    /// 线程模型：HDevEngine 是进程内 static 单例，多个脚本节点会并发回调同一个后端实例，
    /// 因此所有可变状态一律 [ThreadStatic]（每个执行线程各自持有窗口句柄与绘制标记）。
    /// 注意 [ThreadStatic] 字段不能依赖初始化器，取值一律在使用处判空/判界。
    /// </summary>
    internal sealed class HDevDisplayBackend : IHDevOperators
    {
        [ThreadStatic] private static HTuple _win;      // 本线程常驻的离屏 buffer 窗口句柄
        [ThreadStatic] private static int _winW;        // 当前窗口宽（像素）
        [ThreadStatic] private static int _winH;        // 当前窗口高（像素）
        [ThreadStatic] private static bool _sizeLocked;  // 尺寸已由底图决定，脚本自报尺寸不再改
        [ThreadStatic] private static bool _drawn;      // 本轮脚本是否真的画过东西
        [ThreadStatic] private static int _hintW;       // 无底图时的兜底画布宽
        [ThreadStatic] private static int _hintH;       // 无底图时的兜底画布高

        private const int MinSide = 16;
        private const int MaxSide = 8192;
        private const int FallbackW = 800;
        private const int FallbackH = 600;

        /// <summary>本轮脚本是否产生过绘制（决定要不要发布效果图）</summary>
        public static bool HasDrawing => _drawn;

        /// <summary>
        /// 每次 Execute 之前调用：复位绘制标记，并把第一路图像输入预画成底图。
        /// 预画底图的意义：用户只写一句 dev_disp_text 就能"贴在自己图上"，
        /// 不必强制先写 dev_display (Image)；而他写了 dev_display 时也只是原位重画一次，无副作用。
        /// </summary>
        public static void Begin(HObject baseImage)
        {
            _drawn = false;
            _sizeLocked = false;
            _hintW = 0;
            _hintH = 0;

            if (IsImage(baseImage) && TryImageSize(baseImage, out int w, out int h))
            {
                Ensure(w, h);
                _sizeLocked = true;
                Guard("dev_display (底图)", delegate { HOperatorSet.DispObj(baseImage, _win); });
                return;
            }

            // 没有图像输入：保留上一次的窗口尺寸复用，画布尺寸等真正绘制时再定
            if (_win != null)
            {
                try { HOperatorSet.ClearWindow(_win); } catch { }
            }
        }

        /// <summary>把 buffer 窗口内容回读成效果图（返回的 HImage 所有权交给调用方）</summary>
        public static HImage Capture()
        {
            if (_win == null) return null;
            HImage result = null;
            Guard("dump_window_image", delegate
            {
                HObject ho;
                HOperatorSet.DumpWindowImage(out ho, _win);
                result = new HImage(ho);
            });
            return result;
        }

        /// <summary>释放本线程窗口（插件卸载/节点销毁时可选调用；正常路径常驻复用）</summary>
        public static void Release()
        {
            if (_win == null) return;
            try { HOperatorSet.CloseWindow(_win); } catch { }
            _win = null;
            _winW = 0;
            _winH = 0;
            _sizeLocked = false;
            _drawn = false;
        }

        // ────────────────────────── 内部工具 ──────────────────────────

        private static bool IsImage(HObject o)
        {
            if (o == null || !o.IsInitialized()) return false;
            // 空对象集（阈值/筛选后一个目标都没剩下）取类型会抛 IndexOutOfRange，
            // 而 HALCON 自己 disp_obj 画空对象是合法的（什么都不画）。
            // 所以这里先数元素个数：0 个就当作「不是图像」，交给默认画布分支处理。
            HTuple number;
            HOperatorSet.CountObj(o, out number);
            if (number.Length == 0 || number[0].I == 0) return false;
            return o.GetObjClass().S == "image";
        }

        private static bool TryImageSize(HObject o, out int w, out int h)
        {
            w = h = 0;
            try
            {
                HTuple tw, th;
                HOperatorSet.GetImageSize(new HImage(o), out tw, out th);
                w = tw.I;
                h = th.I;
                return w > 0 && h > 0;
            }
            catch { return false; }
        }

        private static int Clamp(int v)
        {
            if (v < MinSide) return MinSide;
            if (v > MaxSide) return MaxSide;
            return v;
        }

        /// <summary>
        /// 确保存在一个 w×h 的离屏 buffer 窗口；尺寸变化时关旧开新（HALCON 不支持改窗口尺寸）。
        /// </summary>
        private static void Ensure(int w, int h)
        {
            w = Clamp(w > 0 ? w : (_hintW > 0 ? _hintW : FallbackW));
            h = Clamp(h > 0 ? h : (_hintH > 0 ? _hintH : FallbackH));

            if (_win != null && w == _winW && h == _winH) return;

            if (_win != null)
            {
                try { HOperatorSet.CloseWindow(_win); } catch { }
                _win = null;
            }

            HTuple win = null;
            // 参数顺序：row, column, width, height, background, mode, machine, out window
            // mode 只能给 'buffer'：离屏渲染，不会在屏幕上闪出窗口
            Guard("open_window(buffer)", delegate
            {
                HOperatorSet.OpenWindow(0, 0, w, h, "white", "buffer", "local", out win);
            });
            _win = win;
            _winW = w;
            _winH = h;
        }

        /// <summary>脚本没显示过图像时用的兜底尺寸</summary>
        private static void EnsureDefault()
        {
            if (_sizeLocked && _win != null) return;
            Ensure(_hintW, _hintH);
        }

        /// <summary>
        /// ★ 交给 HDevelop 引擎的窗口句柄必须是"新容器"，绝不能直接把内部持有的 _win 实例 out 出去。
        /// 实测：一旦把 _win 本体交给引擎，封送过程会把它内部的 handle 搬空（Length 仍为 1、ToString 变空串），
        /// 之后所有绘制算子都报 HALCON error #2453: HALCON handle is NULL。
        /// 另两条铁律：HTuple.Clone() 对句柄型抛 #2455（句柄不可 serialize）；Append() 会偷走源 tuple 的句柄。
        /// 唯一可用的复制方式是 new HTuple(_win.H)。
        /// </summary>
        private static HTuple HandoutWindow()
        {
            if (_win == null) Ensure(_hintW, _hintH);
            try
            {
                return new HTuple(_win.H);
            }
            catch
            {
                return _win;   // 兜底：极端情况下宁可退回原实例，也不让脚本在这里直接崩
            }
        }

        private static void Guard(string op, Action act)
        {
            try
            {
                act();
            }
            catch (HalconException hex)
            {
                string raw = hex.GetErrorMessage();
                if (raw == null || raw.Length == 0) raw = hex.Message;
                throw new DisplayOpException(
                    $"效果图绘制失败（{op}）：{raw}\n" +
                    $"提示：检查颜色名是否为 Halcon 认得的写法（如 'red'/'green'/'cyan'），" +
                    $"以及行/列坐标是否为有效数值");
            }
        }

        // ────────────────────────── IHDevOperators 实现 ──────────────────────────

        public void DevOpenWindow(HTuple row, HTuple col, HTuple width, HTuple height, HTuple background, out HTuple window)
        {
            Guard("dev_open_window", delegate
            {
                // 尺寸策略：底图优先。已按底图定尺后忽略脚本自报的宽高，保证效果图与原图 1:1 对齐
                if (_sizeLocked) EnsureDefault();
                else Ensure(SafeInt(width, FallbackW), SafeInt(height, FallbackH));
            });
            window = HandoutWindow();
        }

        public void DevCloseWindow()
        {
            // 脚本的 close_window 只清空、不真关：窗口句柄由宿主常驻复用，避免每帧重建
            Guard("dev_close_window", delegate
            {
                EnsureDefault();
                HOperatorSet.ClearWindow(_win);
            });
        }

        public void DevSetWindow(HTuple window)
        {
            // 单窗口模型：忽略脚本切窗，后续绘制仍落在宿主效果图窗口上
        }

        public void DevGetWindow(out HTuple window)
        {
            Guard("dev_get_window", delegate { EnsureDefault(); });
            window = HandoutWindow();
        }

        public void DevSetWindowExtents(HTuple row, HTuple col, HTuple width, HTuple height)
        {
            Guard("dev_set_window_extents", delegate
            {
                if (_sizeLocked) EnsureDefault();
                else Ensure(SafeInt(width, FallbackW), SafeInt(height, FallbackH));
            });
        }

        public void DevSetPart(HTuple row1, HTuple col1, HTuple row2, HTuple col2)
        {
            Guard("dev_set_part", delegate
            {
                EnsureDefault();
                HOperatorSet.SetPart(_win, row1, col1, row2, col2);
            });
        }

        public void DevClearWindow()
        {
            Guard("dev_clear_window", delegate
            {
                EnsureDefault();
                _drawn = true;
                HOperatorSet.ClearWindow(_win);
            });
        }

        public void DevDisplay(HObject objectValue)
        {
            Guard("dev_display", delegate
            {
                if (IsImage(objectValue) && !_sizeLocked && TryImageSize(objectValue, out int w, out int h))
                {
                    // 第一次显示图像：以它为准定住画布尺寸
                    Ensure(w, h);
                    _sizeLocked = true;
                }
                else
                {
                    EnsureDefault();
                }
                _drawn = true;
                HOperatorSet.DispObj(objectValue, _win);
            });
        }

        public void DevDispText(HTuple text, HTuple coordSystem, HTuple row, HTuple column, HTuple color, HTuple genParamName, HTuple genParamValue)
        {
            Guard("dev_disp_text", delegate
            {
                EnsureDefault();
                _drawn = true;
                HOperatorSet.DispText(_win, text, coordSystem, row, column, color, genParamName, genParamValue);
            });
        }

        public void DevSetDraw(HTuple mode)
        {
            Guard("dev_set_draw", delegate
            {
                EnsureDefault();
                HOperatorSet.SetDraw(_win, mode);
            });
        }

        public void DevSetContourStyle(HTuple style)
        {
            Guard("dev_set_contour_style", delegate
            {
                EnsureDefault();
                HOperatorSet.SetContourStyle(_win, style);
            });
        }

        public void DevSetShape(HTuple shape)
        {
            Guard("dev_set_shape", delegate
            {
                EnsureDefault();
                HOperatorSet.SetShape(_win, shape);
            });
        }

        public void DevSetColored(HTuple numChannels)
        {
            Guard("dev_set_colored", delegate
            {
                EnsureDefault();
                HOperatorSet.SetColored(_win, numChannels);
            });
        }

        public void DevSetColor(HTuple color)
        {
            Guard("dev_set_color", delegate
            {
                EnsureDefault();
                HOperatorSet.SetColor(_win, color);
            });
        }

        public void DevSetLut(HTuple lut)
        {
            Guard("dev_set_lut", delegate
            {
                EnsureDefault();
                HOperatorSet.SetLut(_win, lut);
            });
        }

        public void DevSetPaint(HTuple mode)
        {
            Guard("dev_set_paint", delegate
            {
                EnsureDefault();
                HOperatorSet.SetPaint(_win, mode);
            });
        }

        public void DevSetLineWidth(HTuple width)
        {
            Guard("dev_set_line_width", delegate
            {
                EnsureDefault();
                HOperatorSet.SetLineWidth(_win, width);
            });
        }

        private static int SafeInt(HTuple t, int fallback)
        {
            try
            {
                if (t == null || t.Length == 0) return fallback;
                return t.I;
            }
            catch { return fallback; }
        }
    }

    /// <summary>
    /// 老式窗口算子 → HDevelop 标准 dev_* 拼写的行内垫片（送引擎编译前改写，编辑器原文不变）。
    ///
    /// 为什么必须改写：注册显示后端后，HDevEngine 会反过来把原生
    /// open_window / set_color / disp_text / disp_message / disp_image / dump_window_image …
    /// 判为 invalid program line（实测）。而工程师从 HDevelop 或网上粘的代码大量用这些写法，
    /// 不改写就等于"一上 D 方案，老脚本全编译失败"。
    ///
    /// 硬约束：只允许 1 行换 1 行 —— 插件的报错翻译依赖行号一一对应。
    /// 因此 disp_rectangle1 / disp_circle / disp_line / write_string 这类
    /// "直接画在窗口上、需要拆成 gen_* + dev_display 两行"的老算子无法垫片，改为明确报错引导。
    /// </summary>
    internal static class LegacyDisplayShim
    {
        /// <summary>去掉首参（窗口句柄）后加 dev_ 前缀即可等价的原生算子</summary>
        private static readonly HashSet<string> DropWindowHandle = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "set_color", "set_draw", "set_line_width", "set_shape", "set_paint",
            "set_lut", "set_contour_style", "set_colored", "set_part"
        };

        /// <summary>无法 1 行换 1 行改写的老算子 → 给用户的改写指引</summary>
        private static readonly Dictionary<string, string> Unsupported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "disp_rectangle1", "改成两行：gen_rectangle1 (Rect, Row1, Column1, Row2, Column2) 然后 dev_display (Rect)" },
            { "disp_circle",     "改成两行：gen_circle (Circle, Row, Column, Radius) 然后 dev_display (Circle)" },
            { "disp_line",       "改成两行：gen_region_line (Line, Row1, Column1, Row2, Column2) 然后 dev_display (Line)；要任意粗细的细线用 gen_contour_polygon_xld (Line, [Row1,Row2], [Col1,Col2])" },
            { "disp_arrow",      "HALCON 里没有 gen_arrow 这个算子（实测 23.05 无），画不出带箭头的轮廓。改成 gen_region_line (Arrow, Row1, Column1, Row2, Column2) + dev_display (Arrow)；要箭头尖端再用 gen_contour_polygon_xld 补一个小三角形" },
            { "disp_cross",      "改成两行：gen_cross_contour_xld (Cross, Row, Col, Size, Angle) 然后 dev_display (Cross)" },
            { "disp_polygon",    "改成两行：gen_contour_polygon_xld (Contour, [Row1,Row2,…], [Col1,Col2,…]) 然后 dev_display (Contour)" },
            { "set_display_font", "删掉它——HDevEngine 不支持设字体，dev_disp_text 用 HALCON 默认字体。想让文字更醒目就加底色：dev_disp_text (Text, 'window', 12, 12, 'black', ['box','box_color'], ['true','yellow'])" },
            { "set_font",         "删掉它——HDevEngine 不支持设字体，dev_disp_text 用 HALCON 默认字体" },
            { "set_tposition",   "删掉它，把行/列直接写进 dev_disp_text 的第 3、4 个参数" },
            { "write_string",    "改成 dev_disp_text (Text, 'image', Row, Column, 'green', [], [])" },
            { "get_window_extents", "HDevEngine 里不可用；效果图尺寸由插件按输入图像自动决定，删掉即可" },
            { "set_window_extents", "HDevEngine 里不可用；请改用 dev_open_window" },
        };

        /// <summary>
        /// 改写单行。
        /// </summary>
        /// <param name="line">剥离行尾注释后的代码行（含原缩进）</param>
        /// <param name="result">改写结果；无需改写时为原行</param>
        /// <param name="hint">非 null 表示该行用了无法垫片的老算子，内容是给用户的中文指引</param>
        /// <returns>是否发生了改写</returns>
        public static bool TryRewrite(string line, out string result, out string hint)
        {
            result = line;
            hint = null;
            if (string.IsNullOrEmpty(line)) return false;

            int s = 0;
            while (s < line.Length && (line[s] == ' ' || line[s] == '\t')) s++;
            int nameStart = s;
            while (s < line.Length && (char.IsLetterOrDigit(line[s]) || line[s] == '_')) s++;
            if (s == nameStart) return false;

            string op = line.Substring(nameStart, s - nameStart);

            // dev_* 与赋值行（X := …）不动
            if (op.StartsWith("dev_", StringComparison.OrdinalIgnoreCase)) return false;

            int p = s;
            while (p < line.Length && (line[p] == ' ' || line[p] == '\t')) p++;
            if (p >= line.Length || line[p] != '(') return false;

            int close = MatchParen(line, p);
            if (close < 0) return false;                      // 括号不闭合：交给既有 PrecheckBody 报错
            for (int i = close + 1; i < line.Length; i++)
                if (line[i] != ' ' && line[i] != '\r' && line[i] != '\t') return false; // 尾巴有内容：保守放弃

            string indent = line.Substring(0, nameStart);
            List<string> args = SplitArgs(line.Substring(p + 1, close - p - 1));

            // 1) 老式"直接画在窗口上"的算子：给指引，不改写（让引擎按原名报错，再由上层统一提示）
            if (Unsupported.TryGetValue(op, out string guide))
            {
                hint = $"{op} 无法自动兼容，请手动改写：{guide}";
                return false;
            }

            // 2) dump_window_image / dump_window：出图由宿主统一负责，置空占位保持行号
            if (string.Equals(op, "dump_window_image", StringComparison.OrdinalIgnoreCase)
                || string.Equals(op, "dump_window", StringComparison.OrdinalIgnoreCase))
            {
                result = new string(' ', line.Length);
                return true;
            }

            string newName;
            List<string> na;

            if (string.Equals(op, "open_window", StringComparison.OrdinalIgnoreCase))
            {
                // open_window(Row,Col,Width,Height,Background,WindowHandle)
                // dev_open_window(Row,Col,Width,Height,Background,WindowHandle) —— 参数完全同形
                newName = "dev_open_window";
                na = args;
            }
            else if (string.Equals(op, "clear_window", StringComparison.OrdinalIgnoreCase))
            {
                newName = "dev_clear_window";
                na = new List<string>();
            }
            else if (string.Equals(op, "close_window", StringComparison.OrdinalIgnoreCase))
            {
                newName = "dev_close_window";
                na = new List<string>();
            }
            else if (string.Equals(op, "disp_obj", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(op, "disp_image", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(op, "disp_region", StringComparison.OrdinalIgnoreCase))
            {
                // disp_obj / disp_image / disp_region (Obj, W) → dev_display (Obj)
                newName = "dev_display";
                na = Take(args, 0, 1);
            }
            else if (string.Equals(op, "disp_text", StringComparison.OrdinalIgnoreCase))
            {
                // disp_text (W, Text, Coord, Row, Col, Color[, GPN, GPV]) → dev_disp_text 的 7 参形式
                newName = "dev_disp_text";
                na = Drop(args, 1);
                EnsureCoordSystem(na);
                PadDispText(na);
            }
            else if (string.Equals(op, "disp_message", StringComparison.OrdinalIgnoreCase))
            {
                // disp_message (W, Text, Disp, Row, Col, Color, DrawBackground)
                // → dev_disp_text (Text, Disp, Row, Col, Color, [], [])  （背景框由 Halcon 默认处理）
                newName = "dev_disp_text";
                na = Drop(args, 1);
                while (na.Count > 5) na.RemoveAt(na.Count - 1);
                PadDispText(na);
            }
            else if (DropWindowHandle.Contains(op))
            {
                newName = "dev_" + op;
                na = Drop(args, 1);
            }
            else
            {
                return false;
            }

            result = indent + newName + " (" + string.Join(", ", na.ToArray()) + ")";
            return true;
        }

        /// <summary>dev_disp_text 七个参数的官方默认值（顺序与 HDevelop 一致）</summary>
        private static readonly string[] DispTextDefaults =
            { "'hello'", "'window'", "12", "12", "'black'", "[]", "[]" };

        /// <summary>
        /// 老代码里的 disp_text 有两种写法，去掉窗口句柄后参数含义完全错位：
        ///   HALCON 17+ 正式签名：Text, 'window'|'image', Row, Col, Color, GenName, GenValue
        ///   网上老教程常见写法： Text, Row, Col            （把坐标直接放在第三位）
        /// CoordSystem 实测只吃 'window'/'image' 两个字面量，塞数字必报错，
        /// 所以按第 2 个参数是不是坐标系字面量来区分：不是就补一个 'image'
        /// （用 'image' 而非 'window'：老写法里的数字本来就是图像坐标）。
        /// </summary>
        private static void EnsureCoordSystem(List<string> a)
        {
            if (a.Count < 2) { a.Add("'image'"); return; }
            string v = a[1].Trim();
            bool isCoord = v.Length > 2
                && (v[0] == '\'' || v[0] == '"')
                && (string.Equals(v.Substring(1, v.Length - 2), "window", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(v.Substring(1, v.Length - 2), "image", StringComparison.OrdinalIgnoreCase));
            if (!isCoord) a.Insert(1, "'image'");
        }

        private static void PadDispText(List<string> a)
        {
            // dev_disp_text 实测必须 7 参。缺位按官方默认值补——
            // 不能一概补 []：Row/Column/CoordSystem 传空 tuple 引擎直接报错。
            while (a.Count < 7) a.Add(DispTextDefaults[a.Count]);
            while (a.Count > 7) a.RemoveAt(a.Count - 1);
        }

        private static List<string> Take(List<string> args, int from, int count)
        {
            var r = new List<string>();
            for (int i = from; i < args.Count && r.Count < count; i++) r.Add(args[i]);
            return r;
        }

        private static List<string> Drop(List<string> args, int headCount)
        {
            var r = new List<string>();
            for (int i = headCount; i < args.Count; i++) r.Add(args[i]);
            return r;
        }

        /// <summary>按顶层逗号切分参数（忽略 () [] {} 内部与单引号字符串里的逗号）</summary>
        private static List<string> SplitArgs(string inside)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(inside) || inside.Trim().Length == 0) return list;

            int depth = 0;
            bool inStr = false;
            var sb = new StringBuilder();
            for (int i = 0; i < inside.Length; i++)
            {
                char c = inside[i];
                if (c == '\'')
                {
                    inStr = !inStr;
                    sb.Append(c);
                    continue;
                }
                if (!inStr)
                {
                    if (c == '(' || c == '[' || c == '{') depth++;
                    else if (c == ')' || c == ']' || c == '}') depth--;
                    else if (c == ',' && depth == 0)
                    {
                        list.Add(sb.ToString().Trim());
                        sb.Length = 0;
                        continue;
                    }
                }
                sb.Append(c);
            }
            list.Add(sb.ToString().Trim());
            return list;
        }

        /// <summary>返回 open 处左括号配对的右括号下标；不闭合返回 -1</summary>
        private static int MatchParen(string line, int open)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = open; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\'') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}
