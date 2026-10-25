using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Services
{
    /// <summary>
    /// 落点提示文案的唯一拼装处（设计基线 docs/UI设计基线/drag-drop-hint v2 §04）。
    ///
    /// 【为什么单独成类】文案公式是"改一个字就影响两处入口"的东西（流程栏 + 画布），
    /// 而且断言要能直接验字符串——放在 VM 里就只能靠界面看。这里全是纯函数，无状态。
    ///
    /// 公式：<c>[动词] 「名称」→ [路径] · [位置]</c>
    ///  · 动词只认拖拽效果：Copy → 添加；Move → 移动（不按"从哪个面板拖的"猜）。
    ///  · 路径分隔符固定 <c>/</c>（用户 2026-10-24 拍板）。
    ///  · 顶层位置名固定「主流程」——不是流程名（流程名在标签栏上已经有了）。
    /// </summary>
    public static class DragHintText
    {
        /// <summary>顶层的位置名（不是流程名，见类注释）</summary>
        public const string TopLevelPath = "主流程";

        /// <summary>追加到末尾的位置词</summary>
        public const string PositionEnd = "末尾";

        /// <summary>运行锁文案（与 EditGate.RunLockedMessage 同源，不另起一份）</summary>
        public static string Locked => EditGate.RunLockedMessage;

        /// <summary>添加（Copy）</summary>
        public static string Add(string stepName, string path, string position = PositionEnd)
            => Build("添加", stepName, path, position);

        /// <summary>移动（Move）</summary>
        public static string Move(string stepName, string path, string position = PositionEnd)
            => Build("移动", stepName, path, position);

        /// <summary>按效果选动词，再套公式</summary>
        public static string Build(bool isCopy, string stepName, string path, string position = PositionEnd)
            => Build(isCopy ? "添加" : "移动", stepName, path, position);

        /// <summary>公式本体</summary>
        public static string Build(string verb, string stepName, string path, string position)
            => $"{verb}「{stepName}」→ {path} · {position}";

        /// <summary>插到某个步骤之前（位置词本身带目标名）</summary>
        public static string InsertBeforePosition(string targetName) => $"插到「{targetName}」之前";

        /// <summary>插到某个步骤之后（位置词本身带目标名）</summary>
        public static string InsertAfterPosition(string targetName) => $"插到「{targetName}」之后";

        /// <summary>非法落点：原因必须可执行（"怎么才能放"），不能只写"不支持"</summary>
        public static string Illegal(string reason) => $"不能放这里：{reason}";

        /// <summary>需注意：落点在折叠容器内部，放下会展开它</summary>
        public static string WillExpand(string containerName) => $"会展开「{containerName}」后再放入";

        /// <summary>
        /// 路径拼接：≤2 级原样；≥3 级保留末两级、前缀省略为 <c>…</c>
        /// （叶子信息优先——深层嵌套时把最靠内的两级留给人看）。
        /// </summary>
        public static string Path(params string?[] segments)
        {
            var parts = (segments ?? new string?[0])
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .ToList();

            if (parts.Count == 0) return TopLevelPath;
            if (parts.Count <= 2) return string.Join(" / ", parts);

            return "… / " + string.Join(" / ", parts.Skip(parts.Count - 2));
        }

        /// <summary>路径拼接（集合重载）</summary>
        public static string Path(IReadOnlyList<string?> segments)
            => Path(segments?.ToArray() ?? new string?[0]);
    }
}
