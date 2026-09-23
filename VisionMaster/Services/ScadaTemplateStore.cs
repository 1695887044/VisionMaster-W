using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 模板清单里的一行（给界面看的视图）：名字、几个图元、什么时候存的。
    /// <b>不含负载本体</b>——工具箱列表只负责显示，真要用模板时再按
    /// <see cref="ScadaTemplateStore.GetPayload"/> 取一次，避免把一堆图元快照
    /// 绑进列表项里、让每次刷新列表都拷一遍全部模板内容。
    /// </summary>
    /// <param name="TemplateId">模板身份（改名 / 删除 / 插入都按它寻址，不按名字）</param>
    /// <param name="Name">显示名</param>
    /// <param name="ItemCount">模板里有几个图元</param>
    /// <param name="CreatedUtc">保存时刻（UTC）</param>
    public sealed record ScadaTemplateInfo(Guid TemplateId, string Name, int ItemCount, DateTime CreatedUtc);

    /// <summary>
    /// "我的模板"存储：<b>本机模板库的唯一一份数据</b>。
    ///
    /// 它在整个复制粘贴体系里的位置
    /// ---------
    /// <see cref="ScadaClipboard"/> 管"内存里那一份、活不过一次退出"，
    /// 本类管"落盘那一份、下次开软件还在"。两者共用同一种载体
    /// （<see cref="ScadaClipboardPayload"/>）与同一个物化实现
    /// （<see cref="ScadaClipboard.Materialize"/>）——所以"粘贴修好了、插模板还漏着"
    /// 这种劈叉在结构上不可能发生。
    ///
    /// 文件长什么样
    /// ---------
    /// <c>ScadaTemplates.json</c>，与 <c>AppConfig.json</c> / <c>ScadaUsers.json</c> 同级
    /// （程序目录），结构为 <c>{ Version, Templates: [{ TemplateId, Name, CreatedUtc, Payload }] }</c>。
    /// 写盘走<b>原子写</b>（临时文件 → 替换），断电只会丢这一次改动，不会留下半个文件。
    ///
    /// 为什么<b>不</b>跟着方案走（不塞进 <c>SolutionModel</c>）
    /// ---------
    /// 模板是"这台机器上的图库"，不是"这个方案的画面"。跟着方案走的话，
    /// 换个方案就换一批模板——而"把 A 项目做好的阀门样式用到 B 项目"恰恰是模板最常见的用法。
    /// 同理它也不跟着用户账号走：账号文件里放图元快照，等于让权限体系去管图形数据。
    ///
    /// 版本号怎么用
    /// ---------
    /// 文件头有 <see cref="CurrentVersion"/>，每条负载里还有自己的
    /// <see cref="ScadaClipboardPayload.Version"/>。读到<b>更新版本</b>写的文件时
    /// 尽力读（认得的字段照用）、但把 <see cref="IsReadOnly"/> 立起来拒绝一切写入——
    /// 不这么做的话，"运维装错版本、老版本打开过一次"就会把新版本存的模板<b>整批抹掉</b>，
    /// 而现场看到的只是"我的模板少了几个"。宁可让老版本明确报"只读"，也不静默丢数据。
    ///
    /// 重名怎么办
    /// ---------
    /// <see cref="TrySave"/> 重名<b>自动避让</b>（阀门 → 阀门_2），与图元落名同一口径：
    /// 用户存模板时心里想的是"再存一个"，静默覆盖掉一个同名模板才是数据丢失。
    /// <see cref="TryRename"/> 重名则<b>明确拒绝</b>：改名是用户指名道姓要一个名字，
    /// 静默给他改成 _2，他只会以为"改名没生效"。
    ///
    /// 线程：与 <see cref="ScadaUserStore"/> 同口径，只在 UI 线程上读写（右键菜单、工具箱面板），
    /// 故不加锁。
    /// </summary>
    public sealed class ScadaTemplateStore
    {
        /// <summary>模板文件名（与 <c>ScadaUsers.json</c> 同级：程序目录）</summary>
        public const string DefaultFileName = "ScadaTemplates.json";

        /// <summary>文件结构版本。改动 <c>ScadaTemplateRecord</c> / 文件头形状时 +1</summary>
        public const int CurrentVersion = 1;

        /// <summary>模板名长度上限（与账号名同口径：够长到能写清用途，短到不会撑破工具箱那一列）</summary>
        public const int MaxNameLength = 32;

        private static readonly JsonSerializerSettings JsonOptions = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,

            // 刻意不设 TypeNameHandling：模板文件里一个 $type 都不该有。
            // 负载是纯数据快照（见 ScadaElementSnapshot 的类注释），读回来不需要类型信息；
            // 而一旦写出 $type，读盘就得配一张白名单——那正是 SolutionService 上
            // 让人不敢随手放宽的那道口子，没必要为模板再开一张。
        };

        /// <summary>名字比对一律忽略大小写：现场没人记得住当初存的是 Valve 还是 valve</summary>
        private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

        /// <summary>
        /// 程序目录下的共享实例（<b>生产路径用的就是它</b>）。
        ///
        /// 断言工程要的是"每个用例一个干净文件"，所以它们用构造函数自己造实例、
        /// 显式把路径指到临时目录去，不会碰这一个。
        /// </summary>
        public static ScadaTemplateStore Shared { get; } = new();

        private readonly string _storePath;
        private readonly List<ScadaTemplateRecord> _templates = new();

        /// <param name="storePath">
        /// 模板文件路径。传 <c>null</c> 用程序目录下的 <c>ScadaTemplates.json</c>；
        /// 断言工程会显式指定临时路径。
        /// </param>
        public ScadaTemplateStore(string? storePath = null)
        {
            _storePath = storePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultFileName);
            Load();
        }

        /// <summary>模板文件的实际路径（界面上"模板存在哪"要能查到）</summary>
        public string StorePath => _storePath;

        /// <summary>
        /// 上次 <see cref="Load"/> 的失败原因或提示；一切正常为 <c>null</c>。
        ///
        /// 为什么不直接抛：模板文件读不出来时软件仍要能启动（按空清单处理），
        /// 但"你的模板没读出来"必须能被告知——否则用户会以为模板是自己弄丢的。
        /// </summary>
        public string? LastLoadError { get; private set; }

        /// <summary>
        /// 是否只读（磁盘上是更新版本写的文件，见类注释）。为 <c>true</c> 时三个写操作
        /// 全部拒绝并给出原因，用户至少知道"为什么存不进去"。
        /// </summary>
        public bool IsReadOnly { get; private set; }

        /// <summary>
        /// 模板库变了（新增 / 改名 / 删除 / 重新加载）。
        ///
        /// 为什么需要这个通知：存模板的入口在<b>画布的右键菜单</b>里，看模板的地方在
        /// <b>工具箱面板</b>里，两处谁都不认识谁。中间只搁这一个通知，工具箱就不必为了
        /// "存完能立刻看见"去反向依赖编辑器视图模型——那是它至今最值钱的性质。
        ///
        /// 订阅方务必把订阅做成<b>可逆</b>的（入树挂、离树摘，见
        /// <c>ScadaToolboxViewModel.Attach/Detach</c>）：<see cref="Shared"/> 是静态的，
        /// 订阅了不摘，那个订阅者就再也回收不了——而工具箱面板正是随 AvalonDock
        /// 反复装卸的那一种，几轮下来就会攒下一串永远收不到通知的僵尸。
        /// </summary>
        public event EventHandler? Changed;

        /// <summary>模板清单（脱敏视图，按名字排序，工具箱直接绑）</summary>
        public IReadOnlyList<ScadaTemplateInfo> Templates
            => _templates
                .OrderBy(t => t.Name, NameComparer)
                .Select(t => new ScadaTemplateInfo(t.TemplateId, t.Name, t.Payload?.Items.Count ?? 0, t.CreatedUtc))
                .ToArray();

        /// <summary>
        /// 取模板负载（插入 / 拖放时用）。不存在返回 <c>null</c>。
        ///
        /// 返回的是<b>库里的那一个实例</b>，不做深拷贝：调用方
        /// <see cref="ScadaClipboard.Materialize"/> 只读不写（每次物化都新建图元），
        /// 而每插一次模板就整份克隆一遍，等于给"拖模板"这个高频动作加一道没必要的开销。
        /// </summary>
        public ScadaClipboardPayload? GetPayload(Guid templateId) => Find(templateId)?.Payload;

        /// <summary>
        /// 存一个新模板（图元右键「存为模板」走这里）。重名自动避让成 <c>名字_2</c>。
        /// </summary>
        /// <param name="name">用户输入的名字（首尾空格会被去掉）</param>
        /// <param name="payload">要存的图元快照</param>
        /// <param name="saved">成功时给出落库后的那一行（名字可能与输入不同，见重名口径）</param>
        /// <param name="error">失败原因（直接可展示给用户的中文文案）</param>
        public bool TrySave(string? name, ScadaClipboardPayload? payload,
                            out ScadaTemplateInfo? saved, out string? error)
        {
            saved = null;

            if (!CheckName(name, out string clean, out error)) return false;

            // 空模板一律拒收：存进去在工具箱里是一行点不动的项，用户会当成"软件坏了"。
            // 真正的空负载只会来自"什么都没选中就点了存为模板"，那种情况应当在调用方就拦掉。
            if (payload == null || payload.IsEmpty)
            {
                error = "没有可保存的图元";
                return false;
            }

            if (!CheckWritable(out error)) return false;

            clean = MakeUniqueName(clean);

            var record = new ScadaTemplateRecord
            {
                TemplateId = Guid.NewGuid(),
                Name = clean,
                CreatedUtc = DateTime.UtcNow,
                Payload = payload,
            };

            _templates.Add(record);

            // 写盘失败时 Persist 已经把内存态退回磁盘上的样子（见其注释），这里不必再摘记录
            if (!Persist(out error)) return false;

            saved = new ScadaTemplateInfo(record.TemplateId, record.Name, record.Payload.Items.Count, record.CreatedUtc);

            RaiseChanged();
            return true;
        }

        /// <summary>重命名模板。重名<b>拒绝</b>而不是避让（理由见类注释）。</summary>
        public bool TryRename(Guid templateId, string? newName, out string? error)
        {
            var record = Find(templateId);

            if (record == null)
            {
                error = "模板不存在，可能已被删除";
                return false;
            }

            if (!CheckName(newName, out string clean, out error)) return false;

            if (NameComparer.Equals(record.Name, clean))
            {
                error = null;
                return true;   // 名字没变是空操作：不写盘、不产生一次无意义的文件替换
            }

            if (_templates.Any(t => !ReferenceEquals(t, record) && NameComparer.Equals(t.Name, clean)))
            {
                error = $"已有名为「{clean}」的模板";
                return false;
            }

            if (!CheckWritable(out error)) return false;

            record.Name = clean;

            if (!Persist(out error)) return false;

            RaiseChanged();
            return true;
        }

        /// <summary>删除模板。</summary>
        public bool TryRemove(Guid templateId, out string? error)
        {
            var record = Find(templateId);

            if (record == null)
            {
                error = "模板不存在，可能已被删除";
                return false;
            }

            if (!CheckWritable(out error)) return false;

            _templates.Remove(record);

            if (!Persist(out error)) return false;

            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 从磁盘重新加载（外部改了模板文件后手动刷新用）。读不出来按空清单处理，
        /// 原因留在 <see cref="LastLoadError"/> 里。
        /// </summary>
        public void Load()
        {
            LoadCore();
            RaiseChanged();
        }

        /// <summary>
        /// 真正读盘的那一段。与 <see cref="Load"/> 分开只为一件事：读完之后（无论成败）
        /// 都要发一次 <see cref="Changed"/>，而读盘路径上有好几个 <c>return</c>。
        /// </summary>
        private void LoadCore()
        {
            LastLoadError = null;
            IsReadOnly = false;
            _templates.Clear();

            try
            {
                if (!File.Exists(_storePath))
                    return;   // 首次运行：空清单。不落盘，免得"只是打开过一次软件"就留下一个空文件

                var json = File.ReadAllText(_storePath, Encoding.UTF8);
                var file = JsonConvert.DeserializeObject<ScadaTemplateFile>(json, JsonOptions);

                if (file == null)
                {
                    LastLoadError = "模板文件内容为空，已按空清单处理";
                    return;
                }

                if (file.Version > CurrentVersion)
                {
                    IsReadOnly = true;
                    LastLoadError = $"模板文件由更新版本的软件写入（v{file.Version} > v{CurrentVersion}），本次只读，不会覆盖它";
                }

                int skipped = 0;

                foreach (var record in file.Templates ?? new List<ScadaTemplateRecord>())
                {
                    // 逐条剔除残缺记录（手工编辑、写到一半、别的版本写的字段）：
                    // 留着它们不会多出什么，但在工具箱里是一行点不动的项，用户只会觉得软件坏了。
                    if (record == null) { skipped++; continue; }
                    if (record.TemplateId == Guid.Empty) { skipped++; continue; }
                    if (string.IsNullOrWhiteSpace(record.Name)) { skipped++; continue; }
                    if (record.Payload == null || record.Payload.IsEmpty) { skipped++; continue; }
                    if (record.Payload.Version > ScadaClipboardPayload.CurrentVersion) { skipped++; continue; }
                    if (_templates.Any(t => t.TemplateId == record.TemplateId)) { skipped++; continue; }

                    _templates.Add(record);
                }

                if (skipped > 0)
                {
                    var note = $"另有 {skipped} 条模板记录不可用，已跳过";
                    LastLoadError = LastLoadError == null ? note : $"{LastLoadError}；{note}";
                }
            }
            catch (Exception ex)
            {
                // 文件读坏：按空清单处理，但**允许写入**——否则用户再也存不进任何模板，
                // 而那个坏文件本来也读不出东西（与 ScadaUserStore 读坏回落出厂账号同一取舍）。
                LastLoadError = $"模板文件读取失败，已按空清单处理。原因：{ex.Message}";
                _templates.Clear();
                IsReadOnly = false;
            }
        }

        /// <summary>写盘（原子写：先写临时文件再替换，断电不丢已有模板）</summary>
        public bool Save(out string? error)
        {
            try
            {
                var dir = Path.GetDirectoryName(_storePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var file = new ScadaTemplateFile { Version = CurrentVersion, Templates = _templates };
                var json = JsonConvert.SerializeObject(file, JsonOptions);

                var tmp = _storePath + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);

                if (File.Exists(_storePath)) File.Replace(tmp, _storePath, null);
                else File.Move(tmp, _storePath);

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = $"模板文件保存失败：{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 写盘并处理失败：写不进去就<b>把内存态退回磁盘上的样子</b>。
        ///
        /// 与 <see cref="ScadaUserStore.Persist"/> 逐字同款：不退回的话，界面里模板明明
        /// 多了一个、重启后却不见了，而最早那次真实报错早从屏幕上消失了。
        /// </summary>
        private bool Persist(out string? error)
        {
            if (Save(out error)) return true;

            Load();
            return false;
        }

        /// <summary>
        /// 广播"模板库变了"。只在 <see cref="Changed"/> 上做一次空判，订阅方自己的异常
        /// 不在这里吞——库的写入不该被一个刷新列表的订阅者搞成败。
        /// </summary>
        private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        private ScadaTemplateRecord? Find(Guid templateId)
            => templateId == Guid.Empty ? null : _templates.FirstOrDefault(t => t.TemplateId == templateId);

        private bool Exists(string name) => _templates.Any(t => NameComparer.Equals(t.Name, name));

        /// <summary>
        /// 重名避让：续 <c>_2</c>、<c>_3</c>。与
        /// <see cref="ScadaPage.MakeUniqueElementName"/> 同一口径（含"候选上界取数量 + 2"的收口）。
        /// </summary>
        private string MakeUniqueName(string baseName)
        {
            if (!Exists(baseName)) return baseName;

            for (int n = 2; n <= _templates.Count + 2; n++)
            {
                string candidate = $"{baseName}_{n}";
                if (!Exists(candidate)) return candidate;
            }

            return $"{baseName}_{_templates.Count + 1}";
        }

        private bool CheckWritable(out string? error)
        {
            if (!IsReadOnly)
            {
                error = null;
                return true;
            }

            error = $"模板文件由更新版本的软件写入，本版本只读，无法保存：{_storePath}";
            return false;
        }

        private static bool CheckName(string? name, out string clean, out string? error)
        {
            clean = (name ?? string.Empty).Trim();

            if (clean.Length == 0)
            {
                error = "模板名不能为空";
                return false;
            }

            if (clean.Length > MaxNameLength)
            {
                error = $"模板名不能超过 {MaxNameLength} 个字符";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 落盘用的文件头。<b>私有嵌套</b>与 <c>ScadaUserStore.ScadaUserRecord</c> 同款：
        /// 外面永远只拿到 <see cref="ScadaTemplateInfo"/> 视图，不会有人图省事把记录直接绑到界面上。
        /// Newtonsoft 走反射访问公开成员，不影响文件格式。
        /// </summary>
        private sealed class ScadaTemplateFile
        {
            public int Version { get; set; } = CurrentVersion;

            public List<ScadaTemplateRecord>? Templates { get; set; } = new();
        }

        /// <summary>落盘用的一条模板记录</summary>
        private sealed class ScadaTemplateRecord
        {
            public Guid TemplateId { get; set; }

            public string Name { get; set; } = string.Empty;

            public DateTime CreatedUtc { get; set; }

            public ScadaClipboardPayload? Payload { get; set; }
        }
    }
}
