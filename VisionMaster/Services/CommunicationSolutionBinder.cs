using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Communications;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 通讯配置的方案级装配（"方案即唯一事实源"）。
    ///
    /// 背景与边界：
    /// - 改造前：连接配置存在机器级 <c>communications.json</c>，与方案无关；
    /// - 改造后：连接配置随方案走（<see cref="SolutionModel.CommunicationConfigs"/>），
    ///   打开/切换方案时整体替换管理器的连接集合，关闭方案时清空。
    ///
    /// 三条纪律：
    /// 1) <b>先停后换</b>：替换前先 DisconnectAll，避免旧连接的 Worker 线程与新连接抢同一
    ///    COM 口/TCP 端点（那是"两个进程抢串口"的进程内版本，表现为随机的打开失败）；
    /// 2) <b>逐条隔离</b>：某条配置非法只跳过它并记日志，不让整份方案打不开；
    /// 3) <b>不碰机器级覆盖</b>：license / 远程诊断这类"打开方案前就要用"的连接不在本类职责内。
    /// </summary>
    public sealed class CommunicationSolutionBinder
    {
        private readonly AdvancedCommunicationManager _manager;
        private readonly ILogService _log;

        public CommunicationSolutionBinder(AdvancedCommunicationManager manager, ILogService log)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>
        /// 用方案里的连接配置替换当前连接集合。返回 (成功, 失败) 条数。
        /// 方案为 null（无打开方案）时清空全部连接。
        /// </summary>
        public (int Loaded, int Failed) Apply(SolutionModel? solution, bool autoStart)
        {
            var configs = solution?.CommunicationConfigs?.ToList() ?? new List<CommunicationConfig>();

            // 先停：正在轮询的 Worker 必须先退出，否则旧连接仍占着端点。
            try { _manager.StopAll(); } catch (Exception ex) { _log.Warn($"[通讯] 切换方案前停止连接失败：{ex.Message}"); }

            // 再清：逐个移除（RemoveConnection 会停轮询、断链路、释放连接对象）
            foreach (var name in _manager.GetAllConnections().Select(c => c.ConnectionName).ToList())
            {
                try { _manager.RemoveConnection(name); }
                catch (Exception ex) { _log.Warn($"[通讯] 移除连接「{name}」失败：{ex.Message}"); }
            }

            int loaded = 0, failed = 0;
            foreach (var config in configs)
            {
                if (config == null) { failed++; continue; }

                if (!config.Validate(out var error))
                {
                    failed++;
                    _log.Warn($"[通讯] 方案里的连接「{config.ConnectionName}」配置无效，已跳过：{error}");
                    continue;
                }

                try
                {
                    _manager.AddConnection(config);
                    loaded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _log.Error($"[通讯] 按方案注册连接「{config.ConnectionName}」失败：{ex.Message}");
                }
            }

            if (autoStart && loaded > 0)
            {
                try { _manager.StartAll(); }
                catch (Exception ex) { _log.Error($"[通讯] 方案连接自动启动失败：{ex.Message}"); }
            }

            _log.Info($"[通讯] 方案通讯配置已装配：成功 {loaded} 条，跳过 {failed} 条");
            return (loaded, failed);
        }

        /// <summary>把管理器的当前连接写回方案（保存方案时调用）。</summary>
        public void Capture(SolutionModel? solution)
        {
            if (solution == null) return;
            solution.CommunicationConfigs =
                new System.Collections.ObjectModel.ObservableCollection<CommunicationConfig>(
                    _manager.GetAllConnections());
        }
    }
}
