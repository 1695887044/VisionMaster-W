using System;
using System.Collections.Generic;
using HalconDotNet;

namespace Plugin.CodeReader
{
    /// <summary>
    /// 二维码识别引擎：create_data_code_2d_model + find_data_code_2d。
    ///
    /// 与一维引擎为什么必须分开
    /// ---------
    /// 两个家族的**结果寻址方式完全不同**，硬合会写出一堆 if(是一维) 分支：
    ///   一维：find_bar_code 只回一个字符串元组，要查"第几个是什么"得用**整数下标**
    ///   二维：find_data_code_2d 回 ResultHandles + DecodedDataStrings，要查细节得把**句柄**传回去
    ///
    /// 模型句柄的生命周期
    /// ---------
    /// **一个二维模型只能是一种码制**（码制是在 create 时定的，不是在 find 时传的）。
    /// 所以句柄按码制缓存：码制没变就复用，变了就 clear + create。
    /// 不做"每个码制一个句柄"的字典 —— 那会让配置界面试算时反复切换码制、句柄无限堆积。
    ///
    /// 参数每次 find 前重设一遍
    /// ---------
    /// set_*_param 只是往句柄上写几个字段，开销可忽略；而"改了配置却因为句柄被复用而没生效"
    /// 是极难查的一类问题（现象是"参数动了但结果不变"）。所以宁可每次重设，换掉这个坑。
    /// </summary>
    public sealed class DataCodeEngine : IDisposable
    {
        private HTuple _handle = new();
        private string _handleForSymbology = string.Empty;
        private string _lastCountNote = string.Empty;
        private bool _disposed;

        /// <summary>最近一次识别的计数备注（候选数与成功数不一致时非空），供日志</summary>
        public string LastCountNote => _lastCountNote;

        /// <summary>
        /// 识别。返回的 <paramref name="codes"/> 只含**成功解出内容**的码；
        /// 一个都没解出时返回 true 且列表为空 —— "没找到码"是正常工况，不是失败。
        /// </summary>
        public bool TryDecode(
            HObject? image,
            string halconSymbologyName,
            string polarity,
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
                handle = GetHandle(halconSymbologyName);
            }
            catch (Exception ex)
            {
                // 码制名拼错会走到这里。把可选值列出来，现场不用去翻文档
                error = $"建立「{halconSymbologyName}」二维码模型失败：{ex.Message}。"
                      + $"请确认码制名是否为：{CodeSymbologyTable.DescribeValidNames()}";
                return false;
            }

            HObject? symbols = null;
            try
            {
                ApplyParams(handle, polarity, timeoutMs);

                HOperatorSet.FindDataCode2d(
                    image,
                    out symbols,
                    handle,
                    new HTuple(),
                    new HTuple(),
                    out HTuple resultHandles,
                    out HTuple dataStrings
                );

                // 【为什么不直接相信 DecodedDataStrings】
                // HALCON 官方文档对这两个输出的口径自相矛盾：ResultHandles 写"只含成功解码的"，
                // DecodedDataStrings 写"含所有检测到的"。既然文档不一致，就不能按任一方写死逻辑。
                // 这里按"字符串非空"收码（解不出内容的候选，其字符串为空），
                // 并在数量不一致时把两个数都报出来 —— 宁可多一条诊断，也不要静默丢码。
                int handleCount = resultHandles.Length;
                int stringCount = dataStrings.Length;

                for (int i = 0; i < stringCount; i++)
                {
                    string text = dataStrings[i].S ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                        codes.Add(text);
                }

                // 数量不一致时留一条诊断（差异多半来自"找到候选但没解出内容"）。
                // 不抛错、不影响结果 —— 但它解释了"为什么图上有码却 Count=0"，是排障的关键线索。
                _lastCountNote =
                    handleCount == codes.Count
                        ? string.Empty
                        : $"二维候选 {stringCount} 个（句柄 {handleCount} 个），其中成功解出 {codes.Count} 个";

                return true;
            }
            catch (Exception ex)
            {
                error = $"二维码识别失败：{ex.Message}";
                return false;
            }
            finally
            {
                symbols?.Dispose();
            }
        }

        private void ApplyParams(HTuple handle, string polarity, int timeoutMs)
        {
            if (!string.IsNullOrWhiteSpace(polarity))
                HOperatorSet.SetDataCode2dParam(handle, "polarity", new HTuple(polarity));

            if (timeoutMs > 0)
                HOperatorSet.SetDataCode2dParam(handle, "timeout", new HTuple(timeoutMs));
        }

        private HTuple GetHandle(string halconSymbologyName)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_handle.Length > 0 && string.Equals(_handleForSymbology, halconSymbologyName, StringComparison.Ordinal))
                return _handle;

            if (_handle.Length > 0)
            {
                HOperatorSet.ClearDataCode2dModel(_handle);
                _handle = new HTuple();
            }

            HOperatorSet.CreateDataCode2dModel(
                new HTuple(halconSymbologyName),
                new HTuple(),
                new HTuple(),
                out _handle
            );
            _handleForSymbology = halconSymbologyName;
            return _handle;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_handle.Length > 0)
            {
                try { HOperatorSet.ClearDataCode2dModel(_handle); }
                catch { /* 释放失败不打断整体回收 */ }
                _handle = new HTuple();
            }
        }
    }
}
