using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;

namespace Plugin.CSharpScript
{
    /// <summary>
    /// 脚本对「共享变量」表的访问器：别名解析 + 未声明/读失败的中文错误。
    ///
    /// 为什么单独抽出来而不是写在 <see cref="ScriptContext"/> 里：
    /// ① <see cref="ScriptContext"/> 的构造是 internal（宿主每次执行前 new），断言工程造不出来；
    ///    访问器是纯逻辑（声明表 + 能力契约），可以直接在断言里构造，覆盖"未声明/类型转换/只读"；
    /// ② 别名解析、方向校验、错误文案只有一份，<c>GetGlobal</c> / <c>SetGlobal</c> 共用。
    /// </summary>
    public sealed class ScriptSharedAccessor
    {
        private readonly Dictionary<string, ScriptSharedVarDef> _byAlias = new(StringComparer.Ordinal);
        private readonly ISharedVariableAccess _store;

        /// <param name="declarations">左侧「共享变量」表的声明（可为 null/空）</param>
        /// <param name="store">运行期能力契约（可为 null：此时读写都会给出明确失败）</param>
        public ScriptSharedAccessor(IEnumerable<ScriptSharedVarDef> declarations, ISharedVariableAccess store)
        {
            _store = store;
            if (declarations == null) return;

            foreach (var def in declarations)
            {
                if (def == null) continue;
                var alias = def.EffectiveAlias;
                if (string.IsNullOrWhiteSpace(alias)) continue;
                // 同别名后者覆盖前者：界面会在"校验脚本"里把重复别名报出来，
                // 运行期只需要一个确定的行为（不允许随机命中）。
                _byAlias[alias] = def;
            }
        }

        /// <summary>已声明的别名快照（供校验/提示）</summary>
        public IReadOnlyCollection<string> Aliases => _byAlias.Keys;

        /// <summary>
        /// 按别名读共享变量；未声明/读失败抛 <see cref="InvalidOperationException"/>（消息可直接上屏）。
        /// </summary>
        public object Get(string alias)
        {
            var def = Require(alias);

            if (_store == null)
                throw new InvalidOperationException("当前执行环境不支持读取共享变量");

            if (!_store.TryRead(def.VariableId, def.Name, out var value, out var error))
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? $"读取共享变量「{alias}」失败"
                    : error);
            }

            return value;
        }

        /// <summary>按别名读并转成 T（空值返回 default；类型转不动抛异常，由宿主包成"脚本运行异常"）。</summary>
        public T Get<T>(string alias)
        {
            object value = Get(alias);
            if (value == null) return default;
            if (value is T typed) return typed;
            return (T)Convert.ChangeType(value, typeof(T));
        }

        /// <summary>
        /// 按别名写共享变量；未声明 / 声明只读 / 系统变量 / 写入失败都抛
        /// <see cref="InvalidOperationException"/>（消息可直接上屏）。
        /// </summary>
        public void Set(string alias, object value)
        {
            var def = Require(alias);

            string guard = ValidateWritable(def, alias);
            if (guard != null)
                throw new InvalidOperationException(guard);

            if (_store == null)
                throw new InvalidOperationException("当前执行环境不支持写入共享变量");

            if (!_store.TryWrite(def.VariableId, def.Name, value, out var error))
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? $"写入共享变量「{alias}」失败"
                    : error);
            }
        }

        /// <summary>写共享变量，失败返回 false 并带原因（脚本里想分支处理时用；不抛异常）。</summary>
        public bool TrySet(string alias, object value, out string error)
        {
            try
            {
                Set(alias, value);
                error = null;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 写方向的声明级闸门：系统变量恒只读；方向=只读时不许写。
        ///
        /// 为什么在访问器里再挡一次（宿主写入器也会拒绝系统变量）：
        /// ① 错误消息能直接指向"把方向改成读写"这个可执行动作，而不是等到写入器报"未实现可写契约"；
        /// ② 方向=只读是**用户声明**的意图，即便目标变量本身可写，也不该被脚本悄悄改掉。
        /// </summary>
        private static string ValidateWritable(ScriptSharedVarDef def, string alias)
        {
            if (def.IsSystem)
                return $"「{alias}」是系统变量（只读），不能写入";
            if (def.Access != ScriptSharedAccess.ReadWrite)
                return $"共享变量「{alias}」声明为只读，不能写入（把方向改成「读写」）";
            return null;
        }

        /// <summary>解析别名；未声明时给"该去哪声明"的可执行提示。</summary>
        private ScriptSharedVarDef Require(string alias)
        {
            var key = alias?.Trim();
            if (string.IsNullOrEmpty(key))
                throw new InvalidOperationException("共享变量别名不能为空");

            if (_byAlias.TryGetValue(key, out var def))
                return def;

            throw new InvalidOperationException(
                $"脚本引用了未声明的共享变量「{key}」：请在左侧「共享变量」表里添加一行（别名 = {key}）");
        }
    }
}
