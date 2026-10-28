using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Core.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prism.Dialogs;
using VisionMaster.Binding;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.ViewModels.DialogViewModels;

namespace RecipeChecks
{
    internal static class Program
    {
        private static int _pass;
        private static int _fail;

        [STAThread]
        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            RecipeApplyGate.ResetForTests();

            Section("数据模型 / 零回归");
            TestZeroRegression();
            TestTypeKeys();

            Section("捕获");
            var fixture = BuildFixture();
            var capture = fixture.Service.Capture("产品A", "A-001", "首件配方");
            Check("捕获成功", capture.Success, capture.Message);
            Check("捕获条数 = 6", capture.EntryCount == 6, $"实际 {capture.EntryCount}");
            Check("方案格式版本抬到 2.0", fixture.Solution.Version >= 2.0, fixture.Solution.Version.ToString("0.0"));
            Check("结构版本 = 1", fixture.Solution.RecipeSchemaVersion == 1);
            Check("生效指针为空", fixture.Solution.ActiveRecipeId == null);
            Check("捕获跳过链接端口", capture.Recipe!.Entries.All(e => e.Key != "LinkedKey"));
            Check("捕获跳过展示索引", capture.Recipe.Entries.All(e => e.Key != "DisplayViewIndex"));
            Check("捕获跳过路径", capture.Recipe.Entries.All(e => e.Key != "FilePath"));
            Check("枚举存成员名", capture.Recipe.Entries.First(e => e.Key == "Day").Value?.ToString() == "Monday");
            Check("枚举声明类型", capture.Recipe.Entries.First(e => e.Key == "Day").DeclaredType == "System.DayOfWeek");
            Check("复杂对象存 JArray", capture.Recipe.Entries.First(e => e.Key == "Roi").Value is JArray);
            Check("复杂对象声明 JToken", capture.Recipe.Entries.First(e => e.Key == "Roi").DeclaredType == "Newtonsoft.Json.Linq.JToken");

            var recipe = capture.Recipe!;
            var json = SolutionService.Serialize(fixture.Solution);
            Check("序列化含 Recipes", json.Contains("\"Recipes\""));
            Check("序列化 Kind 写成员名", json.Contains("\"Kind\": \"StepInput\""));
            Check("序列化无 JToken $type", !json.Contains("\"$type\": \"Newtonsoft.Json.Linq"));
            Check("序列化无 NaN", !json.Contains("NaN"));

            var reloaded = JsonConvert.DeserializeObject<SolutionModel>(json, SolutionService.SerializationSettings);
            Check("往返配方数 = 1", reloaded?.Recipes?.Count == 1, reloaded?.Recipes?.Count.ToString() ?? "null");
            Check("往返阈值 = 0.75",
                reloaded!.Recipes![0].Entries.First(e => e.Key == "Threshold").Value?.ToObject<double>() == 0.75);

            Section("应用 / 偏离");
            var step = fixture.Step;
            step.SetInputValue("Threshold", 0.9);
            step.SetInputValue("Mode", "Rect");
            var versionBefore = fixture.Flow.Version;
            var apply = fixture.Service.Apply(recipe.RecipeId);
            Check("应用成功", apply.Success, apply.Message);
            Check("应用条数 = 6", apply.AppliedCount == 6, apply.AppliedCount.ToString());
            Check("阈值已写回 0.75", Math.Abs(ToDouble(step.InputValues["Threshold"]) - 0.75) < 1e-9);
            Check("字符串已写回 Circle", ToText(step.InputValues["Mode"]) == "Circle");
            Check("枚举已写回 Monday", ToText(step.InputValues["Day"]) == "Monday");
            Check("复杂对象已写回 JArray", step.InputValues["Roi"] is JArray);
            Check("生效指针 = 配方", fixture.Solution.ActiveRecipeId == recipe.RecipeId);
            Check("最后应用版本 = 内容版本", recipe.LastAppliedContentVersion == recipe.ContentVersion);
            Check("FlowModel.Version 已推进", fixture.Flow.Version > versionBefore,
                $"{versionBefore} -> {fixture.Flow.Version}");

