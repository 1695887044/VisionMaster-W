using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace VisionMaster.Services
{
    /// <summary>
    /// 报警历史的<b>方案级</b>落盘端：把“上次加载时看到的报警”存到解决方案自己的
    /// <c>Alarms\AlarmHistory.json</c>，下次打开同一份方案时再读回来。
    ///
    /// 与 <see cref="ScadaAlarmHistoryWriter"/> 的分工
    /// ---------
    /// · CSV（<c>AlarmHistory-yyyy-MM-dd.csv</c>）是给人和 Excel 看的<b>事件流水</b>，
    ///   一行一次状态迁移，只追加、按天滚动；
    /// · 本类（<c>AlarmHistory.json</c>）是给报警历史面板恢复用的<b>当前记录快照</b>，
    ///   一条报警一行、每次状态变化整体重写。面板要的是“这条报警现在是什么态”，
    ///   而不是自己把 CSV 里的迁移行重新拼一遍——那既容易拼错，也解释不了旧格式缺列。
    ///
    /// 为什么放在方案目录而不是软件目录
    /// ---------
    /// 报警历史与画面、变量、图片一样属于“这份方案自己的运行资料”。放在
    /// <c>Path.GetDirectoryName(SolutionFilePath)\Alarms</c> 之后，复制/移动整个方案文件夹
    /// 就带走了自己的历史；同一台机器上的两条产线也不会互相看到对方的报警。
    ///
    /// 线程
    /// ---------
    /// 报警事件可能来自变量轮询线程（见 <see cref="ScadaAlarmEngine"/> 的线程约定），
    /// 所以读写都过 <see cref="_gate"/>；文件写用临时文件 + Replace，断电不会留下半份 JSON。
    /// </summary>
    public sealed class ScadaAlarmHistoryStore
    {
        /// <summary>最多保留多少条记录。与引擎内存历史上限、历史面板的 MaxRows 对齐</summary>
        public const int MaxRecords = 2000;

        private const string FileName = "AlarmHistory.json";

        private readonly string _path;
        private readonly Action<ScadaDiagnosticLevel, string>? _diagnostic;
        private readonly object _gate = new();

        public ScadaAlarmHistoryStore(string directory, Action<ScadaDiagnosticLevel, string>? diagnostic = null)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("报警历史目录不能为空", nameof(directory));

            Directory = directory;
            _path = Path.Combine(directory, FileName);
            _diagnostic = diagnostic;
        }

        /// <summary>本方案自己的报警历史目录（<c>&lt;方案目录&gt;\Alarms</c>）</summary>
        public string Directory { get; }

        /// <summary>快照文件全路径（诊断与断言用）</summary>
        public string FilePath => _path;

        /// <summary>
        /// 解析方案自己的报警目录：<c>Path.GetDirectoryName(SolutionFilePath)\Alarms</c>。
        /// 未保存的方案没有路径，回落到软件目录下的 <c>Alarms</c>（与旧行为一致，至少不丢数据）。
        /// </summary>
        public static string ResolveDirectory(SolutionModel? solution)
        {
            var solutionPath = solution?.SolutionFilePath;
            if (!string.IsNullOrWhiteSpace(solutionPath))
            {
                var root = Path.GetDirectoryName(solutionPath);
                if (!string.IsNullOrWhiteSpace(root))
                    return Path.Combine(root, "Alarms");
            }

            return Path.Combine(AppContext.BaseDirectory, "Alarms");
        }

        /// <summary>
        /// 把“本轮记录”和“上次读回的历史”合成一份写回快照。
        /// 本轮在前：同一 <see cref="ScadaAlarmRecord.RecordId"/> 出现两次时保留本轮的最新状态。
        /// </summary>
        public static IReadOnlyList<ScadaAlarmRecord> Merge(
            IEnumerable<ScadaAlarmRecord>? prior,
            IEnumerable<ScadaAlarmRecord>? current)
        {
            var merged = new List<ScadaAlarmRecord>();

            if (current != null)
                merged.AddRange(current.Where(r => r != null));

            if (prior != null)
                merged.AddRange(prior.Where(r => r != null));

            return merged
                .GroupBy(r => r.RecordId)
                .Select(g => g.First())
                .OrderByDescending(r => r.ActivatedAtUtc)
                .Take(MaxRecords)
                .ToArray();
        }

        /// <summary>
        /// 读回上次加载时保存的记录，<b>最新在前</b>。文件不存在/损坏时返回空表并报一条诊断——
        /// 历史读不出来不该拦住运行，但也不能一声不响。
        /// </summary>
        public IReadOnlyList<ScadaAlarmRecord> Load(int max = MaxRecords)
        {
            if (max <= 0)
                return Array.Empty<ScadaAlarmRecord>();

            lock (_gate)
            {
                if (!File.Exists(_path))
                    return Array.Empty<ScadaAlarmRecord>();

                try
                {
                    var json = File.ReadAllText(_path, Encoding.UTF8);
                    var snapshots = JsonConvert.DeserializeObject<List<ScadaAlarmRecordSnapshot>>(json);
                    if (snapshots == null || snapshots.Count == 0)
                        return Array.Empty<ScadaAlarmRecord>();

                    return snapshots
                        .Where(s => s != null)
                        .OrderByDescending(s => s.ActivatedAtUtc)
                        .Take(max)
                        .Select(s => s.ToRecord())
                        .ToArray();
                }
                catch (Exception ex)
                {
                    _diagnostic?.Invoke(ScadaDiagnosticLevel.Warning,
                        $"[报警历史] 读取方案历史失败：{ex.Message}");
                    return Array.Empty<ScadaAlarmRecord>();
                }
            }
        }

        /// <summary>
        /// 把当前记录整体写成快照。返回是否写成功；失败只报诊断，不抛——
        /// 落盘失败不该让一次报警从运行态消失（与 <see cref="ScadaAlarmHistoryWriter"/> 同一口径）。
        /// </summary>
        public bool Save(IEnumerable<ScadaAlarmRecord> records)
        {
            if (records == null)
                return false;

            lock (_gate)
            {
                try
                {
                    var snapshots = records
                        .Where(r => r != null)
                        .OrderByDescending(r => r.ActivatedAtUtc)
                        .Take(MaxRecords)
                        .Select(ScadaAlarmRecordSnapshot.FromRecord)
                        .ToList();

                    System.IO.Directory.CreateDirectory(Directory);

                    var json = JsonConvert.SerializeObject(snapshots, Formatting.None);
                    var tmp = _path + ".tmp";

                    try
                    {
                        File.WriteAllText(tmp, json, new UTF8Encoding(false));
                        if (File.Exists(_path))
                            File.Replace(tmp, _path, null);
                        else
                            File.Move(tmp, _path);
                    }
                    catch
                    {
                        try { if (File.Exists(tmp)) File.Delete(tmp); }
                        catch { /* 清理失败不影响主流程 */ }
                        throw;
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    _diagnostic?.Invoke(ScadaDiagnosticLevel.Warning,
                        $"[报警历史] 写入方案历史失败：{ex.Message}");
                    return false;
                }
            }
        }
    }
}
