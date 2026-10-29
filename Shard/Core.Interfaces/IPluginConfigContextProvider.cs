using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces.Database;

namespace Core.Interfaces
{
    /// <summary>
    /// 配置态插件视图的宿主上下文（只读快照）
    ///
    /// 为什么需要它
    /// ---------
    /// 插件 DLL 是独立程序集，只引用 Core.Interfaces（见 Plugin.*.csproj），
    /// 拿不到宿主的 AppSettingsService / HttpImageServer —— 也就读不到
    /// AppConfig.json 里的监听端口、访问令牌，更不知道"当前流程叫什么"。
    /// 于是由宿主在打开配置窗口时把这份快照推给插件视图（见 PluginConfigShellViewModel）。
    ///
    /// 为什么是"快照"而不是"活对象"
    /// ---------
    /// 插件视图只用于"照着当前配置拼一次请求"（如本地图片推送测试），
    /// 不需要感知宿主配置的热变更；传值类型可以彻底避免插件反向持有宿主对象、
    /// 在窗口关闭后仍引用已释放资源。
    /// </summary>
    public class PluginConfigContext
    {
        /// <summary>
        /// 当前流程名
        /// HTTP 收图按流程名分槽，URL 路径 /flow/{流程名} 里也要用（拼 URL 前必须转义）
        /// </summary>
        public string FlowName { get; set; } = string.Empty;

        /// <summary>
        /// 宿主 HTTP 收图服务在 AppConfig.json 里是否启用
        /// </summary>
        public bool HttpEnabled { get; set; }

        /// <summary>
        /// 宿主 HTTP 收图服务此刻是否真的在监听
        /// 与 <see cref="HttpEnabled"/> 可能不一致：端口被占用、启动抛异常时配置是启用的但没在听
        /// </summary>
        public bool HttpListening { get; set; }

        /// <summary>
        /// 推送目标主机
        /// 已由宿主把 0.0.0.0 / [::] 这类"监听任意网卡"的地址映射为可连接的本地回环地址
        /// </summary>
        public string HttpHost { get; set; } = "127.0.0.1";

        /// <summary>
        /// 推送目标端口
        /// </summary>
        public int HttpPort { get; set; }

        /// <summary>
        /// 访问令牌（请求头 Authorization: Bearer {HttpToken}）
        /// </summary>
        public string HttpToken { get; set; } = string.Empty;

        /// <summary>
        /// 服务端等待流程执行完成的上限（毫秒）
        /// 客户端自身的超时应略大于它，否则服务端还在跑，客户端先断，看到的错误会失真
        /// </summary>
        public int RequestTimeoutMs { get; set; }

        /// <summary>
        /// 当前方案已配置的相机（供"相机采集"模式的下拉选择）。
        ///
        /// 为什么是 <see cref="CameraOption"/> 这个拷贝而不是直接给 CameraDescriptor：
        /// 守住"配置上下文是快照"这条纪律。直接给描述符等于让插件持有宿主方案里的活对象，
        /// 插件即使在窗口关闭后仍能改它（比如顺手改了序列号），而宿主完全察觉不到——
        /// 这类"插件改宿主配置"的问题只在下次保存方案时以"配置怎么变了"的形式暴露。
        /// </summary>
        public List<CameraOption> Cameras { get; set; } = new();

        /// <summary>
        /// 当前方案里"可以拿来当变量名的名字"快照（供插件配置界面的「变量名」行做下拉候选）。
        ///
        /// 为什么必须有：插件够不到"变量管理"所在的程序集，而"给哪个变量赋值/读哪个变量"
        /// 恰恰只能从那里选。靠用户手打名字的代价已经实测过（2026-10-10）：打错的后果
        /// 要么是运行期"找不到全局变量"，要么是**把值写进了另一个变量** —— 两种都很难倒查。
        ///
        /// 与 <see cref="Cameras"/> 同一范式：拷贝一份值（名字/类型/说明），
        /// 不让插件视图持有宿主方案里的活对象。
        /// </summary>
        public List<VariableOption> Variables { get; set; } = new();

