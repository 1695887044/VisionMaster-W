using System;
using System.IO;
using System.Text;
using System.Text.Json;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 软件级配置服务（AppConfig.json，位于程序目录，与 Layout.xml 同级）
    /// 职责：方案清单与默认启动方案的读写，独立于解决方案文件
    /// </summary>
    public class AppSettingsService
    {
        private static readonly string ConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppConfig.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public AppSettingsService()
        {
            Load();
        }

        /// <summary>
        /// 当前软件配置（启动时自动从磁盘加载）
        /// </summary>
        public AppConfigModel Current { get; private set; } = new();

        /// <summary>
        /// 从磁盘加载（文件不存在或损坏时使用默认配置）
        /// </summary>
        public void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    Current = new AppConfigModel();
                    return;
                }
                var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                Current = JsonSerializer.Deserialize<AppConfigModel>(json, JsonOptions) ?? new AppConfigModel();
            }
            catch (Exception ex)
            {
                // 配置损坏不应阻断启动
                Console.WriteLine($"软件配置加载失败，使用默认配置。原因：{ex.Message}");
                Current = new AppConfigModel();
            }
        }

        /// <summary>
        /// 保存到磁盘（原子写：先写临时文件再替换，断电不丢旧配置）
        /// </summary>
        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(Current, JsonOptions);
                var tmp = ConfigPath + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                if (File.Exists(ConfigPath)) File.Replace(tmp, ConfigPath, null);
                else File.Move(tmp, ConfigPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"软件配置保存失败：{ex.Message}");
                throw;
            }
        }
    }
}
