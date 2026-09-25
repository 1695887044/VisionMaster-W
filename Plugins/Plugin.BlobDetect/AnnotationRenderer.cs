using System;
using HalconDotNet;

namespace Plugin.BlobDetect
{
    /// <summary>
    /// 标注图渲染器：把"底图 + 缺陷红圈 + 左上角判定文字"合成成一张可显示的 HImage。
    ///
    /// 【为什么要开离屏窗口】
    /// HALCON 往图上写文字（disp_text）必须依附窗口的字库机制，没有"纯算子写文字"的路子；
    /// 而 paint_region 只能涂色块、画不了字。所以照搬仓库里 Plugin.ImageScript 已验证的做法：
    /// open_window 用 'buffer' 模式离屏渲染（不会在屏幕上闪出窗口），画完再用 dump_window_image 回读成 HImage。
    ///
    /// 【为什么窗口要缓存复用，而不是每次开/关】
    /// 仓库既有结论：HALCON 反复 open/close 窗口会 #9302 死锁。因此窗口按"图像尺寸"缓存，
    /// 尺寸不变就一直复用；尺寸变了才关旧开新。Dispose 时统一关闭。
    /// 每个插件实例各自持有一个窗口（配置预览实例、流程运行实例互不干扰），
    /// 并加锁防止预览线程与流程线程同时操作同一扇窗。
    /// </summary>
    internal sealed class AnnotationRenderer : IDisposable
    {
        // 同一实例可能被 UI 预览线程与流程线程先后调用，锁一次把"改窗口状态→画→回读"整段罩住，
        // 否则两路绘制会互相串色/串图（窗口状态是全局的）
        private readonly object _gate = new();

        private HTuple? _window;
        private int _width;
        private int _height;
        private bool _disposed;

        /// <summary>
        /// 渲染标注图。返回的 HImage 所有权交给调用方；失败返回 null（由调用方决定是否降级）。
        /// </summary>
        /// <param name="baseImage">底图（原图或灰度图，尺寸即画布尺寸；本方法不释放它，所有权在调用方）</param>
        /// <param name="defects">缺陷区域（可为 null 或空集，空集不画）</param>
        /// <param name="lines">左上角多行文字</param>
        /// <param name="textColor">文字颜色（HALCON 颜色名，如 "green"/"red"）</param>
        public HImage? Render(HObject baseImage, HObject? defects, string[] lines, string textColor)
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

                // 2. 缺陷描边（只画边界，不遮挡产品图像，便于人眼判断缺陷位置）
                if (HasContent(defects))
                {
                    HOperatorSet.SetDraw(win, "margin");
                    HOperatorSet.SetLineWidth(win, 2);
                    HOperatorSet.SetColor(win, "red");
                    HOperatorSet.DispObj(defects!, win);
                    HOperatorSet.SetLineWidth(win, 1);   // 还原，别把线宽状态带去下一张
                }

                // 3. 左上角文字：加白底框，保证在任何背景上都读得清
                TrySetFont(win, 20);
                HOperatorSet.DispText(
                    win,
                    new HTuple(lines),
                    "window",
                    12,
                    12,
                    textColor,
                    new HTuple("box_color"),
                    new HTuple("white"));

                // 4. 从离屏窗口回读成图像（这就是"标注图"）
                HOperatorSet.DumpWindowImage(out HObject shot, win);
                var result = new HImage(shot);
                // HALCON .NET 是引用计数语义：new HImage(shot) 生成的是独立句柄，但 shot 本身是"源对象"，
                // 不释放会留下一个引用计数（同期在卡尺插件上实测：500 次不释放源对象约漏 160MB）。
                // 本插件的标注渲染在配置预览与流程运行都会走到，长期运行会持续累积，故包装后立刻释放。
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
        /// 对象集里是否真有内容。空对象集（阈值/筛选后一个都没剩下）没有"类型"可言，
        /// HALCON 画空对象是合法的（什么都不画），但取类型会抛——所以先数个数再决定画不画。
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