            var dev0 = fixture.Service.ComputeDeviation();
            Check("应用后偏离 = 0", dev0.HasActiveRecipe && dev0.Deviated == 0,
                $"deviated={dev0.Deviated}");
            step.SetInputValue("Threshold", 0.9);
            var dev1 = fixture.Service.ComputeDeviation();
            Check("改一个值后偏离 = 1", dev1.Deviated == 1, $"deviated={dev1.Deviated}");
            step.SetInputValue("Threshold", 0.75);
            Check("改回后偏离 = 0", fixture.Service.ComputeDeviation().Deviated == 0);

            Section("边界：链接 / 孤儿 / 类型收窄");
            step.SetLink("Mode", new LinkReference
            {
                Kind = LinkKind.GlobalVariable,
                DisplayAddress = "SCADA.RecipeName",
            });
            var linkedReport = fixture.Service.Validate(recipe.RecipeId);
            Check("链接端口预检报错", linkedReport.HasErrors && linkedReport.Issues.Any(i => i.Key == "Mode"));
            var linkedApply = fixture.Service.Apply(recipe.RecipeId);
            Check("链接端口应用被拒", !linkedApply.Success);
            Check("链接端口值未变", ToText(step.InputValues["Mode"]) == "Circle");
            step.RemoveLink("Mode");

            fixture.Flow.Steps.Remove(step);
            var orphanReport = fixture.Service.Validate(recipe.RecipeId);
            Check("孤儿条目预检报错", orphanReport.HasErrors);
            fixture.Flow.Steps.Add(step);

            step.SetInputValue("Threshold", 1); // int，捕获时是 double：收窄必须拒绝
            var narrowing = fixture.Service.Validate(recipe.RecipeId);
            Check("double -> int 收窄被拒", narrowing.HasErrors);
            step.SetInputValue("Threshold", 0.75);

            Section("回滚");
            step.SetInputValue("Threshold", 0.1);
            step.SetInputValue("Mode", "BeforeRollback");
            var rollbackService = new RecipeService(
                fixture.Workspace,
                requireAudit: false,
                beforeWrite: (s, key) =>
                {
                    if (key == "Roi") throw new InvalidOperationException("注入失败");
                });
            var rollback = rollbackService.Apply(recipe.RecipeId);
            Check("应用失败并已回滚", !rollback.Success && rollback.RolledBack && !rollback.RollbackFailed,
                rollback.Message);
            Check("回滚后阈值 = 0.1", Math.Abs(ToDouble(step.InputValues["Threshold"]) - 0.1) < 1e-9);
            Check("回滚后字符串 = BeforeRollback", ToText(step.InputValues["Mode"]) == "BeforeRollback");
            Check("回滚后指针保持旧值", fixture.Solution.ActiveRecipeId == recipe.RecipeId);

            Section("门闸");
            RecipeApplyGate.ResetForTests();
            Check("启动登记成功", RecipeApplyGate.TryBeginStart(out var startLease));
            Check("在途启动时应用被封门", !RecipeApplyGate.TryBeginApply(30, out _));
            startLease!.Dispose();
            Check("在途归零后可应用", RecipeApplyGate.TryBeginApply(30, out var applyLease));
            Check("封门标志为真", RecipeApplyGate.IsApplying);
            applyLease!.Dispose();
            Check("释放后封门标志为假", !RecipeApplyGate.IsApplying);

            Section("审计 / 权限");
            var strict = new RecipeService(fixture.Workspace, requireAudit: true, audit: null);
            var auditFail = strict.Apply(recipe.RecipeId);
            Check("审计不可用禁止应用", !auditFail.Success && auditFail.Message.Contains("审计不可用"),
                auditFail.Message);

            var policy = new ScadaAccessPolicy();
            var permissionService = new RecipeService(
                fixture.Workspace, accessPolicy: policy, requireAudit: false);
            var permissionFail = permissionService.Apply(recipe.RecipeId);
            Check("未登录禁止切换", !permissionFail.Success && permissionFail.Message.Contains("登录"),
                permissionFail.Message);

            Section("生命周期");
            var deleteActive = fixture.Service.Delete(recipe.RecipeId, out var deleteError);
            Check("生效配方禁止删除", !deleteActive, deleteError);
            var contentVersion = recipe.ContentVersion;
            var renamed = fixture.Service.Rename(recipe.RecipeId, "产品A-改名", out var renameError);
            Check("重命名成功", renamed, renameError);
            Check("重命名不改内容版本", recipe.ContentVersion == contentVersion,
                $"{contentVersion} -> {recipe.ContentVersion}");

