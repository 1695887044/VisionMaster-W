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

            // 存量自愈：历史上 PopulateObject 的集合追加缺陷把 AppConfig 写成过 4096 / 16384 / 65536
            // 份同一个方案（每次 Load 翻一倍）。Load 已在内存里去重，这里回写一次让磁盘也干净——
            // 否则每次启动都要解析一个 27MB 的配置文件，而且方案列表/自检显示的数量仍是天文数字。
            // 回写失败不影响启动：内存里已经是干净清单，只是磁盘下次再清。
            if (_settings.LastLoadNormalized)
            {
                try
                {
                    _settings.Save();
                    _log.Info($"[启动自检] AppConfig 已清理重复/空白条目（方案清单现 {_settings.Current.Solutions.Count} 项）");
                }
                catch (Exception ex)
                {
                    _log.Warn($"[启动自检] AppConfig 清理后回写失败（不影响启动）：{ex.Message}");
                }
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