        /// <summary>
        /// 数据库能力（供「数据库查询/执行/写行」的配置窗口做「测试语句」）。
        ///
        /// 与 <see cref="Cameras"/> / <see cref="Variables"/> 的"值快照"不同，这是一个**能力对象**：
        /// 测试必须真连库，快照给不了。插件只能通过它执行命名语句，拿不到连接/事务/SQL 编辑入口，
        /// 因此不破坏"配置上下文不递活对象"的边界。默认是空实现，未接数据库时测试按钮给出明确失败。
        /// </summary>
        public IDatabaseAccess Database { get; set; } = NullDatabaseAccess.Instance;

        /// <summary>
        /// 当前方案里**有文件本体**的资产（模板 / 模型 / 图像），带已解析好的绝对路径。
        ///
        /// 为什么必须有：配置态的图（标定源图、模板参考图）要能在界面上直接看到，
        /// 而 <c>ProjectAssetPathResolver.TryResolve</c> 只能在运行期用（要 IExecutionContext）。
        /// 没有这份快照，插件就只能让用户手填绝对路径 —— 换台机器就断，正是资产系统要解决的问题。
        ///
        /// 与 <see cref="Cameras"/> 同一范式：拷贝值（Id/类型/名字/路径/版本），
        /// 不递宿主方案里的活对象；已归档或文件缺失的资产**不进来**（宿主侧就过滤掉）。
        /// 配方 / 标定没有独立文件，不在本表（它们的内联内容走运行期解析）。
        /// </summary>
        public List<AssetPathOption> Assets { get; set; } = new();
    }

    /// <summary>
    /// 资产路径快照（供插件配置界面列出"能选哪些图像/模板/模型"并直接取到路径）。
    /// </summary>
    public class AssetPathOption
    {
        /// <summary>资产稳定身份（写回步骤配置的就是它，不是路径）</summary>
        public Guid AssetId { get; set; }

        /// <summary>资产类型：template / model / image</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>资产名（资产页里的名字，可能随时改；寻址一律用 <see cref="AssetId"/>）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>已解析的绝对路径（宿主保证非空且文件存在）</summary>
        public string AbsolutePath { get; set; } = string.Empty;

        /// <summary>资产版本号（版本库里的 vN）</summary>
        public int Version { get; set; }

        /// <summary>
        /// 供 <c>ComboBox.SelectedValuePath</c> 用的字符串形式。
        /// 不用 <see cref="AssetId"/> 直绑：ViewModel 侧存的是字符串（步骤配置里也是字符串），
        /// Guid↔string 的隐式转换在"清空选择"时会抛转换失败，表现为选完又跳回去。
        /// </summary>
        public string AssetIdText => AssetId.ToString();

        /// <summary>下拉框显示文本：名字（v版本）；空 Id 是"不使用资产"这一项，原样显示名字</summary>
        public string Caption => AssetId == Guid.Empty
            ? Name
            : string.IsNullOrWhiteSpace(Name) ? AssetId.ToString("N")[..8] : $"{Name}（v{Math.Max(1, Version)}）";
    }

    /// <summary>
    /// 配置态"资产 Id → 绝对路径"的解析口径。
    ///
    /// 与运行期的 <c>ProjectAssetPathResolver</c> 成对：那边拿 <c>IExecutionContext</c>，
    /// 这边拿 <see cref="PluginConfigContext.Assets"/> 快照。判据保持一致（类型必须匹配、
    /// 路径必须存在），报错文案也说清"该选哪类资产"，别让用户对着空画布猜。
    /// </summary>
    public static class PluginConfigAssetPaths
    {
        public static bool TryResolve(
            PluginConfigContext? context,
            string? assetId,
            string expectedKind,
            out string path,
            out string error)
        {
            path = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(assetId))
            {
                error = "未选择资产";
                return false;
            }

