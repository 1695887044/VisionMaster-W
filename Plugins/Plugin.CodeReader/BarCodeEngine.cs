using System;
using System.Collections.Generic;
using HalconDotNet;

namespace Plugin.CodeReader
{
    /// <summary>
    /// 一维条码识别引擎：create_bar_code_model + find_bar_code。
    ///
    /// 与二维引擎的一处关键差别
    /// ---------
    /// **一维的模型句柄不绑定码制**：create_bar_code_model 只吃通用参数，
    /// 码制是 find_bar_code 的入参。所以句柄建一次就够，切码制不用重建。
    /// （二维正相反：码制在 create 时定死，切码制必须重建 —— 见 DataCodeEngine。）
    ///
    /// 为什么不用 decode_bar_code_rectangle2（"已知位置直接解码"）
    /// ---------
    /// 那条路要求矩形**角度也准**，现场调参成本高；而本插件按用户要求不做定位（上级裁好小图再传进来），
    /// 没有"已知位置"这个前提。reduce_domain 已能拿到大部分提速收益。
    /// 等真测出节拍不够再加 —— 那时是有数据支撑的优化，不是猜的。
    /// </summary>
    public sealed class BarCodeEngine : IDisposable
    {
        private HTuple _handle = new();
        private bool _disposed;

        /// <summary>
        /// 识别。返回的 <paramref name="codes"/> 只含成功解出的码；
        /// 一个都没解出时返回 true 且列表为空 —— "没找到码"是正常工况，不是失败。
        /// </summary>
        /// <param name="halconCodeType">
        /// find_bar_code 的 CodeType：具体码制名（'Code 128' 等）或 'auto'。
        /// 'auto' 会让 HALCON 在内部逐个码制试，节拍明显更慢；码制固定时应明确指定。
        /// </param>
        /// <remarks>
        /// 【为什么这里没有"极性"参数】实测：一维模型的 set_bar_code_param 不接受 'polarity'
        /// （HALCON 报 #3286 Wrong generic parameter name），而二维模型的同名参数是接受的 ——
        /// 一维读码本身对极性鲁棒，压根没有这个旋钮。
        /// 实测被接受的 1D 参数名（逐个试出来的，不是猜的）：
        ///   element_size_min / element_size_max / num_scanlines / orientation / quiet_zone /
        ///   start_stop_tolerance / check_char / meas_thresh / min_identical_scanlines /
        ///   persistence / timeout / composite_code
        /// 本引擎目前只用了 timeout —— 其余在没实测需求前不暴露（多一个旋钮就多一处要现场理解的东西）。
        /// 这条差异也是配置界面"参数按家族分派"的硬依据：1D 面板不该出现极性项。
        /// </remarks>
        public bool TryDecode(
            HObject? image,
            string halconCodeType,
            int timeoutMs,
            out List<string> codes,
            out string error
        )
        {
            codes = new List<string>();
            error = string.Empty;

            if (image == null)
            {
                error = "输入图像为空";
                return false;
            }

            HTuple handle;
            try
            {
                handle = GetHandle();
            }
            catch (Exception ex)
            {
                error = $"建立一维条码模型失败：{ex.Message}";
                return false;
            }

            HObject? symbolRegions = null;
            try
            {
                ApplyParams(handle, timeoutMs);

                HOperatorSet.FindBarCode(
                    image,
                    out symbolRegions,
                    handle,
                    new HTuple(halconCodeType),
                    out HTuple dataStrings
                );

                for (int i = 0; i < dataStrings.Length; i++)
                {
                    string text = dataStrings[i].S ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                        codes.Add(text);
                }

                return true;
            }
            catch (Exception ex)
            {
                // 码制名拼错会走到这里（HALCON 直接报错）。把可选值列出来，现场不用翻文档
                error = $"一维条码识别失败（码制 '{halconCodeType}'）：{ex.Message}。"
                      + $"请确认码制是否为：{CodeSymbologyTable.DescribeValidNames()}";
                return false;
            }
            finally
            {
                symbolRegions?.Dispose();
            }
        }

        private void ApplyParams(HTuple handle, int timeoutMs)
        {
            // 只设超时：一维搜索本身很快，但图里没有码时 HALCON 会在候选区域上反复尝试，
            // 没有超时保护会拖住整条流程。极性等参数一维模型不接受，见 TryDecode 的 remarks。
            if (timeoutMs > 0)
                HOperatorSet.SetBarCodeParam(handle, "timeout", new HTuple(timeoutMs));
        }

        private HTuple GetHandle()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_handle.Length > 0) return _handle;

            HOperatorSet.CreateBarCodeModel(new HTuple(), new HTuple(), out _handle);
            return _handle;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_handle.Length > 0)
            {
                try { HOperatorSet.ClearBarCodeModel(_handle); }
                catch { /* 释放失败不打断整体回收 */ }
                _handle = new HTuple();
            }
        }
    }
}