            Section("V2 变量 / 导入导出");
            var local = fixture.Workspace.GlobalVariables
                .OfType<LocalVariableModel>()
                .First(v => v.Name == "Setpoint");
            Check("变量已捕获", recipe.Entries.Any(e =>
                e.Kind == RecipeTargetKind.LocalVariable && e.TargetName == "Setpoint"));
            Check("触发源变量未捕获", recipe.Entries.All(e => e.TargetName != "TriggerVar"));
            Check("变量应用后 = 10.5", Math.Abs(Convert.ToDouble(local.Value) - 10.5) < 1e-9,
                Convert.ToString(local.Value));

            local.Value = 99.0;
            var prepared = fixture.Service.PrepareVariablesForSave(out var prepareError);
            Check("保存前变量回填", prepared && Math.Abs(Convert.ToDouble(local.Value) - 10.5) < 1e-9,
                prepareError);

            fixture.Flow.TriggerVariable = "Setpoint";
            var triggerReport = fixture.Service.Validate(recipe.RecipeId);
            Check("触发源变量预检报错",
                triggerReport.HasErrors && triggerReport.Issues.Any(i => i.Target == "Setpoint"));
            fixture.Flow.TriggerVariable = "TriggerVar";

            var fileService = new RecipeFileService(fixture.Service);
            var path = Path.Combine(AppContext.BaseDirectory,
                "recipe-checks-" + Guid.NewGuid().ToString("N") + ".vmrecipe");
            var exported = fileService.Export(recipe.RecipeId, path, out var exportError);
            Check("导出成功", exported, exportError);
            if (exported)
            {
                var exportedJson = File.ReadAllText(path);
                Check("导出文件无 $type", !exportedJson.Contains("\"$type\"", StringComparison.Ordinal));
            }

            var originalName = recipe.Name;
            Check("原配方改名腾出名称",
                fixture.Service.Rename(recipe.RecipeId, originalName + "-源", out var importRenameError),
                importRenameError);
            Check("导入成功", fileService.Import(path, out var imported, out var importError), importError);
            Check("导入名称 = 原名称", imported?.Name == originalName, imported?.Name ?? "null");
            Check("导入内容版本 = 1", imported?.ContentVersion == 1);
            Check("导入不应用", fixture.Solution.ActiveRecipeId == recipe.RecipeId);
            Check("导入条目数 = 6", imported?.Entries.Count == 6,
                imported?.Entries.Count.ToString() ?? "null");
            File.Delete(path);

            var badPath = Path.Combine(AppContext.BaseDirectory,
                "recipe-checks-bad-" + Guid.NewGuid().ToString("N") + ".vmrecipe");
            File.WriteAllText(badPath, "{\"$type\":\"X\",\"Recipe\":{\"Name\":\"X\"}}");
            Check("含 $type 拒绝", !fileService.Import(badPath, out _, out var typeError), typeError);
            File.WriteAllText(badPath,
                "{\"FormatVersion\":1,\"Recipe\":{\"Name\":\"X\"},\"Address\":\"1\"}");
            Check("含设备字段拒绝", !fileService.Import(badPath, out _, out var deviceError), deviceError);
            File.Delete(badPath);

            Section("V3 参数裁剪 / 应用预览");
            var candidates = fixture.Service.GetCandidates(recipe.RecipeId);
            Check("候选不包含已加入的 Mode",
                candidates.All(c => !(c.Kind == RecipeTargetKind.StepInput && c.Key == "Mode")));
            Check("候选不包含触发源变量",
                candidates.All(c => c.TargetName != "TriggerVar"));

            var cvBefore = recipe.ContentVersion;
            Check("删除参数 Mode",
                fixture.Service.RemoveEntry(recipe.RecipeId, RecipeTargetKind.StepInput,
                    fixture.Step.StepID, "Mode", out var removeError), removeError);
            Check("删除后内容版本递增", recipe.ContentVersion > cvBefore,
                $"{cvBefore} -> {recipe.ContentVersion}");
            Check("删除后 Mode 出现在候选",
                fixture.Service.GetCandidates(recipe.RecipeId).Any(c =>
                    c.Kind == RecipeTargetKind.StepInput && c.Key == "Mode"));
            Check("重新添加 Mode",
                fixture.Service.AddEntry(recipe.RecipeId, RecipeTargetKind.StepInput,
                    fixture.Step.StepID, "Mode", out var addError), addError);
            Check("重复添加被拒",
                !fixture.Service.AddEntry(recipe.RecipeId, RecipeTargetKind.StepInput,
                    fixture.Step.StepID, "Mode", out _));