            if (!Guid.TryParse(assetId.Trim(), out var id) || id == Guid.Empty)
            {
                error = $"资产 Id 不是合法 Guid：{assetId}";
                return false;
            }

            if (context?.Assets == null)
            {
                error = "配置上下文缺失，无法解析资产";
                return false;
            }

            var hit = context.Assets.FirstOrDefault(a => a.AssetId == id);
            if (hit == null)
            {
                error = "当前方案里没有该资产（可能已归档、文件缺失或方案未保存）";
                return false;
            }

            if (!string.Equals(hit.Kind, expectedKind, StringComparison.OrdinalIgnoreCase))
            {
                error = $"资产「{hit.Name}」的类型是 {hit.Kind}，这里需要 {expectedKind}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(hit.AbsolutePath))
            {
                error = $"资产「{hit.Name}」没有可读取的文件路径";
                return false;
            }

            path = hit.AbsolutePath;
            return true;
        }
    }

    /// <summary>
    /// 变量名候选快照（供插件配置界面列出"能选哪些变量名"）。
    ///
    /// 与 <see cref="CameraOption"/> 同一范式 —— 只带展示与寻址需要的字段，值不进场。
    /// </summary>
    public class VariableOption
    {
        /// <summary>
        /// 变量稳定身份（<c>IVariable.VariableId</c>）。
        /// 声明式共享变量按它寻址：变量改名后声明仍能命中（与 <c>LinkReference</c> 的 Id 口径一致）。
        /// 运行时变量（Scope=Runtime）没有稳定身份，这里为 <see cref="Guid.Empty"/>。
        /// </summary>
        public Guid VariableId { get; set; }

        /// <summary>变量名（写进流程的就是它；选定后由插件落成**常量**，不是连线）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 作用域取值："Global"（变量管理里那些，跨流程随方案存盘）
        /// / "Runtime"（流程执行中由「变量定义」节点创建的本地变量）
        /// / "System"（运动轴状态投影，只读、不落盘）。
        /// 取值与「变量赋值」插件的 Scope 端口同一套字面量，界面按它过滤候选。
        /// </summary>
        public string Scope { get; set; } = "Global";

        /// <summary>
        /// 声明类型的短名（如 Int32 / String / HImage）。
        /// 为什么界面必须有它：值那一行只能填文本，用户要据此才知道"该填整数还是文本"——
        /// 类型不匹配是这条链上最常见的失败（真机 2026-10-10 反馈里就是它）。
        /// </summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>说明文字（变量管理里的描述；运行时变量则为"由哪个节点声明"）</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>是否系统变量（只读；写入会在 <c>GlobalVariableWriter</c> 那一层被拒）</summary>
        public bool IsSystem { get; set; }

        /// <summary>
        /// 是否可写（宿主侧按 <c>IWritableVariable</c> 判定）。
        /// 界面据此把方向锁成"只读"，而不是让用户选完"读写"、运行期才报错。
        /// </summary>
        public bool CanWrite { get; set; }

        /// <summary>
        /// 所属设备（网络变量的连接名）；本地变量、运行时变量为空串。
        ///
        /// 为什么必须带出来：多设备允许同名点位之后（同一设备内唯一），
        /// 下拉里若只有名字，两台设备的 <c>Alarm[90]</c> 就是两条一模一样的候选，
        /// 用户等于没得选——而选错设备的后果是把值写到另一台设备上。
        /// </summary>
        public string Device { get; set; } = string.Empty;

        /// <summary>下拉框显示文本：名字（类型）；有设备时补「· 设备」</summary>
        public string Caption
        {
            get
            {
                var head = string.IsNullOrWhiteSpace(TypeName) ? Name : $"{Name}（{TypeName}）";
                return string.IsNullOrWhiteSpace(Device) ? head : $"{head} · {Device}";
            }
        }
    }

