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
    ///   · 原生 open_window / set_color / disp_text 注册后端后【照样编译通过、也不报错】，
    ///     但它们画的是另一个窗口/句柄，效果图会静默变空白（实测：脚本只调 open_window 时
    ///     Capture() 返回 null，用户拿不到任何报错却什么都看不见）。
    ///     所以必须配合 <see cref="LegacyDisplayShim"/> 把输出导航回宿主画布——
    ///     拦它的理由是「防静默丢失」，不是「防编译失败」；
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

        // ── publish_preview 输出改道（见 ScriptAssets\procedures\publish_preview.hdvp）──
        // 脚本里 publish_preview (Image, 3) → 展开成 dev_set_window(['vmview',3]) + dev_display(Image)。
        // _routeView 是 dev_set_window 记下的待改道视图号；_router 由宿主在 Begin 时按线程传入。
        // 必须 [ThreadStatic]：引擎是 static 单例，多个脚本节点并发执行，不能共用一个回调目标。
        [ThreadStatic] private static int _routeView;
        [ThreadStatic] private static Action<HObject, int> _router;

        // 画布窗口的原生字体串（形如 'default-Normal-12'），建窗时抓一次，每轮 Begin 写回。
        [ThreadStatic] private static HTuple _font0;

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
        public static void Begin(HObject baseImage, Action<HObject, int> router = null)
        {
            _drawn = false;
            _sizeLocked = false;
            _hintW = 0;
            _hintH = 0;
            _routeView = 0;
            _router = router;

            if (IsImage(baseImage) && TryImageSize(baseImage, out int w, out int h))
            {
                Ensure(w, h);
                RestoreFont();
                _sizeLocked = true;
                Guard("dev_display (底图)", delegate { HOperatorSet.DispObj(baseImage, _win); });
                return;
            }

            // 没有图像输入：保留上一次的窗口尺寸复用，画布尺寸等真正绘制时再定
            if (_win != null)
            {
                try { HOperatorSet.ClearWindow(_win); } catch { }
            }
            RestoreFont();
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
            _font0 = null;      // 窗口都没了，旧字体串不能留给下一扇窗
        }

        // ────────────────────────── 内部工具 ──────────────────────────

        /// <summary>
        /// 对象集里是否真有内容。空对象集（阈值/筛选后一个都没剩下）没有"类型"可言，
        /// 取元素/取类型都会抛 IndexOutOfRange，唯一安全的判据是先数个数。
        /// </summary>
        private static bool HasContent(HObject o)
        {
            if (o == null || !o.IsInitialized()) return false;
            HTuple number;
            HOperatorSet.CountObj(o, out number);
            return number.Length > 0 && number[0].I > 0;
        }

        private static bool IsImage(HObject o)
        {
            // HALCON 自己 disp_obj 画空对象是合法的（什么都不画），但取类型会抛——
            // 所以先判有无内容，0 个就当作「不是图像」，交给默认画布分支处理。
            if (!HasContent(o)) return false;
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

            // 刚建好的窗口还没人碰过字体，此刻 get_font 拿到的就是"原生"值，存下来供 Begin 复位。
            // 实测 get_font/set_font 是逐位忠实的回路（把窗口污染成 34 号后「存→改→写回」，
            // 落墨面积仍是 34 号那个数），所以这个快照可信。
            _font0 = null;
            try { HOperatorSet.GetFont(_win, out _font0); }
            catch { _font0 = null; }   // 拿不到就算了，复位是锦上添花，绝不能因此弄挂绘制
        }

        /// <summary>
        /// 把画布窗口的字体写回原生值。
        ///
        /// 为什么由宿主做而不是让每个模板自己收尾：字号是「窗口级」状态，而画布窗口按线程
        /// 常驻复用（HDevelop 在反复 open/close 时会 #9302 死锁，所以刻意不关）。脚本半路抛异常
        /// 就漏掉还原，会把大字号串给后面所有脚本——实测串台后下一个只写 dev_disp_text 的
        /// 脚本落墨面积仍是 18303 px 而不是原生 2500 px，效果图上的字直接糊满半张图。
        ///
        /// 也别想用 set_display_font(W, -1, …) 复位：官方过程里 -1 就是 16 号，不是原生 12 号。
        /// </summary>
        private static void RestoreFont()
        {
            if (_win == null || _font0 == null) return;
            try { HOperatorSet.SetFont(_win, _font0); }
            catch { /* 复位失败不影响本轮绘制 */ }
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
            // publish_preview 的带内信令：['vmview',N] —— 只记下视图号，等下一个 dev_display 消费。
            // 除此之外仍是单窗口模型：忽略脚本切窗，后续绘制都落在宿主效果图窗口上。
            int view = ReadViewDirective(window);
            if (view > 0) _routeView = view;
        }

        /// <summary>
        /// 识别 dev_set_window 收到的是不是 publish_preview 信令 ['vmview',N]。
        /// 真窗口句柄由 dev_get_window 交回，是 H5E... 形式的单个句柄，不可能是 2 元素元组，
        /// 所以这里可以判得很死，不会误伤正常的切窗口写法。
        /// </summary>
        private static int ReadViewDirective(HTuple window)
        {
            try
            {
                if (window == null || window.Length != 2) return 0;
                if (window[0].S != "vmview") return 0;
                int v = window[1].I;
                return (v >= 1 && v <= 9) ? v : 0;
            }
            catch { return 0; }
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
                // publish_preview 改道：这张图交宿主直接发到指定视图，不进效果图画布、
                // 也不置 _drawn（否则脚本只用 publish_preview 时会多出一张空白的窗口1截图）。
                // 拿不到宿主回调、或图是空的，就退回正常绘制——绝不把用户的图无声吞掉。
                int view = _routeView;
                _routeView = 0;
                if (view > 0 && _router != null && HasContent(objectValue))
                {
                    _router(objectValue, view);
                    return;
                }

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
    /// 为什么必须改写：注册显示后端后，原生 open_window / set_color / disp_text /
    /// disp_image / dump_window_image … 并【不会】编译失败（这一点早期注释写错了，已实测纠正），
    /// 真正的问题是它们画在另一张窗口上——效果图会静默变空白且不报错，比编译失败更难查。
    /// 改写就是把输出导航回宿主那张 buffer 画布。工程师从 HDevelop 或网上粘的代码大量用这些
    /// 写法，不改写等于"一上 D 方案，老脚本画出来的东西全看不见"。
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
            { "disp_arrow",      "改成 gen_arrow_contour_xld (Arrow, Row1, Column1, Row2, Column2, HeadLength, HeadWidth) 然后 dev_display (Arrow)。注意它是 7 参（1 输出 + 6 入参），多写一个参数会报 invalid program line" },
            { "disp_cross",      "改成两行：gen_cross_contour_xld (Cross, Row, Col, Size, Angle) 然后 dev_display (Cross)" },
            { "disp_polygon",    "改成两行：gen_contour_polygon_xld (Contour, [Row1,Row2,…], [Col1,Col2,…]) 然后 dev_display (Contour)" },
            // set_display_font / set_font 原先被列进本字典（=直接判脚本出错），理由写的是
            // "HDevEngine 不支持设字体"。实测该理由不成立，故摘出黑名单：配 dev_get_window
            // 拿到的真句柄，两者都能正常执行并真的改变字号（同一行文字 12 号 2500 px → 34 号 18303 px）。
            // 三条实测出来的坑，写模板时必须记住：
            //   1) Size 给 -1 **不是**还原默认。官方 set_display_font.hdvp 里是 if(Size=-1)
            //      Size:=16（Windows 再乘 1.13677），实测 -1 与显式写 16 落墨面积逐位相同（4759 px）。
            //      画布窗口的原生字体是 default-Normal-12，所以"写 -1 复原"是个看着合理的陷阱。
            //   2) 唯一忠实的还原回路是 get_font 存原值 + set_font 写回：先把窗口污染成 34 号，
            //      再「存 → 改 24 → 写回」，落墨面积仍是 34 号的 18303 px，逐位相同。
            //      不过模板不必自己写这两行——宿主在 Begin() 里每轮自动复位，见 RestoreFont。
            //   3) 字号是窗口级状态、画布窗口常驻复用，脚本之间会串台；好在 set_display_font
            //      是幂等的绝对设置，所以模板里"要多大就显式设多大"，不必依赖上游状态。
            // 另：dev_disp_text 的第 6、7 个参数（GenParam）实测边界——
            //   ['box'] 有效且**默认就是开的**（关掉才看得出：默认 18303 px vs ['box'],['false'] 3754 px），
            //   所以再写 ['box'],['true'] 纯属冗余；['box_color'] 有效；['shadow'] 完全无效
            //   （关框后加不加阴影都是 3754 px）；['font'] / ['size'] 直接让算子失败——
            //   想改字号只能走上面的窗口级 set_display_font，没有单次调用的路子。
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
