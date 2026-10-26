﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using Core.Interfaces.Io;
using Core.Interfaces.Result;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 方案服务
    /// 负责方案的创建、保存和加载
    /// </summary>
    public class SolutionService : BindableBase
    {
        private readonly ObservableCollection<SolutionModel> _solutionModels = new();

        /// <summary>
        /// 方案序列化设置：
        /// TypeNameHandling.Auto —— 抽象/接口位置（ConnectionConfigBase.Config 等）自动写入 $type，
        /// 反序列化按 $type 重建具体子类（ModbusTcpConfig/SiemensS7Config...），新增协议零维护；
        /// SerializationBinder 白名单限定可实例化类型，防止恶意 .vms 文件借 $type 执行任意类型构造。
        /// </summary>
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = new SafeSerializationBinder()
        };

        /// <summary>
        /// $type 白名单：只允许本项目的 Models / CommunicationContracts 命名空间下类型被反序列化
        /// </summary>
        private class SafeSerializationBinder : Communications.ConnectionConfigSerializationBinder
        {
        }

        /// <summary>
        /// 方案序列化设置的只读出口：流程剪贴板（FlowClipboard）的复制快照与 .vms 落盘
        /// 共用同一条管线——TypeNameHandling / binder 口径一旦两份漂移，就会出现
        /// "存盘读得回、粘贴不认账"的劈叉。断言工程没有本程序集私有字段可拿，
        /// 只能用同一个 public binder 复刻同款设置（FlowCanvasChecks.Program.RoundTripSettings）。
        /// </summary>
        public static JsonSerializerSettings SerializationSettings => _jsonSettings;

        /// <summary>
        /// 方案模型集合（只读）
        /// </summary>
        public ReadOnlyObservableCollection<SolutionModel> SolutionModels { get; }

        /// <summary>
        /// 初始化方案服务
        /// </summary>
        public SolutionService()
        {
            SolutionModels = new ReadOnlyObservableCollection<SolutionModel>(_solutionModels);
        }

        /// <summary>
        /// 创建新方案
        /// </summary>
        public Result<SolutionModel> Create(SolutionModel newSolution)
        {
            if (newSolution == null)
                return Result<SolutionModel>.NG("解决方案不能为空");

            _solutionModels.Add(newSolution);
            return Result<SolutionModel>.Ok(newSolution);
        }

        /// <summary>
        /// 按落盘口径序列化方案（与 SaveAsync 写出的文本逐字一致）。
        /// 用途：草稿转存需要"内存里的内容"与"磁盘上的内容"做逐字比较，
        /// 只有两边走同一套设置（同一份 _jsonSettings）比较才有意义，故单独暴露出来。
        /// </summary>
        public static string Serialize(SolutionModel solution)
        {
            if (solution == null) throw new ArgumentNullException(nameof(solution));
            return JsonConvert.SerializeObject(solution, _jsonSettings);
        }

        /// <summary>
        /// 保存方案到文件（原子写：先写 .tmp 再替换，写到一半断电也不会把原方案毁掉）
        /// </summary>
        public async Task<Result<bool>> SaveAsync(SolutionModel targetSolution, string filePath)
        {
            try
            {
                // 先序列化再建目录：序列化失败（坏对象/循环引用）不该在磁盘上留下一个空方案文件夹。
                var json = Serialize(targetSolution);

                var directory = Path.GetDirectoryName(filePath);
                var createdDirectory = false;
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    createdDirectory = true;
                }

                // 原子写：临时文件与目标同目录（跨盘 File.Replace 会失败），
                // 先落临时文件再整体替换；目标已存在走 Replace（保留旧文件到备份位可空），
                // 不存在才 Move。失败时临时文件清掉，不给用户留垃圾；
                // 本次刚建出来的目录若已空，一并删掉（保存失败不该留下“看起来像方案”的空壳）。
                var tmp = filePath + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(false));
                    if (File.Exists(filePath)) File.Replace(tmp, filePath, null);
                    else File.Move(tmp, filePath);
                }
                catch
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 清理失败不影响主流程 */ }
                    if (createdDirectory) SolutionPaths.TryDeleteEmptyFolder(directory);
                    throw;
                }

                // .vms 已原子落盘 ⇒ 身份从此来自磁盘。放在 catch 之外的成功路径上，
                // 写失败时不该把「已落盘」这个事实提前记上（否则审计标签会写一个磁盘上没有的 id）。
                targetSolution.IsIdentityPersisted = true;

                return Result<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.NG($"保存方案失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从文件加载方案
        /// </summary>
        public async Task<Result<SolutionModel>> LoadAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return Result<SolutionModel>.NG($"文件不存在: {filePath}");
                }

                var json = await File.ReadAllTextAsync(filePath);

                // 老文件的 SolutionId 是缺失的，但属性初始化器会补一个新 Guid，
                // 反序列化后无法再从对象上区分「磁盘里写了这个 id」与「刚生成的」。
                // 所以判据只能落在原始 JSON 文本上：有非空的 "SolutionId" 键才算落盘过。
                var identityPersisted = HasPersistedSolutionId(json);

                // 反序列化是纯 CPU 活：3542 步 / 3.9 MB 的方案在 UI 线程上要 1~2 秒，打开时就是白卡。
                // 只把**解析**挪到后台线程；后面的迁移钩子仍在原上下文（它们会经 DI 拿插件表，
                // 不保证线程安全）。这里刻意不加 ConfigureAwait(false)——续体必须回 UI 线程。
                var solution = await Task.Run(
                    () => JsonConvert.DeserializeObject<SolutionModel>(json, _jsonSettings));

                if (solution == null)
                {
                    return Result<SolutionModel>.NG("方案文件解析失败");
                }

                // IO/方案身份归一化：老文件没有这些字段时补默认值，避免运行期空引用。
                // 注：IO 接入层已下线（配置折成通讯连接+网络变量，见 IoMigrationHook），
                // 但旧字段仍要读出来——它是折算的输入，也是回退旧版本时的兼容面。
                if (solution.SolutionId == Guid.Empty)
                    solution.SolutionId = Guid.NewGuid();
                solution.IsIdentityPersisted = identityPersisted;
                solution.IoBuses ??= new List<IoBusDescriptor>();
                solution.IoDevices ??= new List<IoDeviceDescriptor>();
                solution.TemplateAssets ??= new List<VisionMaster.Models.Assets.TemplateAsset>();
                solution.ModelAssets ??= new List<VisionMaster.Models.Assets.ModelAsset>();
                solution.ImageAssets ??= new List<VisionMaster.Models.Assets.ImageAsset>();
                solution.AssetVersionLog ??= new List<VisionMaster.Models.Assets.AssetVersionRecord>();
                solution.AssetDefaults ??= new List<VisionMaster.Models.Assets.AssetDefaultPointer>();
                foreach (var device in solution.IoDevices)
                {
                    if (device == null) continue;
                    device.Points ??= new List<IoPointDescriptor>();
                    device.Params ??= new IoParams();
                }

                // IO 接入层 → 通讯层的一次性折算（幂等：同名连接已存在就跳过该总线）。
                // 合并的理由：IO 层与通讯层各持一条物理链路（各开一条 TCP / 一个 COM），
                // 同一端点配了两边就会互抢端口；合并后链路与轮询调度都只有一套。
                // 旧字段保留不删——回退到旧版本软件仍能读到配置。
                // 注：折算本身分两层（草稿在 Core.Interfaces 算、真实对象在 Communication 造），
                // 因为 Core 引用不到 Communication 的配置/地址类型（依赖方向不允许反过来）。
                IoMigrationHook?.Invoke(solution);

                // P1-⑦：老方案升级——历史版本 While 循环体误用 BranchType.Default 存储，
                // 归一化为专属的 WhileLoop，使"未绑定条件"红灯红字与流程栏样式恢复正确判定
                foreach (var flow in solution.Flows)
                {
                    if (flow == null) continue;

                    NormalizeBranchTypes(flow.Steps);

                    // 调用方式老值迁移：老文件是单选枚举（1=定时/2=变量/3=子程序），
                    // 新模型是位集（见 FlowInvokeType 注释），在这里读入时一次性换算。
                    // 只改内存、不立刻回写文件——下次保存自然按新格式落盘，不产生"偷偷改盘"
                    flow.InvokeType = FlowInvokeTypeExtensions.MigrateLegacy((int)flow.InvokeType);
                }

                // 强制骨架流程（Home / Main / End）补齐：新建方案已自带（见 SolutionModel.Flows），
                // 老方案在这里补——认领老默认名 GoHome/MainTask，缺哪个角色建哪条（幂等）
                MandatoryFlows.Ensure(solution);

                // 步骤图标补全：Icon 是"建步骤时"从工具箱快照进 .vms 的，老方案或插件补
                // ShortName 之前建的步骤会是空串。这里按插件表补一次（只补空值、只改展示字段）。
                // 补全失败只记调试痕迹，绝不让一个图标问题把方案加载弄失败。
                try
                {
                    StepIconBackfillHook?.Invoke(solution);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[方案加载] 步骤图标补全失败（已忽略）：{ex.Message}");
                }

                // 画面/图元补发稳定身份：SCADA 领域模型引入 Id 之前写出的方案文件没有这些字段，
                // 读入后补一次（幂等），使按 Id 寻址的绑定、画面跳转从第一次打开起就是稳定的
                solution.Scada.EnsureIdentity();

                // 旧轴控件迁移：轴面板与旧点位表合并（只处理"整页只有这两个图元"的安全情形，
                // 其余页面只产出提示、不自动改版面）。幂等：合并后旧 TypeKey 不再存在。
                solution.Scada.MigrateLegacyAxisElements(out var axisMigrationMessages);
                foreach (var message in axisMigrationMessages)
                    System.Diagnostics.Debug.WriteLine($"[SCADA 轴控件迁移] {message}");

                if (!_solutionModels.Contains(solution))
                {
                    _solutionModels.Add(solution);
                }

                return Result<SolutionModel>.Ok(solution);
            }
            catch (Exception ex)
            {
                return Result<SolutionModel>.NG($"加载方案失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 原始 JSON 里是否带非空的 <c>"SolutionId"</c>。不做完整解析：LoadAsync 已经要解析一遍，
        /// 这里只是取一个「字段存在性」的事实，用字符串扫描足够且不会因解析设置不同而变。
        /// 判空 Guid 是为了兼容「显式写了 SolutionId 但值是 00000000-…」的历史文件。
        /// </summary>
        private static bool HasPersistedSolutionId(string json)
        {
            const string key = "\"SolutionId\"";
            var i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return false;

            var colon = json.IndexOf(':', i + key.Length);
            if (colon < 0) return false;

            var start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length) return false;

            // 值可能是 "guid" 或 null
            if (json[start] != '"') return false;
            var end = json.IndexOf('"', start + 1);
            if (end < 0) return false;

            var text = json.Substring(start + 1, end - start - 1);
            return Guid.TryParse(text, out var id) && id != Guid.Empty;
        }

        /// <summary>
        /// IO → 通讯 折算钩子（由宿主在启动时挂接，指向 <c>IoMigrationApplier.Apply</c>）。
        /// <para>为什么用钩子而不是直接调：Core 引用不到 Communication 的配置/地址类型
        /// （依赖方向不允许反过来）。与 <c>WriteScope.AttachLeaseRegistry</c> 同一手法——
        /// 把"需要跨层才能做的那一步"静态注入。</para>
        /// <para>未挂接时跳过折算，行为与改造前一致（旧 IO 字段照常可用）。</para>
        /// </summary>
        public static Action<SolutionModel>? IoMigrationHook { get; set; }

        /// <summary>
        /// 方案加载后的步骤图标补全钩子（宿主启动时挂接，指向 <c>StepIconBackfill.Apply</c>）。
        /// <para>为什么用钩子：Core 看不到引擎扫出来的插件表（<c>IPluginProvider</c> 在容器里），
        /// 而图标来源（DisplayAttribute.ShortName）只有插件表知道。与 IoMigrationHook 同一手法。</para>
        /// <para>未挂接时跳过：老步骤保持原样（空图标），不影响任何流程语义。</para>
        /// </summary>
        public static Action<SolutionModel>? StepIconBackfillHook { get; set; }

        /// <summary>
        /// 递归归一化分支类型（含嵌套在分支内的容器步骤）
        /// </summary>
        private static void NormalizeBranchTypes(IEnumerable<StepModel> steps)
        {
            if (steps == null) return;

            foreach (var step in steps)
            {
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        if (step is WhileStep && branch.BranchType == BranchType.Default)
                            branch.BranchType = BranchType.WhileLoop;

                        NormalizeBranchTypes(branch.Steps);
                    }
                }
            }
        }
    }
}
