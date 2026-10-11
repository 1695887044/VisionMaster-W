using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Models
{
    /// <summary>
    /// 并行分支的"记账字典"（二期，评审高危 7）：影子存储 + 写入记账。
    ///
    /// 为什么不能拿父域字典做快照 diff（v1 缺陷，v2 废弃）：
    ///  (a) "写回与快照相等的值视为未写"会漏报同值写入——"≥2 分支写同名⇒Warn"的承诺失效；
    ///  (b) 精确记账必须换掉 LocalVariables 实例；
    ///  (c) 值比较的 Equals 可能抛（字典可放任意 object），在父线程打断合并。
    /// 记账制从根上消除三条：写路径先登记 key，合并不做值比较。
    ///
    /// 线程纪律：影子里程内只有本分支线程读写（种子浅拷在扇出前的父线程完成）；
    /// join 之后 WrittenKeys/_store 由父线程独占读取——两段之间由 join 的全分支等待隔离。
    /// 插件零改动：既有代码写 context.LocalVariables[name] = value 被本装饰器透明拦截记账。
    /// </summary>
    public sealed class BranchVariableRegistry : IDictionary<string, object>
    {
        /// <summary>影子存储（种子 = 父域浅拷）</summary>
        private readonly Dictionary<string, object> _store;

        /// <summary>写入记账（本分支线程独占写，join 后父线程读）</summary>
        private readonly HashSet<string> _writtenKeys = new();

        /// <summary>构造：种子来自父域 LocalVariables 的浅拷（引用复制，不 clone 值）</summary>
        public BranchVariableRegistry(IDictionary<string, object> seed)
        {
            _store = seed == null
                ? new Dictionary<string, object>()
                : new Dictionary<string, object>(seed);
        }

        /// <summary>本分支真实写入过的变量名集合（join 后供合并与冲突告警按"真实写入"统计）</summary>
        public IReadOnlyCollection<string> WrittenKeys => _writtenKeys;

        // ============ IDictionary<string, object> 样板：写路径先登记，读路径直通 ============

        public object this[string key]
        {
            get => _store[key];
            set
            {
                _writtenKeys.Add(key);
                _store[key] = value;
            }
        }

        public ICollection<string> Keys => _store.Keys;

        public ICollection<object> Values => _store.Values;

        public int Count => _store.Count;

        public bool IsReadOnly => false;

        public void Add(string key, object value)
        {
            _writtenKeys.Add(key);
            _store.Add(key, value);
        }

        public void Add(KeyValuePair<string, object> item)
        {
            _writtenKeys.Add(item.Key);
            ((ICollection<KeyValuePair<string, object>>)_store).Add(item);
        }

        public bool Remove(string key)
        {
            _writtenKeys.Add(key); // 删除也是"写"：记名让合并知道本分支动过这个槽位
            return _store.Remove(key);
        }

        public bool Remove(KeyValuePair<string, object> item)
        {
            _writtenKeys.Add(item.Key);
            return ((ICollection<KeyValuePair<string, object>>)_store).Remove(item);
        }

        public bool TryGetValue(string key, out object value) => _store.TryGetValue(key, out value);

        public bool ContainsKey(string key) => _store.ContainsKey(key);

        public bool Contains(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_store).Contains(item);

        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
            => ((ICollection<KeyValuePair<string, object>>)_store).CopyTo(array, arrayIndex);

        public void Clear()
        {
            foreach (var key in _store.Keys)
                _writtenKeys.Add(key);
            _store.Clear();
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _store.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
