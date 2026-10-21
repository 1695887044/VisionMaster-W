using System;
using System.IO;
using System.Linq;
using System.Text;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace ScadaChecks
{
    /// <summary>
    /// M2a 终审 P0 的落盘/通知回归（2026-10-21）。
    ///
    /// 覆盖：
    ///   A1 审计结果词「跳过」必须落成 CSV 的「未执行」，不能落成「失败」
    ///   A2 CSV 列序必须是 对象 / 事件 / 动作 / 结果（router 传参顺序曾整体反置）
    ///   A3 账号重置密码会触发 IScadaUserChangeNotifier（Web 模块据此踢掉旧会话）
    ///   A4 账号改角色会触发通知；改密码也会（同一条出口）
    /// </summary>
    internal static class M2AuditRegressionChecks
    {
        public static int Pass { get; private set; }

        public static int Fail { get; private set; }

        public static void Run()
        {
            Pass = 0;
            Fail = 0;

            Console.WriteLine();
            Console.WriteLine("--- M2AuditRegressionChecks：审计落盘 / 账号变更通知 ---");

            var dir = Path.Combine(Path.GetTempPath(), "ScadaM2Audit_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(dir);

                var writer = new ScadaAuditWriter(dir, retentionDays: 0);

                writer.Append(
                    actor: "alice",
                    sessionId: "s1",
                    clientAddress: "127.0.0.1",
                    subject: "启动按钮",
                    eventText: "Web 操作",
                    action: "Pressed",
                    result: "跳过",
                    detail: "动作被跳过");

                writer.Append(
                    actor: "alice",
                    sessionId: "s2",
                    clientAddress: "127.0.0.1",
                    subject: "对象A",
                    eventText: "Web 操作",
                    action: "Pressed",
                    result: "成功",
                    detail: "动作成功");

                var file = Directory.GetFiles(dir, "Audit-*.csv").Single();
                var lines = File.ReadAllLines(file, Encoding.UTF8);

                // 第一行是表头，第二行起是记录；本段只追加两条，顺序固定。
                var skipped = SplitCsv(lines.Length > 1 ? lines[1] : string.Empty);
                var success = SplitCsv(lines.Length > 2 ? lines[2] : string.Empty);

                Check("A1", skipped.Length >= 6 && skipped[5] == "未执行",
                    $"结果列={At(skipped, 5)}（期望 未执行；「跳过」不能落成失败）");

                Check("A2",
                    success.Length >= 6
                    && success[2] == "对象A"
                    && success[3] == "Web 操作"
                    && success[4] == "Pressed"
                    && success[5] == "成功",
                    $"列序 对象={At(success, 2)} 事件={At(success, 3)} 动作={At(success, 4)} 结果={At(success, 5)}");
            }
            catch (Exception ex)
            {
                Check("A1", false, $"异常：{ex.GetType().Name}：{ex.Message}");
                Check("A2", false, "见 A1 异常");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* 清理失败不影响判据 */ }
            }

            // A3/A4：账号写操作 → 变更通知（Web 模块订阅它来撤销旧会话）
            var userDir = Path.Combine(Path.GetTempPath(), "ScadaM2User_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(userDir);
                var store = new ScadaUserStore(Path.Combine(userDir, "users.json"));

                string? notifiedUser = null;
                string? notifiedReason = null;
                store.UserChanged += (name, reason) =>
                {
                    notifiedUser = name;
                    notifiedReason = reason;
                };

                var resetOk = store.TryResetPassword("admin", "NewPass#2026", out var resetError);
                Check("A3", resetOk && notifiedUser == "admin" && !string.IsNullOrWhiteSpace(notifiedReason),
                    $"reset={resetOk} err={resetError ?? "<无>"} user={notifiedUser ?? "<未通知>"} reason={notifiedReason ?? "<无>"}");

                notifiedUser = null;
                notifiedReason = null;

                var addOk = store.TryAddUser("op1", "OpPass#2026", ScadaRole.Operator, out var addError);
                string? roleError = null;
                var roleOk = addOk && store.TrySetRole("op1", ScadaRole.Engineer, out roleError);
                Check("A4", roleOk && notifiedUser == "op1" && !string.IsNullOrWhiteSpace(notifiedReason),
                    $"add={addOk} role={roleOk} err={addError ?? roleError ?? "<无>"} user={notifiedUser ?? "<未通知>"} reason={notifiedReason ?? "<无>"}");
            }
            catch (Exception ex)
            {
                Check("A3", false, $"异常：{ex.GetType().Name}：{ex.Message}");
                Check("A4", false, "见 A3 异常");
            }
            finally
            {
                try { Directory.Delete(userDir, recursive: true); } catch { /* 同上 */ }
            }
        }

        private static string At(string[] cells, int index)
            => index >= 0 && index < cells.Length ? cells[index] : "<缺列>";

        /// <summary>本段写入的字段都不含逗号/引号，按逗号切即可（不引入第三方 CSV 解析）</summary>
        private static string[] SplitCsv(string line) => line.Split(',');

        private static void Check(string id, bool ok, string detail)
        {
            if (ok) Pass++;
            else Fail++;

            Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + id + "  " + detail);
        }
    }
}
