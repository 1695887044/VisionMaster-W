using System;
using HalconDotNet;

namespace Plugin.CaliperMeasure
{
    /// <summary>
    /// 标注图渲染器：把"底图 + 卡尺小矩形 + 边缘点十字 + 左上角文字"合成成一张可显示的 HImage。
    ///
    /// 【为什么要开离屏窗口】
    /// HALCON 往图上写文字（disp_text）必须依附窗口的字库机制，没有"纯算子写文字"的路子。
    /// 照搬仓库既有方案（Plugin.BlobDetect.AnnotationRenderer）：open_window 用 'buffer' 模式
    /// 离屏渲染（不会在屏幕上闪出窗口），画完再用 dump_window_image 回读成 HImage。
    ///
    /// 【为什么窗口要缓存复用，而不是每次开/关】
    /// 仓库既有结论：HALCON 反复 open/close 窗口会 #9302 死锁。因此窗口按"图像尺寸"缓存，
    /// 尺寸不变就一直复用；尺寸变了才关旧开新。每个插件实例各自持有一扇窗（配置预览实例、流程运行实例互不干扰），
    /// 并加锁防止预览线程与流程线程同时操作同一扇窗（窗口状态是全局的）。
    /// </summary>
    internal sealed class AnnotationRenderer : IDisposable
    {
        private readonly object _gate = new();

        private HTuple? _window;
        private int _width;
        private int _height;
        private bool _disposed;

        /// <summary>
        /// 渲染标注图。返回的 HImage 所有权交给调用方；失败返回 null（由调用方决定是否降级）。
        /// </summary>
        /// <param name="baseImage">底图（原图或灰度图，尺寸即画布尺寸；本方法不释放它，所有权在调用方）</param>
        /// <param name="caliperRects">卡尺位置小矩形（XLD 轮廓，可为 null/空集；空集不画）</param>
        /// <param name="measureLines">测量连线（XLD 轮廓，可为 null/空集；空集不画）——量的是哪两点/哪段距离</param>
        /// <param name="fittedGeometry">拟合几何（XLD，可为 null/空集；空集不画）——拟合出的直线/圆（第二批新增图层）</param>
        /// <param name="edgePoints">边缘点十字（XLD，可为 null/空集；空集不画）</param>
        /// <param name="lines">左上角多行文字</param>
        /// <param name="textColor">文字颜色（HALCON 颜色名，如 "green"/"red"）</param>
        public HImage? Render(HObject baseImage, HObject? caliperRects, HObject? measureLines, HObject? fittedGeometry, HObject? edgePoints, string[] lines, string textColor)
        {
            lock (_gate)
            {
                if (_disposed || baseImage == null || !baseImage.IsInitialized()) return null;

                // 画布大小取自底图；尺寸变了 EnsureWindow 会自动重建窗口
                HOperatorSet.GetImageSize(baseImage, out HTuple tw, out HTuple th);
                int w = tw.I, h = th.I;
                if (w <= 0 || h <= 0) return null;

                EnsureWindow(w, h);
                var win = _window!;

                // 1. 底图：先把整幅画布铺满，后面所有笔画都叠在它上面
                HOperatorSet.SetDraw(win, "margin");
                HOperatorSet.SetColor(win, "white");
                HOperatorSet.DispObj(baseImage, win);

                // 2. 卡尺位置小矩形：黄色描边，只画轮廓不遮挡被检特征
                if (HasContent(caliperRects))
                {
                    HOperatorSet.SetLineWidth(win, 1);
                    HOperatorSet.SetColor(win, "yellow");
                    HOperatorSet.DispObj(caliperRects!, win);
                }

                // 3. 测量连线：青色加粗，把"量的是哪两点/哪段距离"画出来
                if (HasContent(measureLines))
                {
                    HOperatorSet.SetLineWidth(win, 2);
                    HOperatorSet.SetColor(win, "cyan");
                    HOperatorSet.DispObj(measureLines!, win);
                }

                // 3.5 拟合几何（第二批新增）：品红加粗，画拟合出的直线/圆——与青色"测量连线"
                // 分层，一眼区分"拟合到的几何"与"测量的那段距离"
                if (HasContent(fittedGeometry))
                {
                    HOperatorSet.SetLineWidth(win, 2);
                    HOperatorSet.SetColor(win, "magenta");
                    HOperatorSet.DispObj(fittedGeometry!, win);
                }

                // 4. 边缘点十字：绿色加粗，让"边缘找没找到、找到在哪"一眼可见
                if (HasContent(edgePoints))
                {
                    HOperatorSet.SetLineWidth(win, 2);
                    HOperatorSet.SetColor(win, "green");
                    HOperatorSet.DispObj(edgePoints!, win);
                }

                // 线宽状态是窗口级的，画完还原，别把状态带进下一张图
                HOperatorSet.SetLineWidth(win, 1);

                // 5. 左上角文字：加白底框，保证在任何背景上都读得清。
                // 注意：只给 'box_color' 不会画框——必须同时给 'box','true' 才真正画出底色方框
                //（HALCON disp_text 的 box 是独立开关，box_color 只是它的颜色）。
                TrySetFont(win, 20);
                HOperatorSet.DispText(
                    win,
                    new HTuple(lines),
                    "window",
                    12,
                    12,
                    textColor,
                    new HTuple("box", "box_color"),
                    new HTuple("true", "white"));

                // 6. 从离屏窗口回读成图像（这就是"标注图"）
                // 注意：dump_window_image 产出的 shot 是"源对象"，new HImage(shot) 是独立句柄，
                // 但 HALCON .NET 是引用计数语义——不释放源对象会留下一个引用计数导致内存只增不减
                //（已实测：500 次不释放源对象泄漏约 160MB）。因此包装后立刻释放 shot。
                HOperatorSet.DumpWindowImage(out HObject shot, win);
                var result = new HImage(shot);
                shot.Dispose();
                return result;
            }
        }

        /// <summary>
        /// 确保存在 w×h 的离屏窗口；尺寸不变就复用，变了才关旧开新（HALCON 不支持改窗口尺寸）。
        /// </summary>
        private void EnsureWindow(int w, int h)
        {
            if (_window != null && w == _width && h == _height) return;

            CloseWindow();

            // open_window 参数：行, 列, 宽, 高, 父窗口, 模式, 机器名
            // mode 只给 'buffer'：离屏渲染，避免产线上突然闪出黑窗口
            HOperatorSet.OpenWindow(0, 0, w, h, "white", "buffer", "local", out HTuple win);
            _window = win;
            _width = w;
            _height = h;
        }

        /// <summary>
        /// 设置字号。格式参考仓库既有实现（Windows 面 HALCON 的字号串形如 -字体-字号-*-0-*-*-1-）。
        /// 失败直接吞掉：字号是"锦上添花"，绝不能因为它让整张标注图渲染不出来。
        /// </summary>
        private static void TrySetFont(HTuple win, int size)
        {
            try { HOperatorSet.SetFont(win, $"-Consolas-{size}-*-0-*-*-1-"); }
            catch { /* 用默认字号即可 */ }
        }

        /// <summary>
        /// 对象集里是否真有内容。空对象集没有"类型"可言，HALCON 画空对象合法（什么都不画），
        /// 但取类型会抛——所以先数个数再决定画不画。
        /// </summary>
        private static bool HasContent(HObject? o)
        {
            if (o == null || !o.IsInitialized()) return false;
            HOperatorSet.CountObj(o, out HTuple number);
            return number.Length > 0 && number[0].I > 0;
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
    }
}
