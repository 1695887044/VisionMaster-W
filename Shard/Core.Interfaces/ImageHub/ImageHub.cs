using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Core.Interfaces
{
    /// <summary>
    /// 网络图像中转站：宿主 HTTP 服务端"投递"，流程插件"取走"。
    ///
    /// 为什么是静态类
    /// ---------
    /// 投递方（宿主 HttpImageServer）与取走方（插件 ImageAcquisitionPlugin）分属两个程序集，
    /// 而插件是由 PluginService 以 Assembly.LoadFrom 装进**默认 AssemblyLoadContext** 的，
    /// 它引用的 Core.Interfaces 会命中宿主已经加载的那一份（同标识 → 同一个程序集实例）。
    /// 也就是说两边的 ImageHub 天然是同一个类型、同一份静态状态——
    /// 这正是选静态类而不是"往容器里注册一个单例"的原因：插件不参与宿主容器，
    /// 它拿不到任何注入，只能认静态口。
    ///
    /// 为什么按流程名分槽
    /// ---------
    /// 多条流程可以同时被 HTTP 触发（每条一个会话、各自独立运行）。若只有一个公共队列，
    /// A 流程的图会被 B 流程的采集步骤取走——现场表现是"图串了"，而且只在并发时偶发，最难查。
    /// 分槽后"谁投给谁"由流程名唯一确定，投递方与取走方用的是同一个名字来源
    /// （投递方 = URL 里的流程名，取走方 = IExecutionContext.CurrentFlowName）。
    ///
    /// 为什么每槽设上限
    /// ---------
    /// 投递方在流程跑完后就把结果回给客户端了，正常不会有积压；
    /// 但如果流程里压根没有"网络推送"采集步骤（用户把步骤删了/换了模式），
    /// 投进去的图就永远没人取。不设上限的话，一个循环发图的客户端能把内存吃干。
    /// 满了就丢**最旧**的一帧：对"实时检测"而言旧帧已无价值，丢新帧反而会让延迟越积越大。
    /// </summary>
    public static class ImageHub
    {
        /// <summary>每个流程槽的队列上限（超出时丢最旧的一帧）</summary>
        private const int MaxQueuedPerFlow = 8;

        /// <summary>流程名 → 该流程的待取图像队列</summary>
        private static readonly ConcurrentDictionary<string, ConcurrentQueue<HubImageItem>> _slots
            = new ConcurrentDictionary<string, ConcurrentQueue<HubImageItem>>(StringComparer.Ordinal);

        /// <summary>
        /// 流程名 → "有新帧推入"的通知信号（二元信号量，初值 0）。
        ///
        /// 为什么用信号量而不是轮询
        /// ---------
        /// 取图方（采集插件）要"等到有新图为止"。若用 Sleep 轮询，节拍快时白等、节拍慢时白转 CPU，
        /// 而且停止流程后最坏还要多转一圈才退得出来。信号量是事件驱动：Push 一 Release 立刻唤醒，
        /// 没人等时也不会占资源。容量取 1 就够——它只表达"槽里可能来了新东西"，
        /// 到底有没有图由队列说了算（醒来后一律回队列重试）。
        /// </summary>
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _signals
            = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        /// <summary>
        /// 投递一帧图（宿主 HTTP 服务端调用）。
        /// 流程名为空时归入 "" 槽，与 <see cref="TryPop"/> 的归一化规则一致。
        /// </summary>
        public static void Push(HubImageItem item)
        {
            if (item == null) return;

            var key = Normalize(item.FlowName);
            var queue = _slots.GetOrAdd(key, _ => new ConcurrentQueue<HubImageItem>());
            queue.Enqueue(item);

            // 超限丢最旧：只在"没人取"这种异常堆积下才会发生
            while (queue.Count > MaxQueuedPerFlow && queue.TryDequeue(out _))
            {
            }

            // 唤醒正在 WaitPop 上等图的取图方。
            // 没人等时信号量已满（计数 1），Release 会抛 SemaphoreFullException——那正是"无需唤醒"的情形，吞掉即可。
            var signal = _signals.GetOrAdd(key, _ => new SemaphoreSlim(0, 1));
            try { signal.Release(); }
            catch (SemaphoreFullException) { }
        }

        /// <summary>
        /// 取走该流程最新的一帧（插件采集步骤调用）。
        ///
        /// 注意取的是**最新**而不是最早：投递方是"发一张 → 等结果 → 再发下一张"的同步节奏，
        /// 正常队列里最多只有一帧；真有积压时，处理最新一帧对实时检测更有意义。
        /// </summary>
        /// <returns>取到返回 true；队列空返回 false（非阻塞口，调用方若想"等图"请用 <see cref="WaitPop"/>）</returns>
        public static bool TryPop(string flowName, out HubImageItem item)
        {
            item = null;
            if (!_slots.TryGetValue(Normalize(flowName), out var queue)) return false;

            // 排空到最后一帧，取走它
            HubImageItem latest = null;
            while (queue.TryDequeue(out var current))
                latest = current;

            if (latest == null) return false;
            item = latest;
            return true;
        }

        /// <summary>
        /// 阻塞取走该流程最新的一帧：槽里有图立即取走，没有就**一直等到有新图推来**为止。
        ///
        /// 为什么需要"等"这一档（与 TryPop 的分工）
        /// ---------
        /// TryPop 表达"现在有没有图"，WaitPop 表达"给我一张图"。采集步骤属于后者：
        /// 它的正常工作状态就是等图，等不到不算出错——报失败会让本步与后面所有吃图的步骤
        /// 连锁报错，把真正的根因（还没图）埋在一堆"输入图像为空"的噪音里。
        ///
        /// 唯一的退出方式
        /// ---------
        ///   1) 新图到达（正常路径）；
        ///   2) <paramref name="token"/> 被取消——即用户点了"停止流程"。
        /// 取消是"取消"而不是"失败"：本方法只返回 false 把决定权交回调用方，
        /// 自己绝不触碰流程控制状态，**不赋予取图方任何终止流程的能力**。
        /// </summary>
        /// <param name="flowName">流程名（与 Push 同口径归一化）</param>
        /// <param name="token">取消令牌（来自 IExecutionContext.CancellationToken，用于响应"停止流程"）</param>
        /// <param name="item">取到的图像帧；返回 false 时为 null</param>
        /// <returns>取到图返回 true；等待期间被取消返回 false</returns>
        public static bool WaitPop(string flowName, CancellationToken token, out HubImageItem item)
        {
            item = null;

            while (true)
            {
                // 快路径：宿主 HTTP 链路是"先 Push 再触发流程"，这里必然命中，零等待
                if (TryPop(flowName, out item)) return true;

                // 进入等待前先看一眼取消：避免"已停止还要先睡一下"的多余延迟
                if (token.IsCancellationRequested) return false;

                var signal = _signals.GetOrAdd(Normalize(flowName), _ => new SemaphoreSlim(0, 1));

                try
                {
                    signal.Wait(token);
                }
                catch (OperationCanceledException)
                {
                    return false; // 停止流程：交回调用方按取消语义处理
                }

                // 醒来后一律回队列重试：信号只代表"可能来了新帧"，
                // 也可能图已被并发的另一个取图者抢走——那就继续等下一帧
            }
        }

        /// <summary>当前该流程待取的图像帧数（诊断用）</summary>
        public static int Count(string flowName)
            => _slots.TryGetValue(Normalize(flowName), out var queue) ? queue.Count : 0;

        /// <summary>清空指定流程的待取队列（切换方案/流程重编译时清理陈旧帧）</summary>
        public static void Clear(string flowName)
        {
            if (_slots.TryGetValue(Normalize(flowName), out var queue))
            {
                while (queue.TryDequeue(out _))
                {
                }
            }
        }

        /// <summary>清空全部流程槽（宿主退出时调用）</summary>
        public static void ClearAll()
        {
            foreach (var key in new List<string>(_slots.Keys))
                Clear(key);
        }

        /// <summary>流程名归一化：null/空白统一成空串，保证 Push 与 TryPop 落在同一个槽上</summary>
        private static string Normalize(string flowName)
            => string.IsNullOrWhiteSpace(flowName) ? string.Empty : flowName;
    }
}
