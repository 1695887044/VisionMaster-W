namespace Core.Interfaces
{
    /// <summary>
    /// 数据记录 flush 桥：Plugin.DataRecord 加载时注册，Plugin.ExcelExport 读账本前调用。
    /// 用静态桥而不是插件互引，避免插件对插件 ProjectReference；
    /// 未注册（Excel 报表单独使用、流程里没有「CSV记录」）时视为无需等待。
    /// </summary>
    public static class DataRecordFlushBridge
    {
        /// <summary>flush 处理器：参数为超时毫秒，返回 false = 队列未排空/写盘失败。</summary>
        public static volatile Func<int, bool>? FlushHandler;

        /// <summary>等待 CSV 队列排空；未注册时直接放行。</summary>
        public static bool TryFlush(int timeoutMs)
        {
            var handler = FlushHandler;
            return handler == null || handler(timeoutMs);
        }
    }
}
