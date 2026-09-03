using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using VisionMaster.Services;

namespace VisionMaster.Lifetime.Checks
{
    /// <summary>
    /// 基础环境自检：AppConfig.json 可解析 + 日志目录可写。任一失败阻止启动。
    /// </summary>
    public class ConfigCheck : IStartupCheck
    {
        private readonly AppSettingsService _settings;
        private readonly ILogService _log;

        public ConfigCheck(AppSettingsService settings, ILogService log)
        {
            _settings = settings;
            _log = log;
        }

        public string Name => "配置与日志环境";

        public async Task<CheckResult> ExecuteAsync(CancellationToken ct)
        {
            // 1. AppConfig.json 加载（不存在时 Load 内部生成默认配置，不算失败）
            try
            {
                await Task.Run(() => _settings.Load(), ct);
            }
            catch (Exception ex)
            {
                return CheckResult.Fail(CheckLevel.Error, $"AppConfig.json 加载失败：{ex.Message}");
            }

            // 2. 日志目录可写
            try
            {
                string logDir = Path.Combine(AppContext.BaseDirectory, "Logs");
                Directory.CreateDirectory(logDir);
                string probe = Path.Combine(logDir, $".write_probe_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                return CheckResult.Fail(CheckLevel.Error, $"日志目录不可写：{ex.Message}");
            }

            return CheckResult.Ok($"方案清单 {_settings.Current.Solutions.Count} 项");
        }
    }
}
