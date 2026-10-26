using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace ScadaChecks
{
    /// <summary>
    /// 调试模式断言（2026-10-26）：
    ///   ① 未上线任何人可开；② 上线后仅「用户管理」可开；
    ///   ③ 开启状态<b>不按时间失效</b>，只有显式关闭才变 false；④ 开/关/被拒都落审计。
    ///
    /// 为什么断言里不碰 AppConfig.json：<see cref="AppSettingsService"/> 的路径是
    /// <c>AppDomain.BaseDirectory</c>，断言宿主写它会污染 bin（也破坏密闭性）。
    /// 所以这里直接构造 <see cref="DebugModeSettings"/> + 内存落盘口，
    /// 测的是 <see cref="DebugModeService"/> 的判定与写入口，不测文件格式。
    /// </summary>
    internal partial class Program
    {
        private static void DebugModeChecks()
        {
            Section("[DM] 调试模式：未上线免权限 / 上线需用户管理 / 不按时间失效");

            // ---- DM1~DM5：开发期（未上线）免权限 ----
            {
                var f = new DebugModeFixture();
                var service = f.Build(preReleaseBuild: true);

                Check("[DM1] 默认未开启", !service.IsEnabled, $"Enabled={service.IsEnabled}");
                Check("[DM2] 开发期未登录也可用", service.CanUse(out var reason), $"reason={reason}");
                Check("[DM3] 开发期阶段文案写明「未上线」",
                    service.StageText.Contains("未上线", StringComparison.Ordinal), service.StageText);

                var ok = service.TrySetEnabled(true, out var error);
                Check("[DM4] 开发期开启成功并落盘一次",
                    ok && f.Node.Enabled && f.Saves == 1, $"ok={ok}, Enabled={f.Node.Enabled}, saves={f.Saves}, err={error}");
                Check("[DM5] 开启记录写入了操作者与时间（仅展示）",
                    f.Node.EnabledBy == f.Actor && f.Node.EnabledAt.Length > 0,
                    $"{f.Node.EnabledBy} / {f.Node.EnabledAt}");
            }

            // ---- DM6~DM8：已上线 + 无权限 → 拒绝且不写盘 ----
            {
                var f = new DebugModeFixture { Allowed = false };
                f.Node.ProjectReleased = true;
                var service = f.Build(preReleaseBuild: true);

                Check("[DM6] 上线后无权限不可用", !service.CanUse(out var reason), $"reason={reason}");
                Check("[DM7] 拒绝原因点名「用户管理」",
                    (reason ?? string.Empty).Contains("用户管理", StringComparison.Ordinal), reason);

                var ok = service.TrySetEnabled(true, out var error);
                Check("[DM8] 无权限时开启被拒且不写盘",
                    !ok && !f.Node.Enabled && f.Saves == 0, $"ok={ok}, Enabled={f.Node.Enabled}, saves={f.Saves}, err={error}");
                Check("[DM9] 权限探测的 Key 是 UserAdministration",
                    f.ProbedKeys.Count > 0 && f.ProbedKeys.All(k => k == ScadaPermissionKeys.UserAdministration),
                    string.Join(",", f.ProbedKeys));
            }

            // ---- DM10~DM11：已上线 + 有权限 → 可开可关 ----
            {
                var f = new DebugModeFixture { Allowed = true };
                f.Node.ProjectReleased = true;
                var service = f.Build(preReleaseBuild: true);

                var on = service.TrySetEnabled(true, out var onError);
                Check("[DM10] 上线后有权限可开启",
                    on && f.Saves == 1 && f.Node.Enabled, $"ok={on}, saves={f.Saves}, err={onError}");

                var off = service.TrySetEnabled(false, out var offError);
                Check("[DM11] 关闭走同一条权限闸门且落盘",
                    off && !f.Node.Enabled && f.Saves == 2, $"ok={off}, Enabled={f.Node.Enabled}, saves={f.Saves}, err={offError}");
            }

            // ---- DM12：构建常量说"已上线"时，配置写 false 也翻不回去 ----
            {
                var f = new DebugModeFixture { Allowed = false };
                f.Node.ProjectReleased = false; // 配置想"降级"回开发期
                var service = f.Build(preReleaseBuild: false);

                Check("[DM12] 发版构建下配置 ProjectReleased=false 仍按上线处理",
                    service.ProjectReleased && !service.CanUse(out _),
                    $"ProjectReleased={service.ProjectReleased}");
            }

            // ---- DM13：不按时间失效（唯一一条"失效"判据是 Enabled 本身）----
            {
                var f = new DebugModeFixture { Allowed = true };
                f.Node.Enabled = true;
                f.Node.EnabledBy = "老张";
                f.Node.EnabledAt = "2000-01-01 00:00:00";
                var service = f.Build(preReleaseBuild: true);

                Check("[DM13] 2000 年开启的调试模式今天仍然有效（无到期时间）",
                    service.IsEnabled, $"EnabledAt={f.Node.EnabledAt}");
                Check("[DM14] 服务里不存在任何时间判据（EnabledAt 只回显）",
                    service.EnabledAt == "2000-01-01 00:00:00", service.EnabledAt);
            }

            // ---- DM15~DM17：审计 ----
            {
                var dir = Path.Combine(Path.GetTempPath(), "vm-debugmode-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var f = new DebugModeFixture { Allowed = false };
                    f.Node.ProjectReleased = true;
                    f.Audit = new ScadaAuditWriter(dir);
                    var service = f.Build(preReleaseBuild: true);

                    service.TrySetEnabled(true, out _);      // 被拒
                    f.Allowed = true;
                    service.TrySetEnabled(true, out _);      // 成功

                    var text = string.Join("\n", Directory.GetFiles(dir, "*.csv")
                        .Select(p => File.ReadAllText(p)));

                    Check("[DM15] 拒绝与成功都落审计",
                        text.Contains("调试模式", StringComparison.Ordinal)
                        && text.Contains("被拒绝", StringComparison.Ordinal)
                        && text.Contains("成功", StringComparison.Ordinal),
                        $"bytes={text.Length}");
                    Check("[DM16] 审计里带操作者",
                        text.Contains(f.Actor, StringComparison.Ordinal), f.Actor);
                }
                catch (Exception ex)
                {
                    Check("[DM15] 拒绝与成功都落审计", false, $"异常：{ex.GetType().Name}：{ex.Message}");
                    Check("[DM16] 审计里带操作者", false, "同上");
                }
                finally
                {
                    try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
                    catch { /* 临时目录清不掉不影响断言结论 */ }
                }
            }

            // ---- DM17：配置写失败不得抛（UI 线程调用，抛就退进程）----
            {
                var node = new DebugModeSettings();
                var service = new DebugModeService(
                    () => node,
                    (_, _, _) => throw new IOException("磁盘已满（模拟）"),
                    _ => new DebugPermissionResult(true, null),
                    () => "tester",
                    audit: null,
                    preReleaseBuild: true);

                var ok = service.TrySetEnabled(true, out var error);
                Check("[DM17] 写盘抛异常被收住并给出原因",
                    !ok && !string.IsNullOrEmpty(error) && !node.Enabled,
                    $"ok={ok}, err={error}");
            }
        }

        /// <summary>调试模式夹具：内存配置节 + 计数落盘口 + 可编程权限探测。</summary>
        private sealed class DebugModeFixture
        {
            public DebugModeSettings Node { get; } = new();

            public int Saves { get; private set; }

            public List<string> ProbedKeys { get; } = new();

            public bool Allowed { get; set; } = true;

            public string? DenyReason { get; set; } = "需要「用户管理」权限，当前是「操作员」";

            public string Actor { get; set; } = "tester";

            public ScadaAuditWriter? Audit { get; set; }

            public DebugModeService Build(bool preReleaseBuild)
                => new(
                    settings: () => Node,
                    persist: (enabled, actor, at) =>
                    {
                        Saves++;
                        Node.Enabled = enabled;
                        if (enabled)
                        {
                            Node.EnabledBy = actor;
                            Node.EnabledAt = at;
                        }
                        return true;
                    },
                    permissionProbe: key =>
                    {
                        ProbedKeys.Add(key);
                        return new DebugPermissionResult(Allowed, Allowed ? null : DenyReason);
                    },
                    actorProvider: () => Actor,
                    audit: Audit,
                    preReleaseBuild: preReleaseBuild);
        }
    }
}