            var preview = fixture.Service.Preview(recipe.RecipeId);
            Check("预览可应用", preview.CanApply, preview.Report?.Summary ?? "");
            Check("预览总条数 = 6", preview.Total == 6, preview.Total.ToString());
            Check("预览无错误", preview.Errors == 0, preview.Errors.ToString());

            fixture.Step.SetInputValue("Threshold", 0.9);
            var changedPreview = fixture.Service.Preview(recipe.RecipeId);
            Check("预览修改数 = 1", changedPreview.Modified == 1, changedPreview.Modified.ToString());
            fixture.Step.SetInputValue("Threshold", 0.75);

            fixture.Step.SetLink("Mode", new LinkReference
            {
                Kind = LinkKind.GlobalVariable,
                DisplayAddress = "SCADA.RecipeName",
            });
            var blockedPreview = fixture.Service.Preview(recipe.RecipeId);
            Check("预检错误时禁止应用", !blockedPreview.CanApply && blockedPreview.Errors > 0,
                blockedPreview.Report?.Summary ?? "");
            fixture.Step.RemoveLink("Mode");

            Section("V4 SCADA 应用配方动作");
            Check("ApplyRecipe 枚举值 = 23", (int)ScadaActionType.ApplyRecipe == 23);
            Check("ApplyRecipe 显示名", ScadaActionType.ApplyRecipe.DisplayName() == "应用配方");
            Check("ApplyRecipe 属危险动作", ScadaActionType.ApplyRecipe.IsDangerous());
            Check("ApplyRecipe 已接通", ScadaActionType.ApplyRecipe.PendingReason() == null);

            var scadaAction = new ScadaAction { Type = ScadaActionType.ApplyRecipe };
            Check("未选配方描述", scadaAction.Describe() == "应用配方（未选配方）",
                scadaAction.Describe());
            scadaAction.BindRecipe(recipe.RecipeId, recipe.Name);
            Check("已选配方", scadaAction.HasRecipe);
            Check("描述含配方名", scadaAction.Describe().Contains(recipe.Name), scadaAction.Describe());

            Check("SCADA 按 Id 应用成功",
                fixture.Service.TryApply(recipe.RecipeId, null, out var byIdError), byIdError ?? "");
            Check("SCADA 按名应用成功",
                fixture.Service.TryApply(Guid.Empty, recipe.Name, out var byNameError), byNameError ?? "");
            Check("SCADA 未知配方被拒",
                !fixture.Service.TryApply(Guid.Empty, "不存在的配方", out var missingError),
                missingError ?? "");

            Section("V5 SCADA 配方选择器 / 动作行接线");
            var pickerVm = new ScadaRecipePickerViewModel(fixture.Service);
            pickerVm.OnDialogOpened(new DialogParameters
            {
                { ScadaRecipePicker.CurrentIdKey, recipe.RecipeId },
                { ScadaRecipePicker.CurrentNameKey, recipe.Name },
            });

            Check("选择器列出方案内全部配方",
                pickerVm.Recipes.Count == fixture.Service.Recipes.Count && pickerVm.Recipes.Count > 0,
                $"{pickerVm.Recipes.Count} / {fixture.Service.Recipes.Count}");
            Check("按 Id 预选当前配方",
                pickerVm.SelectedRecipe?.Id == recipe.RecipeId,
                pickerVm.SelectedRecipe?.Name ?? "null");
            Check("生效中徽标落在 ActiveRecipeId 那条",
                pickerVm.Recipes.Count(r => r.IsActive) == 1
                && pickerVm.Recipes.Single(r => r.IsActive).Id == fixture.Solution.ActiveRecipeId);

