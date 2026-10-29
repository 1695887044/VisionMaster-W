using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [V6] v6 四条动作（登录 / 注销 / 切换语言 / 延时）的执行侧。
        ///
        /// 钉的是四件事：
        /// ① 枚举值 / 显示名 / 接通状态（29~32，PendingReason 全为 null，且都不算危险动作）；
        /// ② 三条出口（登录窗 / 用户会话 / 语言服务）没接线时<b>如实说一句</b>，不假装成功；
        /// ③ 接线之后的成功 / 取消 / 失败三档各自落在哪一级日志上；
        /// ④ 「延时」唯一那条会改变"这一串动作怎么跑"的语义——当前调用返回时后续动作<b>还没跑</b>，
        ///    把消息循环推起来之后才跑，而且不阻塞 UI 线程。
        ///
        /// 为什么每条都断言"日志行数"而不只看内容：这三条出口的正确行为是"什么都不多做"，
        /// 只查内容会让"多记了一行""把后续动作提前跑了"这类错静默过关。
        /// </summary>
        private static class ActionV6Checks
        {
            public static void Run() => RunChecks();

            private static void RunChecks()
            {
                Section("v6 四条动作（登录 / 注销 / 切换语言 / 延时）");

                // ── ① 枚举与文案 ──
                Check("[V6-1] 枚举末尾追加 Logon=29 / Logoff=30 / SetLanguage=31 / Delay=32，"
                      + "显示名为「登录 / 注销 / 切换语言 / 延时」，四条都已接通且都不算危险动作",
                    (int)ScadaActionType.Logon == 29
                    && (int)ScadaActionType.Logoff == 30
                    && (int)ScadaActionType.SetLanguage == 31
                    && (int)ScadaActionType.Delay == 32
                    && ScadaActionType.Logon.DisplayName() == "登录"
                    && ScadaActionType.Logoff.DisplayName() == "注销"
                    && ScadaActionType.SetLanguage.DisplayName() == "切换语言"
                    && ScadaActionType.Delay.DisplayName() == "延时"
                    && ScadaActionType.Logon.PendingReason() == null
                    && ScadaActionType.Logoff.PendingReason() == null
                    && ScadaActionType.SetLanguage.PendingReason() == null
                    && ScadaActionType.Delay.PendingReason() == null
                    // 登录/注销改的是"谁在操作"，不动物理设备；切换语言与延时更是纯界面副作用。
                    // 把任意一条塞进 IsDangerous 都会让普通按钮莫名弹出红色确认框。
                    && !ScadaActionType.Logon.IsDangerous()
                    && !ScadaActionType.Logoff.IsDangerous()
                    && !ScadaActionType.SetLanguage.IsDangerous()
                    && !ScadaActionType.Delay.IsDangerous(),
                    $"{(int)ScadaActionType.Logon}/{(int)ScadaActionType.Logoff}/"
                    + $"{(int)ScadaActionType.SetLanguage}/{(int)ScadaActionType.Delay}");

                // ── ② 登录：没接线 / 取消 / 成功 ──
                var logonUnwired = Dispatch(actions: new ScadaAction { Type = ScadaActionType.Logon });
                Check("[V6-2] 登录没接登录窗：Warn 一行含「未接入登录窗」，不假装成功",
                    logonUnwired.Lines.Count == 1
                    && logonUnwired.Lines[0].StartsWith("W ", StringComparison.Ordinal)
                    && logonUnwired.Lines[0].Contains("未接入登录窗", StringComparison.Ordinal),
                    logonUnwired.Lines.Count == 1 ? logonUnwired.Lines[0] : "no log");

                int promptCalls = 0;
                string? promptArgument = "未调用";
                var logonOk = Dispatch(
                    loginPrompt: _ =>
                    {
                        promptCalls++;
                        promptArgument = _;
                        return true;
                    },
                    actions: new ScadaAction { Type = ScadaActionType.Logon });
                Check("[V6-3] 登录窗接线且登录成功：调一次（参数为 null），日志一行 Info「已登录」",
                    promptCalls == 1
                    && promptArgument == null
                    && logonOk.Lines.Count == 1
                    && logonOk.Lines[0].StartsWith("I ", StringComparison.Ordinal)
                    && logonOk.Lines[0].Contains("已登录", StringComparison.Ordinal),
                    $"calls={promptCalls} / {(logonOk.Lines.Count == 1 ? logonOk.Lines[0] : "no log")}");

                var logonCancelled = Dispatch(
                    loginPrompt: _ => false,
                    actions: new ScadaAction { Type = ScadaActionType.Logon });
                Check("[V6-4] 登录窗接线但用户取消/没通过：记为跳过（Warn），不写成成功",
                    logonCancelled.Lines.Count == 1
                    && logonCancelled.Lines[0].StartsWith("W ", StringComparison.Ordinal)
                    && logonCancelled.Lines[0].Contains("登录被取消或未通过", StringComparison.Ordinal),
                    logonCancelled.Lines.Count == 1 ? logonCancelled.Lines[0] : "no log");

                // ── ③ 注销：没接线 / 成功（要写出原用户名）──
                var logoffUnwired = Dispatch(actions: new ScadaAction { Type = ScadaActionType.Logoff });
                Check("[V6-5] 注销没接用户会话：Warn 一行含「未接入用户会话」",
                    logoffUnwired.Lines.Count == 1
                    && logoffUnwired.Lines[0].StartsWith("W ", StringComparison.Ordinal)
                    && logoffUnwired.Lines[0].Contains("未接入用户会话", StringComparison.Ordinal),
                    logoffUnwired.Lines.Count == 1 ? logoffUnwired.Lines[0] : "no log");

                var session = new FakeUserSession { CurrentUserName = "张三" };
                var logoffOk = Dispatch(
                    userSession: session,
                    actions: new ScadaAction { Type = ScadaActionType.Logoff });
                Check("[V6-6] 注销成功：日志含原用户名「张三」（注销前取，注销后那个名字已经没了）",
                    session.LogoutCalls == 1
                    && session.CurrentUserName == null
                    && logoffOk.Lines.Count == 1
                    && logoffOk.Lines[0].StartsWith("I ", StringComparison.Ordinal)
                    && logoffOk.Lines[0].Contains("已注销", StringComparison.Ordinal)
                    && logoffOk.Lines[0].Contains("张三", StringComparison.Ordinal),
                    $"calls={session.LogoutCalls} / user={session.CurrentUserName ?? "null"} / "
                    + $"{(logoffOk.Lines.Count == 1 ? logoffOk.Lines[0] : "no log")}");

                // ── ④ 切换语言：按 Id / 按名 / 目标不存在 / 没接线 ──
                var chinese = new ScadaLanguage { Name = "简体中文" };
                var english = new ScadaLanguage { Name = "English" };

                var byId = Dispatch(
                    languages: new FakeLanguageService(chinese, english),
                    actions: new ScadaAction
                    {
                        Type = ScadaActionType.SetLanguage,
                        LanguageId = english.LanguageId,
                        // 名字故意填一个过期的：Id 优先，名字只作兜底
                        LanguageName = "旧名字",
                    });
                Check("[V6-7] 切换语言按 Id 成功：日志写的是切换后的当前语言名「English」",
                    byId.Lines.Count == 1
                    && byId.Lines[0].StartsWith("I ", StringComparison.Ordinal)
                    && byId.Lines[0].Contains("切换语言 → English", StringComparison.Ordinal),
                    byId.Lines.Count == 1 ? byId.Lines[0] : "no log");

                var byName = Dispatch(
                    languages: new FakeLanguageService(chinese, english),
                    actions: new ScadaAction { Type = ScadaActionType.SetLanguage, LanguageName = "English" });
                Check("[V6-8] 切换语言按名字成功（Id 为空时的兜底）：日志含「切换语言 → English」",
                    byName.Lines.Count == 1
                    && byName.Lines[0].StartsWith("I ", StringComparison.Ordinal)
                    && byName.Lines[0].Contains("切换语言 → English", StringComparison.Ordinal),
                    byName.Lines.Count == 1 ? byName.Lines[0] : "no log");

                var missing = Dispatch(
                    languages: new FakeLanguageService(chinese, english),
                    actions: new ScadaAction { Type = ScadaActionType.SetLanguage, LanguageName = "法语" });
                Check("[V6-9] 切换语言目标不存在：Error 一行，原因由语言服务带上来（不静默留在原语言）",
                    missing.Lines.Count == 1
                    && missing.Lines[0].StartsWith("E ", StringComparison.Ordinal)
                    && missing.Lines[0].Contains("法语", StringComparison.Ordinal),
                    missing.Lines.Count == 1 ? missing.Lines[0] : "no log");

                var languageUnwired = Dispatch(
                    actions: new ScadaAction { Type = ScadaActionType.SetLanguage, LanguageName = "English" });
                Check("[V6-10] 切换语言没接语言服务：Warn 一行含「未接入语言服务」",
                    languageUnwired.Lines.Count == 1
                    && languageUnwired.Lines[0].StartsWith("W ", StringComparison.Ordinal)
                    && languageUnwired.Lines[0].Contains("未接入语言服务", StringComparison.Ordinal),
                    languageUnwired.Lines.Count == 1 ? languageUnwired.Lines[0] : "no log");

                // ── ⑤ 延时 ──
                // 本断言宿主是控制台程序，主线程默认还没有 Dispatcher（Dispatcher.FromThread 会是 null）。
                // 先摸一下 CurrentDispatcher 把消息循环建起来——「延时」要的正是"有 Dispatcher 的 UI 线程"。
                _ = Dispatcher.CurrentDispatcher;

                var delayEmpty = Dispatch(
                    actions: new[]
                    {
                        new ScadaAction { Type = ScadaActionType.Delay },
                        new ScadaAction { Type = ScadaActionType.Log, Text = "没填毫秒也要跑的一行" },
                    });
                Check("[V6-11] 延时没填毫秒：Warn 一行，且后续动作照常立刻执行（一条配错的延时不许把整串卡住）",
                    delayEmpty.Lines.Count == 2
                    && delayEmpty.Lines[0].StartsWith("W ", StringComparison.Ordinal)
                    && delayEmpty.Lines[0].Contains("延时未填毫秒", StringComparison.Ordinal)
                    && delayEmpty.Lines[1].Contains("没填毫秒也要跑的一行", StringComparison.Ordinal),
                    string.Join(" | ", delayEmpty.Lines));

                var delayLog = Dispatch(
                    actions: new[]
                    {
                        new ScadaAction { Type = ScadaActionType.Delay, DelayMs = 50 },
                        new ScadaAction { Type = ScadaActionType.Log, Text = "延时后才写的一行" },
                    });
                Check("[V6-12] 延时排上队：当前调用只落一条 Ok（写清剩余 1 条），后续动作此刻还没跑",
                    delayLog.Lines.Count == 1
                    && delayLog.Lines[0].StartsWith("I ", StringComparison.Ordinal)
                    && delayLog.Lines[0].Contains("剩余 1 条将在延时后执行", StringComparison.Ordinal),
                    delayLog.Lines.Count == 1 ? delayLog.Lines[0] : string.Join(" | ", delayLog.Lines));

                // 把消息循环推起来（不阻塞 UI 线程：DispatcherTimer 到点自己跳一拍）
                PumpDispatcher(300);

                Check("[V6-13] 到点后剩余动作真的跑了：多出一行，且带「（延时后）」标记（能一眼看出是延时后的半截）",
                    delayLog.Lines.Count == 2
                    && delayLog.Lines[1].StartsWith("I ", StringComparison.Ordinal)
                    && delayLog.Lines[1].Contains("延时后才写的一行", StringComparison.Ordinal)
                    && delayLog.Lines[1].Contains("（延时后）", StringComparison.Ordinal),
                    string.Join(" | ", delayLog.Lines));

                // 非 UI 线程：Dispatcher.FromThread 为 null → 跳过，后续动作照常立刻执行
                LoggerStub? offThreadLog = null;
                var worker = new Thread(() =>
                {
                    var log = new LoggerStub();
                    var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                    hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Delay, DelayMs = 50 });
                    hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "非 UI 线程上不该被延时" });

                    new ScadaActionDispatcher(log, new FakeValueSource(), new FakeNavigator())
                        .Dispatch(hook, "按钮");

                    offThreadLog = log;
                })
                {
                    IsBackground = true,
                };
                worker.Start();
                worker.Join(5000);

                Check("[V6-14] 非 UI 线程（拿不到 Dispatcher）：Warn「延时只能在 UI 线程上排队」，后续动作照常立刻执行",
                    offThreadLog != null
                    && offThreadLog.Lines.Count == 2
                    && offThreadLog.Lines[0].StartsWith("W ", StringComparison.Ordinal)
                    && offThreadLog.Lines[0].Contains("延时只能在 UI 线程上排队", StringComparison.Ordinal)
                    && offThreadLog.Lines[1].Contains("非 UI 线程上不该被延时", StringComparison.Ordinal),
                    offThreadLog == null ? "工作线程未在 5s 内跑完" : string.Join(" | ", offThreadLog.Lines));
            }

            /// <summary>
            /// 造一份分发器并把一串动作派发出去。三条 v6 出口都是可选参数（默认 null = 没接线），
            /// 断言正是靠"给不给它"来覆盖"未接入"与"接入"两档。
            /// </summary>
            private static LoggerStub Dispatch(
                Func<string?, bool>? loginPrompt = null,
                IScadaUserSession? userSession = null,
                IScadaLanguageService? languages = null,
                params ScadaAction[] actions)
            {
                var log = new LoggerStub();
                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                foreach (var action in actions)
                    hook.Actions.Add(action);

                new ScadaActionDispatcher(
                    log,
                    new FakeValueSource(),
                    new FakeNavigator(),
                    loginPrompt: loginPrompt,
                    userSession: userSession,
                    languages: languages).Dispatch(hook, "按钮");

                return log;
            }

            /// <summary>假的用户会话：注销后把名字清掉，用来证明执行侧确实在<b>注销前</b>取了名字</summary>
            private sealed class FakeUserSession : IScadaUserSession
            {
                public string? CurrentUserName { get; set; } = "张三";

                public bool IsLoggedIn => CurrentUserName != null;

                public int LogoutCalls { get; private set; }

                public bool TryLogout(out string? error)
                {
                    LogoutCalls++;
                    error = null;
                    CurrentUserName = null;
                    return true;
                }
            }

            /// <summary>
            /// 假的语言服务：两张表、当前语言可切、找不到时给可上屏的原因。
            /// 按 Id / 按名字两条入口都照真实现的口径（Id 优先、名字兜底）实现，
            /// 这样"执行侧走的是哪一条"能从日志里的结果反推出来。
            /// </summary>
            private sealed class FakeLanguageService : IScadaLanguageService
            {
                private Guid _current;

                public FakeLanguageService(params ScadaLanguage[] languages)
                    => Languages = languages ?? Array.Empty<ScadaLanguage>();

                public IReadOnlyList<ScadaLanguage> Languages { get; }

                public Guid CurrentLanguageId => _current;

                public string CurrentLanguageName
                    => ScadaTextCatalog.FindById(Languages, _current)?.Name ?? "未选择语言";

                public event Action? Changed;

                public bool TrySetLanguage(Guid languageId, out string? error)
                {
                    var language = ScadaTextCatalog.FindById(Languages, languageId);
                    if (language == null)
                    {
                        error = $"找不到语言表（{languageId:N}）";
                        return false;
                    }

                    _current = language.LanguageId;
                    error = null;
                    Changed?.Invoke();
                    return true;
                }

                public bool TrySetLanguage(string? languageName, out string? error)
                {
                    var language = ScadaTextCatalog.FindByName(Languages, languageName);
                    if (language == null)
                    {
                        error = $"找不到语言表「{languageName}」";
                        return false;
                    }

                    _current = language.LanguageId;
                    error = null;
                    Changed?.Invoke();
                    return true;
                }

                public string Resolve(string? text)
                    => ScadaTextCatalog.Resolve(text, Languages, _current);
            }
        }
    }
}
