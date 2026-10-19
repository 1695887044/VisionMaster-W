using System;
using System.Collections.Generic;

namespace IoChecks
{
    /// <summary>
    /// 测试用例记录：一条断言 = 用例 ID + 名称 + 预期 + 实际 + 通过与否。
    ///
    /// 为什么要结构化而不是只打 PASS/FAIL：
    /// 交付报告要能逐条回答"预期是什么、实际是什么、为什么判失败"；
    /// 只有 PASS/FAIL 时，失败项要靠人回去翻代码才能复原上下文。
    /// </summary>
    internal static class TestCase
    {
        private static readonly List<Row> _rows = new();
        private static int _failed;

        public sealed record Row(
            string Id,
            string Name,
            string Expected,
            string Actual,
            bool Passed,
            bool Skipped = false);

        public static IReadOnlyList<Row> Rows => _rows;
        public static int FailedCount => _failed;
        public static int PassedCount => _rows.Count - _failed - SkippedCount;
        public static int SkippedCount => _rows.FindAll(r => r.Skipped).Count;

        /// <summary>
        /// 断言一条。expected 写"应该是什么"，actual 写"实际观测到什么"。
        /// 判据由调用方给（condition），这样失败时不必再解析字符串。
        /// </summary>
        public static void Check(string id, string name, string expected, bool condition, string? actual = null)
        {
            var row = new Row(id, name, expected, actual ?? (condition ? "符合预期" : "(未提供实际值)"), condition);
            _rows.Add(row);

            if (condition)
            {
                Console.WriteLine($"[PASS] {id} {name}");
                return;
            }

            _failed++;
            Console.WriteLine($"[FAIL] {id} {name}");
            Console.WriteLine($"       预期：{expected}");
            Console.WriteLine($"       实际：{row.Actual}");
        }

        /// <summary>
        /// 记一条"环境不具备、未执行"的用例。
        ///
        /// 为什么要单独记而不是判 PASS：跳过和通过是两件事。判 PASS 会让汇总表
        /// 看起来"这条验过了"，而实际上一个字节都没跑（R38：跳过必须能被反证，
        /// 且必须在报告里显形）。所以这里既不进通过数、也不进失败数。
        /// </summary>
        public static void Skip(string id, string name, string reason)
        {
            _rows.Add(new Row(id, name, "(环境不具备，未执行)", reason, true, Skipped: true));
            Console.WriteLine($"[SKIP] {id} {name}");
            Console.WriteLine($"       原因：{reason}");
        }

        /// <summary>打印汇总表（按用例 ID 分组的第一段，如 D1/D2、V3）。</summary>
        public static void PrintSummary()
        {
            Console.WriteLine();
            Console.WriteLine("=== 用例汇总 ===");
            foreach (var group in GroupByPrefix())
            {
                int pass = 0, fail = 0, skip = 0;
                foreach (var row in group.Value)
                {
                    if (row.Skipped) skip++;
                    else if (row.Passed) pass++;
                    else fail++;
                }
                string line = $"  {group.Key}: 通过 {pass} / 失败 {fail}";
                if (skip > 0) line += $" / 跳过 {skip}";
                Console.WriteLine(line);
            }
            Console.WriteLine($"  合计: 通过 {PassedCount} / 失败 {_failed} / 跳过 {SkippedCount} / 共 {_rows.Count}");
            Console.WriteLine(_failed == 0 ? "RESULT: ALL_OK" : $"RESULT: {_failed} FAILED");
        }

        private static Dictionary<string, List<Row>> GroupByPrefix()
        {
            var map = new Dictionary<string, List<Row>>(StringComparer.Ordinal);
            foreach (var row in _rows)
            {
                var key = Prefix(row.Id);
                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<Row>();
                    map[key] = list;
                }
                list.Add(row);
            }
            return map;
        }

        /// <summary>取用例 ID 的首段（"D1.3" → "D1"）。</summary>
        private static string Prefix(string id)
        {
            int dot = id.IndexOf('.');
            return dot > 0 ? id.Substring(0, dot) : id;
        }
    }
}