            var byNameVm = new ScadaRecipePickerViewModel(fixture.Service);
            byNameVm.OnDialogOpened(new DialogParameters
            {
                { ScadaRecipePicker.CurrentIdKey, Guid.Empty },
                { ScadaRecipePicker.CurrentNameKey, imported!.Name },
            });
            Check("Id 为空时按名字兜底预选",
                byNameVm.SelectedRecipe?.Id == imported.RecipeId,
                byNameVm.SelectedRecipe?.Name ?? "null");

            var unknownVm = new ScadaRecipePickerViewModel(fixture.Service);
            unknownVm.OnDialogOpened(new DialogParameters
            {
                { ScadaRecipePicker.CurrentIdKey, Guid.NewGuid() },
                { ScadaRecipePicker.CurrentNameKey, "不存在的配方" },
            });
            Check("旧数据指向已删配方时落回第一条（不是空选）",
                unknownVm.SelectedRecipe != null
                && unknownVm.SelectedRecipe.Id == fixture.Service.Recipes[0].RecipeId,
                unknownVm.SelectedRecipe?.Name ?? "null");

            IDialogResult? pickerClosed = null;
            pickerVm.RequestClose = MakeDialogCloseListener(r => pickerClosed = r);
            pickerVm.PickCommand.Execute();
            var pickedId = pickerClosed?.Parameters.GetValue<Guid>(ScadaRecipePicker.PickedIdKey);
            var pickedName = pickerClosed?.Parameters.GetValue<string>(ScadaRecipePicker.PickedNameKey);
            Check("确认回传 (配方 Id, 配方名)",
                pickerClosed?.Result == ButtonResult.OK
                && pickedId == recipe.RecipeId
                && pickedName == recipe.Name,
                $"{pickerClosed?.Result} / {pickedId} / {pickedName}");

            IDialogResult? cancelClosed = null;
            pickerVm.RequestClose = MakeDialogCloseListener(r => cancelClosed = r);
            pickerVm.CancelCommand.Execute();
            Check("取消只关窗、不回传目标", cancelClosed?.Result == ButtonResult.Cancel,
                cancelClosed?.Result.ToString() ?? "null");

            var emptyWorkspace = new WorkspaceContext();
            emptyWorkspace.SwitchSolution(new SolutionModel { SolutionName = "EmptyRecipes" });
            var emptyVm = new ScadaRecipePickerViewModel(
                new RecipeService(emptyWorkspace, requireAudit: false));
            emptyVm.OnDialogOpened(new DialogParameters());
            Check("方案内无配方时不可确认",
                emptyVm.Recipes.Count == 0
                && emptyVm.SelectedRecipe == null
                && !emptyVm.PickCommand.CanExecute());

            // 动作行：没接选择器必须"灰掉"，接了必须能回填——两条都是静默失败高发点。
            var actionHost = new ScadaElement { TypeKey = "Hmi.Button" };
            var noPickerEditor = new ScadaEventEditorViewModel(
                actionHost, ScadaEventType.Pressed, "点击", null,
                new FakeVariablePicker(), new FakePagePicker(), new FakeFlowPicker());
            var recipeAction = new ScadaAction { Type = ScadaActionType.ApplyRecipe };
            Check("没接配方选择器时「选择配方」不可用",
                !noPickerEditor.PickRecipeCommand.CanExecute(recipeAction));

            var fakeRecipePicker = new FakeRecipePicker();
            var wiredEditor = new ScadaEventEditorViewModel(
                actionHost, ScadaEventType.Pressed, "点击", null,
                new FakeVariablePicker(), new FakePagePicker(), new FakeFlowPicker(),
                recipePicker: fakeRecipePicker);
            Check("接了配方选择器时「选择配方」可用",
                wiredEditor.PickRecipeCommand.CanExecute(recipeAction));
            wiredEditor.PickRecipeCommand.Execute(recipeAction);
            Check("选择结果回填到动作（Id + 名字）",
                fakeRecipePicker.Invoked
                && recipeAction.TargetRecipeId == fakeRecipePicker.PickedId
                && recipeAction.TargetRecipeName == fakeRecipePicker.PickedName
                && recipeAction.HasRecipe);

            var choiceTypes = wiredEditor.ActionTypeChoices.Select(c => c.Type).ToList();
            Check("「应用配方」排在运动命令之后（不混进设备动作段）",
                choiceTypes.IndexOf(ScadaActionType.ApplyRecipe)
                > choiceTypes.IndexOf(ScadaActionType.MotionJogStop),
                string.Join(",", choiceTypes));