    /// <summary>
    /// 把"配置里存的目标变量"落到稳定 Id：**Id 优先、名字兜底**。
    ///
    /// 三个插件（变量赋值 / 数据记录 / 结果上报）在确认配置时都用它，口径只有一份：
    /// - 已有 Id 且能在候选里找到 → 保留（变量改名后行里的旧名字不动，Id 仍然有效）；
    /// - Id 找不到（变量被删）或为空 → 按名字在候选里**唯一**命中时补上；
    ///   重名时留空，运行期会明确报歧义，而不是替用户选一台设备。
    /// </summary>
    public static class VariableOptionResolver
    {
        /// <summary>解析目标 Id；无法唯一确定时返回 <see cref="Guid.Empty"/>（= 按名字兜底）。</summary>
        public static Guid CaptureId(
            IReadOnlyList<VariableOption>? options, Guid currentId, string? name, string scope)
        {
            // 没有候选快照（运行期 / 无宿主 / 断言夹具）：不动配置，保留原 Id
            if (options == null)
                return currentId;

            if (currentId != Guid.Empty)
            {
                for (var i = 0; i < options.Count; i++)
                {
                    var option = options[i];
                    if (option != null && option.VariableId == currentId)
                        return currentId;
                }
            }

            var key = (name ?? string.Empty).Trim();
            if (key.Length == 0)
                return Guid.Empty;

            var found = Guid.Empty;
            var count = 0;
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (option == null)
                    continue;
                if (!string.Equals(option.Scope ?? string.Empty, scope ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals((option.Name ?? string.Empty).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    continue;

                found = option.VariableId;
                count++;
            }

            return count == 1 ? found : Guid.Empty;
        }
    }

    /// <summary>
    /// 相机选项快照（供插件配置界面列出"能选哪些相机"）。
    /// 只有展示与寻址需要的字段：寻址一律用序列号（见 CameraDescriptor.SerialNo 的说明）。
    /// </summary>
    public class CameraOption
    {
        /// <summary>相机内部 Id（仅供界面回显与状态查询，插件寻址用序列号）</summary>
        public System.Guid Id { get; set; }

        /// <summary>序列号：相机采集步骤引用相机的唯一键</summary>
        public string SerialNo { get; set; } = string.Empty;

        /// <summary>界面显示名（可随时改，不影响寻址）</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 连接状态（枚举而不是只给文字）。
        /// 为什么必须给枚举：插件侧要按状态决定"这是正常、还是会让运行时失败"，
        /// 拿中文串去比较等于把界面文案变成了逻辑契约，改一次文案就断。
        /// </summary>
        public CameraConnectionState State { get; set; }

        /// <summary>状态文字（如"采流中 / 等待客户端接入"），供界面直接显示，避免用户以为配错了</summary>
        public string StateText { get; set; } = string.Empty;

        /// <summary>下拉框显示文本：显示名（序列号）</summary>
        public string Caption =>
            string.IsNullOrWhiteSpace(DisplayName) ? SerialNo : $"{DisplayName}（{SerialNo}）";
    }

    /// <summary>
    /// 插件配置视图的上下文接收者（可选实现）
    ///
    /// 实现者：既实现了 <see cref="IPluginCustomViewProvider"/>，又需要知道宿主运行环境的插件。
    /// 不实现此接口的插件不受任何影响 —— 宿主在 <c>OnDialogOpened</c> 里做一次
    /// <c>is IPluginConfigContextProvider</c> 判定，不匹配就跳过，属于非破坏性扩展。
    ///
    /// 调用时机：宿主在配置窗口打开、且已把插件视图挂进内容区之后调用一次。
    /// 实现方应把值存进公开可绑定属性（视图 DataContext 即插件实例，绑定自动生效）。
    /// </summary>
    public interface IPluginConfigContextProvider
    {
        /// <summary>
        /// 接收宿主透传的配置上下文快照
        /// </summary>
        /// <param name="context">上下文快照，宿主保证非 null</param>
        void SetConfigContext(PluginConfigContext context);
    }
}
