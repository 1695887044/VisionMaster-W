using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace VisionMaster.Services
{
    /// <summary>审计 CSV 的一行（已把方案标签剥成结构化字段）。</summary>
    public sealed record ScadaAuditRow(
        DateTime Timestamp, string Actor, string Subject, string EventText,
        string ActionText, string OutcomeText, string Detail,
        bool HasTag, Guid? SolutionId, bool UnsavedSolution, string? SolutionName,
        string DetailRemainder, string SourceFile, int LineNumber);

    public sealed record ScadaAuditReadResult(
        IReadOnlyList<ScadaAuditRow> Rows, int SkippedLines, IReadOnlyList<string> Files);

    /// <summary>
    /// 审计读取端。文件名判据<b>只复用</b> <see cref="ScadaAuditWriter.TryReadFileDate"/>，
    /// 不自己写一份前缀/日期格式——否则写读两端会各说各话，症状是「审计页签永远空的」。
    /// </summary>
    public sealed class ScadaAuditReader
    {
        public ScadaAuditReader(string? directory = null)
        {
            Directory = string.IsNullOrWhiteSpace(directory)
                ? ScadaAuditWriter.DefaultDirectory
                : directory;
        }

        public string Directory { get; }

        public static IReadOnlyList<string> HeaderColumns => ScadaAuditWriter.Header;

        public static string FileNameFor(DateTime day) => ScadaAuditWriter.SuggestFileName(day);

        public IReadOnlyList<string> EnumerateFiles()
        {
            if (!System.IO.Directory.Exists(Directory))
                return Array.Empty<string>();

            return System.IO.Directory.GetFiles(Directory)
                .Where(path => ScadaAuditWriter.TryReadFileDate(Path.GetFileName(path), out _))
                .OrderBy(path =>
                {
                    ScadaAuditWriter.TryReadFileDate(Path.GetFileName(path), out var day);
                    return day;
                })
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public ScadaAuditReadResult Read() => Read(DateTime.MinValue, DateTime.MaxValue);

        public ScadaAuditReadResult Read(DateTime fromDay, DateTime toDay)
        {
            var rows = new List<ScadaAuditRow>();
            var files = new List<string>();
            var skipped = 0;

            foreach (var path in EnumerateFiles())
            {
                if (!ScadaAuditWriter.TryReadFileDate(Path.GetFileName(path), out var day))
                    continue;
                if (day.Date < fromDay.Date || day.Date > toDay.Date)
                    continue;

                files.Add(path);
                string text;
                try { text = File.ReadAllText(path, Encoding.UTF8); }
                catch (IOException) { skipped++; continue; }
                catch (UnauthorizedAccessException) { skipped++; continue; }

                var records = ParseCsv(text);
                var first = true;
                foreach (var record in records)
                {
                    if (first && record.Fields.Count == 7
                        && string.Equals(record.Fields[0], ScadaAuditWriter.Header[0], StringComparison.Ordinal))
                    {
                        first = false;
                        continue;
                    }
                    first = false;

                    if (record.Fields.Count != 7
                        || !DateTime.TryParseExact(record.Fields[0], ScadaAuditWriter.TimeFormat,
                               CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                    {
                        skipped++;
                        continue;
                    }

                    var detail = record.Fields[6];
                    ScadaAuditSolutionTag.TryParse(detail, out var id, out var unsaved,
                        out var name, out var remainder);

                    rows.Add(new ScadaAuditRow(
                        timestamp,
                        record.Fields[1],
                        record.Fields[2],
                        record.Fields[3],
                        record.Fields[4],
                        record.Fields[5],
                        detail,
                        HasTag: detail.StartsWith("[sol=", StringComparison.Ordinal),
                        SolutionId: id,
                        UnsavedSolution: unsaved,
                        SolutionName: name,
                        DetailRemainder: remainder,
                        SourceFile: path,
                        LineNumber: record.LineNumber));
                }
            }

            return new ScadaAuditReadResult(rows, skipped, files);
        }

        private sealed record CsvRecord(int LineNumber, List<string> Fields);

        private static List<CsvRecord> ParseCsv(string text)
        {
            var records = new List<CsvRecord>();
            if (text.Length == 0) return records;

            var fields = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            var line = 1;
            var recordLine = 1;
            var i = 0;

            if (text[0] == '\uFEFF') i = 1;

            while (i < text.Length)
            {
                var c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;
                            continue;
                        }
                        inQuotes = false;
                        i++;
                        continue;
                    }

                    if (c == '\n') line++;
                    field.Append(c);
                    i++;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        i++;
                        break;
                    case ',':
                        fields.Add(field.ToString());
                        field.Clear();
                        i++;
                        break;
                    case '\r':
                        i++;
                        break;
                    case '\n':
                        fields.Add(field.ToString());
                        field.Clear();
                        if (fields.Count > 1 || fields[0].Length > 0)
                            records.Add(new CsvRecord(recordLine, new List<string>(fields)));
                        fields.Clear();
                        line++;
                        recordLine = line;
                        i++;
                        break;
                    default:
                        field.Append(c);
                        i++;
                        break;
                }
            }

            if (inQuotes)
            {
                records.Add(new CsvRecord(recordLine, new List<string> { field.ToString() }));
                return records;
            }

            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                if (fields.Count > 1 || fields[0].Length > 0)
                    records.Add(new CsvRecord(recordLine, fields));
            }

            return records;
        }
    }
}
