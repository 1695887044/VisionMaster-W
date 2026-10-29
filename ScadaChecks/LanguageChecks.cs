using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using VisionMaster.Scada;
using VisionMaster.Services;
using VisionMaster.ViewModels;

namespace ScadaChecks
{
    /// <summary>
    /// [LG] 多语言：<c>@键</c> 约定、语言表模型、解析三级回落、管理器原地写回、落盘。
    ///
    /// 与 [BL]（文本列表）同一纪律：整段另起 STA 线程跑（后续若加控件级断言，WPF 控件要求 STA），
    /// 只读数据侧与视图模型，不建窗口、不建 Application。
    ///
    /// 为什么要单独立一段而不是并进 [BL]：语言表与文本列表是<b>两条独立的共享词表</b>
    /// （值 → 文字 vs 键 → 文字），混在一段里以后排查"哪条红了"要多读一遍上下文。
    /// </summary>
    internal partial class Program
    {
        private static class LanguageChecks
        {
            public static void Run()
            {
                Section("[LG] 多语言：@键 约定、语言表、解析回落、管理器、落盘");

                Exception? failure = null;

                var thread = new Thread(() =>
                {
                    try { RunLanguageChecksCore(); }
                    catch (Exception ex) { failure = ex; }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                thread.Join();

                if (failure != null)
                    Check("多语言断言全程未抛异常", false, failure.ToString());
            }
        }

        private static void RunLanguageChecksCore()
        {
            // ---------------- @键 约定 ----------------
            var isKeyAtA = ScadaTextCatalog.IsKey("@a", out var keyA);
            var isKeyLoneAt = ScadaTextCatalog.IsKey("@", out _);
            var isKeyEscaped = ScadaTextCatalog.IsKey("@@a", out _);
            var isKeyPlain = ScadaTextCatalog.IsKey("abc", out _);

            Check("[LG1] IsKey：@a 是键、@ 不是、@@a 不是、abc 不是",
                isKeyAtA && keyA == "a" && !isKeyLoneAt && !isKeyEscaped && !isKeyPlain,
                $"@a={isKeyAtA}({keyA}) / @={isKeyLoneAt} / @@a={isKeyEscaped} / abc={isKeyPlain}");

            // ---------------- 解析：当前语言 → 任一语言 → 原文 ----------------
            var chinese = new ScadaLanguage { Name = "简体中文" };
            chinese.Entries.Add(new ScadaLanguageEntry { Key = "btn.start", Text = "启动" });
            chinese.Entries.Add(new ScadaLanguageEntry { Key = "btn.stop", Text = "停止" });

            var english = new ScadaLanguage { Name = "English" };
            english.Entries.Add(new ScadaLanguageEntry { Key = "btn.start", Text = "Start" });
            english.Entries.Add(new ScadaLanguageEntry { Key = "only.en", Text = "Only English" });

            var languages = new List<ScadaLanguage> { chinese, english };

            var currentHit = ScadaTextCatalog.Resolve("@btn.start", languages, chinese.LanguageId);
            Check("[LG2] 当前语言有该键 → 返回该语言的译文",
                currentHit == "启动",
                $"@btn.start → {currentHit}");

            var fallbackHit = ScadaTextCatalog.Resolve("@only.en", languages, chinese.LanguageId);
            Check("[LG3] 当前语言没有、另一语言有 → 回落另一语言",
                fallbackHit == "Only English",
                $"@only.en → {fallbackHit}");

            var missHit = ScadaTextCatalog.Resolve("@nobody.knows", languages, chinese.LanguageId);
            Check("[LG4] 都没有 → 原样返回 @键（看得见哪个键没配，不是空白）",
                missHit == "@nobody.knows",
                $"@nobody.knows → {missHit}");

            // ---------------- @@ 转义 ----------------
            var unescaped = ScadaTextCatalog.Resolve("@@btn.start", languages, chinese.LanguageId);
            var unescapedWithoutTable = ScadaTextCatalog.Resolve("@@plain", null, Guid.Empty);
            Check("[LG5] @@ 转义 → 解析成单个 @（有表/无表都一样）",
                unescaped == "@btn.start" && unescapedWithoutTable == "@plain",
                $"@@btn.start → {unescaped} / @@plain → {unescapedWithoutTable}");

            // ---------------- 语言表管理：首张自动当前、删当前回落 ----------------
            var document = new ScadaDocument();
            var first = document.AddLanguage("简体中文");
            var firstBecameCurrent = document.CurrentLanguageId == first.LanguageId;

            var second = document.AddLanguage("English");
            var secondDidNotSteal = document.CurrentLanguageId == first.LanguageId;

            var removed = document.TryRemoveLanguage(first, out _);
            var fellBackToFirst = document.CurrentLanguageId == second.LanguageId;

            Check("[LG6] AddLanguage 首张自动设为当前语言；删当前语言后当前语言回落到第一张",
                firstBecameCurrent && secondDidNotSteal && removed && fellBackToFirst,
                $"首张当前={firstBecameCurrent} / 第二张没抢={secondDidNotSteal} / 删后回落={fellBackToFirst}");

            // ---------------- EnsureIdentity：补 Id + 收回悬空当前语言 ----------------
            var identityDocument = new ScadaDocument();
            var legacyLanguage = new ScadaLanguage { LanguageId = Guid.Empty, Name = "旧数据语言" };
            identityDocument.Languages.Add(legacyLanguage);
            identityDocument.CurrentLanguageId = Guid.NewGuid();   // 指向一张已经不存在的表

            var repaired = identityDocument.EnsureIdentity();

            Check("[LG7] EnsureIdentity 补缺失 LanguageId，并把指向已删语言的 CurrentLanguageId 收回第一张",
                legacyLanguage.LanguageId != Guid.Empty
                && identityDocument.CurrentLanguageId == legacyLanguage.LanguageId
                && repaired >= 2,
                $"id={legacyLanguage.LanguageId} / current={identityDocument.CurrentLanguageId} / repaired={repaired}");

            // ---------------- 落盘：Languages + CurrentLanguageId 往返 ----------------
            var savedDocument = new ScadaDocument();
            var savedChinese = new ScadaLanguage { Name = "简体中文" };
            savedChinese.Entries.Add(new ScadaLanguageEntry { Key = "btn.start", Text = "启动" });
            savedChinese.Entries.Add(new ScadaLanguageEntry { Key = "btn.stop", Text = "停止" });

            var savedEnglish = new ScadaLanguage { Name = "English" };
            savedEnglish.Entries.Add(new ScadaLanguageEntry { Key = "btn.start", Text = "Start" });

            savedDocument.Languages.Add(savedChinese);
            savedDocument.Languages.Add(savedEnglish);
            savedDocument.CurrentLanguageId = savedEnglish.LanguageId;

            var json = JsonConvert.SerializeObject(savedDocument);
            var roundTrip = JsonConvert.DeserializeObject<ScadaDocument>(json)!;

            Check("[LG8] .vms 往返：Languages（Id/名称/词条）+ CurrentLanguageId 都不丢",
                roundTrip.Languages.Count == 2
                && roundTrip.Languages[0].LanguageId == savedChinese.LanguageId
                && roundTrip.Languages[0].Name == "简体中文"
                && roundTrip.Languages[0].Entries.Count == 2
                && roundTrip.Languages[0].Entries[1].Key == "btn.stop"
                && roundTrip.Languages[0].Entries[1].Text == "停止"
                && roundTrip.Languages[1].LanguageId == savedEnglish.LanguageId
                && roundTrip.CurrentLanguageId == savedEnglish.LanguageId,
                $"languages={roundTrip.Languages.Count} / current={roundTrip.CurrentLanguageId}");

            // ---------------- CollectKeys：从图元属性袋收集 @键 ----------------
            var collectDocument = new ScadaDocument();
            var collectPage = new ScadaPage { Name = "P1" };

            var button = new ScadaElement { TypeKey = "Hmi.Button" };
            button.SetProperty("Text", "@btn.start");
            button.SetProperty("Fill", "#FF3B82F6");   // 普通值，不该被当成键

            var label = new ScadaElement { TypeKey = "Hmi.Label" };
            label.SetProperty("Text", "@title.line1");

            collectPage.Elements.Add(button);
            collectPage.Elements.Add(label);
            collectDocument.Pages.Add(collectPage);

            var collected = ScadaTextCatalog.CollectKeys(collectDocument);

            Check("[LG9] CollectKeys 从图元属性袋收集 @键（普通文本不算、去重排序）",
                collected.Count == 2
                && collected.Contains("btn.start")
                && collected.Contains("title.line1"),
                string.Join(" / ", collected));

            // ---------------- 管理器：副本编辑 + 原地覆盖 + 当前语言写回 ----------------
            var managerDocument = new ScadaDocument();
            var managerChinese = new ScadaLanguage { Name = "简体中文" };
            managerChinese.Entries.Add(new ScadaLanguageEntry { Key = "btn.start", Text = "启动" });
            var managerEnglish = new ScadaLanguage { Name = "English" };
            managerEnglish.Entries.Add(new ScadaLanguageEntry { Key = "btn.start", Text = "Start" });
            managerDocument.Languages.Add(managerChinese);
            managerDocument.Languages.Add(managerEnglish);
            managerDocument.CurrentLanguageId = managerChinese.LanguageId;

            var manager = new LanguageManagerViewModel(managerDocument);
            var managerEditsCopy = !ReferenceEquals(manager.Languages[0].Language, managerChinese)
                                   && manager.Languages.Count == 2
                                   && manager.SelectedIsCurrent;

            // 改副本：改名 + 补一条词条 + 把当前语言换成 English
            manager.SelectedLanguage!.Name = "简体中文（新）";
            manager.AddEntry();
            manager.SetCurrent(manager.Languages[1]);
            manager.Commit();

            Check("[LG10] 管理器：读副本、确认后原地覆盖（对象引用不变）+ 当前语言跟着写回文档",
                managerEditsCopy
                && managerDocument.Languages.Count == 2
                && ReferenceEquals(managerDocument.Languages[0], managerChinese)
                && managerChinese.Name == "简体中文（新）"
                && managerChinese.Entries.Count == 2
                && managerDocument.CurrentLanguageId == managerEnglish.LanguageId,
                $"副本={managerEditsCopy} / name={managerChinese.Name} / entries={managerChinese.Entries.Count} / current={managerDocument.CurrentLanguageId}");

            // ---------------- 运行时服务：切换语言 + 解析跟随 + 失败原因 ----------------
            var serviceDocument = new ScadaDocument();
            var serviceChinese = new ScadaLanguage { Name = "简体中文" };
            serviceChinese.Entries.Add(new ScadaLanguageEntry { Key = "btn.start", Text = "启动" });
            var serviceEnglish = new ScadaLanguage { Name = "English" };
            serviceEnglish.Entries.Add(new ScadaLanguageEntry { Key = "btn.start", Text = "Start" });
            serviceDocument.Languages.Add(serviceChinese);
            serviceDocument.Languages.Add(serviceEnglish);
            serviceDocument.CurrentLanguageId = serviceChinese.LanguageId;

            var service = new ScadaLanguageService(serviceDocument);
            var changedCount = 0;
            service.Changed += () => changedCount++;

            var switchedById = service.TrySetLanguage(serviceEnglish.LanguageId, out var switchError);
            var textAfterSwitch = service.Resolve("@btn.start");
            var switchedByName = service.TrySetLanguage("简体中文", out _);
            var textAfterNameSwitch = service.Resolve("@btn.start");
            var missingLanguage = service.TrySetLanguage(Guid.NewGuid(), out var missingError);
            var unknownName = service.TrySetLanguage("Klingon", out var unknownNameError);

            Check("[LG11] ScadaLanguageService：切换语言写回文档 + Resolve 跟随 + 找不到时给可上屏原因",
                switchedById && switchError == null && textAfterSwitch == "Start"
                && switchedByName && textAfterNameSwitch == "启动"
                && !missingLanguage && !string.IsNullOrEmpty(missingError)
                && !unknownName && !string.IsNullOrEmpty(unknownNameError)
                && changedCount == 2
                && serviceDocument.CurrentLanguageId == serviceChinese.LanguageId,
                $"en={textAfterSwitch} / zh={textAfterNameSwitch} / changed={changedCount} / err={missingError}");

            // ---------------- 空方案：没有文档时一切退化成空表，不抛 ----------------
            var emptyService = new ScadaLanguageService((ScadaDocument?)null);

            // 先调用再断言：写在 && 链里会被短路，emptyError 就成了"未赋值"（编译器直接报 CS0165）
            var emptySwitched = emptyService.TrySetLanguage(Guid.NewGuid(), out var emptyError);

            Check("[LG12] 没有方案：Languages 为空表、CurrentLanguageName 可直接上屏、切换失败给原因",
                emptyService.Languages.Count == 0
                && emptyService.CurrentLanguageId == Guid.Empty
                && emptyService.CurrentLanguageName == "未选择语言"
                && !emptySwitched
                && !string.IsNullOrEmpty(emptyError)
                && emptyService.Resolve("@btn.start") == "@btn.start",
                $"{emptyService.CurrentLanguageName} / {emptyError}");
        }
    }
}