            Section("V6 配方只读系统变量 / 按钮限制");
            var v6 = BuildFixture();
            var v6Source = new RecipeVariableSource(v6.Service, v6.Workspace);
            var v6Catalog = new RecipeVariableCatalog(v6Source);
            var v6Vars = v6Catalog.Snapshot();
            var v6ByName = v6Vars.ToDictionary(v => v.Name, v => v);

            Check("配方系统变量 8 条", v6Vars.Count == 8, v6Vars.Count.ToString());
            Check("变量名都在 System.Recipe 下",
                v6Vars.All(v => v.Name.StartsWith(RecipeVariableCatalog.VariableNamePrefix, StringComparison.Ordinal)),
                string.Join("、", v6Vars.Select(v => v.Name)));
            Check("变量全部只读（不实现 IWritableVariable）",
                v6Vars.All(v => v is not IWritableVariable));

            string VarName(string field) => RecipeVariableCatalog.VariableNamePrefix + field;

            Check("无配方时 Status=未启用",
                (bool)v6ByName[VarName("Active")].Value! == false
                && (string)v6ByName[VarName("Status")].Value! == "未启用",
                (string?)v6ByName[VarName("Status")].Value);

            var v6Capture = v6.Service.Capture("产品B", "B-001", "V6");
            Check("V6 捕获成功", v6Capture.Success, v6Capture.Message);
            Check("捕获后 Status=未应用",
                (string)v6ByName[VarName("Status")].Value! == "未应用",
                (string?)v6ByName[VarName("Status")].Value);

            var v6Apply = v6.Service.Apply(v6Capture.Recipe!.RecipeId);
            Check("V6 应用成功", v6Apply.Success, v6Apply.Message);
            Check("Active=true", (bool)v6ByName[VarName("Active")].Value!);
            Check("Name=产品B", (string)v6ByName[VarName("Name")].Value! == "产品B",
                (string?)v6ByName[VarName("Name")].Value);
            Check("Code=B-001", (string)v6ByName[VarName("Code")].Value! == "B-001");
            Check("Version=1", (int)v6ByName[VarName("Version")].Value! == 1);
            Check("EntryCount=6", (int)v6ByName[VarName("EntryCount")].Value! == 6,
                v6ByName[VarName("EntryCount")].Value?.ToString());
            Check("Status=已生效",
                (string)v6ByName[VarName("Status")].Value! == "已生效",
                (string?)v6ByName[VarName("Status")].Value);

            var v6Summary = (string)v6ByName[VarName("Summary")].Value!;
            Check("摘要含 Threshold = 0.75", v6Summary.Contains("Threshold = 0.75"), v6Summary);
            Check("摘要含 Mode = Circle", v6Summary.Contains("Mode = Circle"), v6Summary);
            Check("摘要含变量 Setpoint", v6Summary.Contains("Setpoint = 10.5"), v6Summary);

            v6.Step.SetInputValue("Threshold", 0.9);
            v6.Service.NotifyStateChanged();
            Check("参数被改后 Deviation=1",
                (int)v6ByName[VarName("Deviation")].Value! == 1,
                v6ByName[VarName("Deviation")].Value?.ToString());
            Check("偏离时 Status=偏离 1",
                (string)v6ByName[VarName("Status")].Value! == "偏离 1",
                (string?)v6ByName[VarName("Status")].Value);

            Check("清除生效", v6.Service.ClearActive(out var v6ClearError), v6ClearError);
            Check("清除后 Active=false", !(bool)v6ByName[VarName("Active")].Value!);
            Check("清除后 Name 为空", (string)v6ByName[VarName("Name")].Value! == string.Empty);
            Check("清除后 Status=未应用",
                (string)v6ByName[VarName("Status")].Value! == "未应用");

            var v6NameVar = v6ByName[VarName("Name")];
            var v6NameBefore = (string)v6NameVar.Value!;
            ((IPort)v6NameVar).Value = "hacked";
            Check("系统变量写入被忽略（仍只读）",
                (string)v6NameVar.Value! == v6NameBefore,
                (string?)v6NameVar.Value);

