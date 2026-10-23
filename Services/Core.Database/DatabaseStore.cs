using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Core.Database
{
    /// <summary>
    /// databases.json 的读写。
    ///
    /// 位置与 AppConfig.json 同级（exe 目录）：数据库 profile 是**安装级**配置，不是方案级资源，
    /// 不能随 .vms 走（.vms 只存 ProfileId/StatementId）。
    ///
    /// 写盘口径照 <c>Core\AppSettingsService.WriteReplacing</c>：先写 .tmp 再替换。
    /// **必须同时 catch IOException 与 UnauthorizedAccessException**（R180）：
    /// 本仓在受限环境里 File.Replace 会被拒，只 catch IOException 会让异常直接带崩宿主。
    /// </summary>
    public sealed class DatabaseStore
    {
        private readonly object _gate = new();
        private readonly string _path;
        private DatabaseFileModel? _model;

        public DatabaseStore(string? path = null)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "databases.json")
                : path!;
        }

        public string FilePath => _path;

        /// <summary>最近一次加载失败的原因（成功时为 null）。用于启动日志与页面提示。</summary>
        public string? LastLoadError { get; private set; }

        public DatabaseFileModel Load()
        {
            lock (_gate)
            {
                if (_model != null) return _model;

                LastLoadError = null;
                if (!File.Exists(_path))
                {
                    _model = new DatabaseFileModel();
                    return _model;
                }

                try
                {
                    var json = File.ReadAllText(_path, Encoding.UTF8);
                    _model = JsonConvert.DeserializeObject<DatabaseFileModel>(json) ?? new DatabaseFileModel();
                    Normalize(_model);
                    return _model;
                }
                catch (Exception ex)
                {
                    // 坏文件不删、不覆盖：留档后按空库继续，下一次保存会写新文件。
                    // 直接抛会让宿主在启动阶段少一个能力，而用户其实还能重建配置。
                    LastLoadError = $"读取 {Path.GetFileName(_path)} 失败：{ex.Message}";
                    TryArchiveBrokenFile();
                    _model = new DatabaseFileModel();
                    return _model;
                }
            }
        }

        /// <summary>保存（原子写）。失败抛异常，由调用方决定提示方式。</summary>
        public void Save(DatabaseFileModel model)
        {
            lock (_gate)
            {
                Normalize(model);

                var json = JsonConvert.SerializeObject(model, Formatting.Indented);
                var tmp = _path + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(tmp, json, new UTF8Encoding(false));

                if (File.Exists(_path)) WriteReplacing(tmp, _path);
                else File.Move(tmp, _path);

                _model = model;
                LastLoadError = null;
            }
        }

        /// <summary>丢弃内存缓存，下次 Load 重新读盘（测试/外部改文件后用）。</summary>
        public void Invalidate()
        {
            lock (_gate) _model = null;
        }

        private static void Normalize(DatabaseFileModel model)
        {
            model.Profiles ??= new();
            foreach (var profile in model.Profiles)
            {
                profile.Statements ??= new();
                profile.Name ??= string.Empty;
                profile.FilePath ??= string.Empty;
                if (profile.TimeoutSeconds <= 0) profile.TimeoutSeconds = 30;
                if (profile.MaxRows <= 0) profile.MaxRows = 1000;
                if (profile.ProfileId == Guid.Empty) profile.ProfileId = Guid.NewGuid();

                foreach (var statement in profile.Statements)
                {
                    statement.Parameters ??= new();
                    statement.Name ??= string.Empty;
                    statement.Sql ??= string.Empty;
                    if (statement.StatementId == Guid.Empty) statement.StatementId = Guid.NewGuid();
                    if (statement.ProfileId == Guid.Empty) statement.ProfileId = profile.ProfileId;

                    for (var i = 0; i < statement.Parameters.Count; i++)
                    {
                        var p = statement.Parameters[i];
                        if (p == null)
                        {
                            statement.Parameters.RemoveAt(i--);
                            continue;
                        }

                        if (!Core.Interfaces.Database.DbTypeNames.IsKnown(p.DbType))
                        {
                            statement.Parameters[i] = new Core.Interfaces.Database.DbStatementParameter
                            {
                                Name = p.Name,
                                DbType = Core.Interfaces.Database.DbTypeNames.Text,
                                IsRequired = p.IsRequired,
                                DefaultValue = p.DefaultValue ?? string.Empty,
                                Description = p.Description ?? string.Empty
                            };
                        }
                    }
                }
            }
        }

        private void TryArchiveBrokenFile()
        {
            try
            {
                var archive = _path + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(_path, archive, overwrite: true);
            }
            catch
            {
                // 留档失败不影响"按空库继续"这条主路径
            }
        }

        private static void WriteReplacing(string tmp, string dest)
        {
            try
            {
                File.Replace(tmp, dest, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // ReplaceFile 要求"能删除目标"；只读/占用/受限环境下会失败。
                // 覆盖写只要求写权限，正好补这一档（代价是冷路径上原子性略降）。
                File.Copy(tmp, dest, overwrite: true);
                try { File.Delete(tmp); } catch { /* 残留 .tmp 无害：下次覆盖写 */ }
            }
        }
    }
}