            var v6Catalog2 = new RecipeVariableCatalog(
                new RecipeVariableSource(v6.Service, v6.Workspace));
            Check("两次构造的变量 Id 相同（确定性身份）",
                v6Catalog2.Snapshot().Select(v => v.VariableId)
                    .SequenceEqual(v6Vars.Select(v => v.VariableId)));

            var buttonHost = new ScadaElement { TypeKey = "Hmi.Button" };
            var pageHost = new ScadaPage();
            var buttonEditor = new ScadaEventEditorViewModel(
                buttonHost, ScadaEventType.Pressed, "按下", null,
                new FakeVariablePicker(), new FakePagePicker(), new FakeFlowPicker());
            var pageEditor = new ScadaEventEditorViewModel(
                pageHost, ScadaEventType.Loaded, "加载", null,
                new FakeVariablePicker(), new FakePagePicker(), new FakeFlowPicker());
            var variableEditor = new ScadaEventEditorViewModel(
                pageHost, ScadaEventType.ValueChanged, "值变化", null,
                new FakeVariablePicker(), new FakePagePicker(), new FakeFlowPicker());

            Check("按钮按下可配「应用配方」",
                buttonEditor.ActionTypeChoices.Any(t => t.Type == ScadaActionType.ApplyRecipe));
            Check("页面加载不可配「应用配方」",
                !pageEditor.ActionTypeChoices.Any(t => t.Type == ScadaActionType.ApplyRecipe));
            Check("变量事件不可配「应用配方」",
                !variableEditor.ActionTypeChoices.Any(t => t.Type == ScadaActionType.ApplyRecipe));

            Console.WriteLine();
            Console.WriteLine($"通过: {_pass}  失败: {_fail}");
            return _fail == 0 ? 0 : 1;
        }

        private sealed class Fixture
        {
            public WorkspaceContext Workspace { get; init; } = null!;
            public SolutionModel Solution { get; init; } = null!;
            public FlowModel Flow { get; init; } = null!;
            public ActionStep Step { get; init; } = null!;
            public RecipeService Service { get; init; } = null!;
        }

        private static Fixture BuildFixture()
        {
            var workspace = new WorkspaceContext();
            var solution = new SolutionModel { SolutionName = "RecipeChecks" };
            solution.Flows.Clear();

            var flow = new FlowModel { FlowName = "Main", InvokeType = FlowInvokeType.LineStart };
            var step = new ActionStep("?", "测试步骤", "RecipeChecks.FakePlugin", "检测");
            step.SetInputValue("Threshold", 0.75);
            step.SetInputValue("Mode", "Circle");
            step.SetInputValue("Day", DayOfWeek.Monday);
            step.SetInputValue("Count", 5);
            step.SetInputValue("Roi", JArray.Parse("[{\"Name\":\"ROI\",\"ShapeType\":\"Rectangle\",\"Params\":[1.0,2.0]}]"));
            step.SetInputValue("LinkedKey", "upstream");
            step.SetLink("LinkedKey", new LinkReference
            {
                Kind = LinkKind.GlobalVariable,
                DisplayAddress = "SCADA.RecipeName",
            });
            step.SetInputValue("DisplayViewIndex", 1);
            step.SetInputValue("FilePath", "C:\\x.png");

            flow.Steps.Add(step);
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            workspace.GlobalVariables.Clear();

            workspace.GlobalVariables.Add(new LocalVariableModel
            {
                Name = "Setpoint",
                DataType = typeof(double),
                Value = 10.5,
            });
            workspace.GlobalVariables.Add(new LocalVariableModel
            {
                Name = "TriggerVar",
                DataType = typeof(bool),
                Value = false,
            });
            flow.TriggerVariable = "TriggerVar";

            return new Fixture
            {
                Workspace = workspace,
                Solution = solution,
                Flow = flow,
                Step = step,
                Service = new RecipeService(workspace, requireAudit: false),
            };
        }

        private static void TestZeroRegression()
        {
            var oldFile = new SolutionModel();
            var oldJson = SolutionService.Serialize(oldFile);
            Check("老文件不写 Recipes", !oldJson.Contains("\"Recipes\""));
            Check("老文件不写 ActiveRecipeId", !oldJson.Contains("\"ActiveRecipeId\""));
            Check("老文件不写 RecipeSchemaVersion", !oldJson.Contains("\"RecipeSchemaVersion\""));
            Check("老文件版本仍为 1.0", oldFile.Version == 1.0);

            oldFile.Recipes = new List<RecipeDefinition>();
            var emptyJson = SolutionService.Serialize(oldFile);
            Check("空集合写 Recipes: []", Regex.IsMatch(emptyJson, "\"Recipes\"\\s*:\\s*\\[\\s*\\]"));

            oldFile.Recipes = null;
            var nullJson = SolutionService.Serialize(oldFile);
            Check("null 不写 Recipes 键", !nullJson.Contains("\"Recipes\""));
        }

        private static void TestTypeKeys()
        {
            Check("double 类型键", RecipeTypeKey.Normalize(typeof(double)) == "System.Double");
            Check("double[] 类型键", RecipeTypeKey.Normalize(typeof(double[])) == "System.Double[]");
            Check("List<T> 与 ObservableCollection<T> 同形状",
                RecipeTypeKey.IsDeclaredTypeCompatible(
                    "System.Collections.Generic.List`1[System.Double]",
                    "System.Collections.ObjectModel.ObservableCollection`1[System.Double]"));
            Check("int -> double 放宽",
                RecipeTypeKey.IsDeclaredTypeCompatible("System.Int32", "System.Double"));
            Check("double -> int 拒绝",
                !RecipeTypeKey.IsDeclaredTypeCompatible("System.Double", "System.Int32"));
            Check("枚举成员名校验", RecipeTypeKey.TryConvertToDeclared(
                new JValue("Monday"), typeof(DayOfWeek), out var day, out _) && (DayOfWeek)day! == DayOfWeek.Monday);
            Check("未定义枚举拒绝", !RecipeTypeKey.TryConvertToDeclared(
                new JValue("Funday"), typeof(DayOfWeek), out _, out _));
        }

        private static double ToDouble(object? value)
            => value is JToken token ? token.ToObject<double>() : Convert.ToDouble(value);

        private static string? ToText(object? value)
            => value is JToken token ? token.ToString() : Convert.ToString(value);

        /// <summary>
        /// 与 ScadaChecks 同款：把 <see cref="DialogCloseListener"/> 的私有构造打开，
        /// 好断言"关窗时回传了什么"。Prism 没给公开构造，这是唯一不改产品的办法。
        /// </summary>
        private static DialogCloseListener MakeDialogCloseListener(Action<IDialogResult> onClosed)
        {
            var ctor = typeof(DialogCloseListener).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Action<IDialogResult>) },
                modifiers: null);

            if (ctor == null)
                throw new MissingMethodException(
                    "Prism.Dialogs.DialogCloseListener", ".ctor(Action<IDialogResult>)");

            return (DialogCloseListener)ctor.Invoke(new object[] { onClosed });
        }

        /// <summary>假选择器：确认即回填固定目标，用来钉"按钮可用 + 回填走 BindRecipe"。</summary>
        private sealed class FakeRecipePicker : IScadaRecipePicker
        {
            public Guid PickedId { get; } = Guid.NewGuid();
            public string? PickedName { get; } = "测试配方";
            public bool Invoked { get; private set; }

            public void Pick(Guid currentRecipeId, string? currentName, Action<Guid, string?> onPicked)
            {
                Invoked = true;
                onPicked(PickedId, PickedName);
            }
        }

        /// <summary>事件编辑器只要求"有入口"，这三个假实现不参与本组断言。</summary>
        private sealed class FakeVariablePicker : IScadaVariablePicker
        {
            public void Pick(Guid currentId, string? currentName, Action<Guid, string?> onPicked) { }
        }

        private sealed class FakePagePicker : IScadaPagePicker
        {
            public void Pick(Guid currentId, string? currentName, Action<Guid, string?> onPicked) { }
        }

        private sealed class FakeFlowPicker : IScadaFlowPicker
        {
            public void Pick(string? currentFlowId, string? currentName, Action<string, string?> onPicked) { }
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("== " + name + " ==");
        }

        private static void Check(string name, bool ok, string detail = "")
        {
            if (ok)
            {
                _pass++;
                Console.WriteLine("[PASS] " + name);
            }
            else
            {
                _fail++;
                Console.WriteLine("[FAIL] " + name + (string.IsNullOrWhiteSpace(detail) ? string.Empty : " | " + detail));
            }
        }
    }
}
