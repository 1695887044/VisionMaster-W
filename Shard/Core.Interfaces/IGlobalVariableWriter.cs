namespace Core.Interfaces
{
    /// <summary>
    /// 全局变量写入口（运行期能力契约）。
    ///
    /// 为什么需要它：插件工程只引用 Core.Interfaces，物理上够不到"变量管理"所在的程序集，
    /// 于是插件想把自己的产出（例如 HImage）交给全局变量时没有任何正门——
    /// 历史上只有"读全局变量"的连线方向，写方向在代码里并不存在
    /// （所以照着注释做的插件会"编译通过、运行静默失败"）。
    /// 本接口把"写全局变量"收敛成一个能力对象挂到执行上下文上，
    /// 插件只认这个契约；查注册表、类型守门、真正落值都留在主程序侧。
    ///
    /// 参数与返回值一律用最朴素的 object 与 string，不引入任何领域类型，
    /// 这样它才能放在依赖链最底层的 Shard 层，被插件直接引用。
    /// </summary>
    public interface IGlobalVariableWriter
    {
        /// <summary>
        /// 按变量名写入全局变量。
        /// 成功返回 true；失败返回 false，且 <paramref name="error"/> 是可以直接展示给操作员的中文原因
        /// （变量不存在、类型不匹配、变量不支持写入等各有明确说法，禁止静默失败）。
        /// </summary>
        /// <param name="name">目标全局变量名（大小写不敏感，与变量管理的查重口径一致）</param>
        /// <param name="value">要写入的值；其类型必须能赋给变量的声明类型</param>
        /// <param name="error">失败原因；成功时为空</param>
        bool TryWrite(string name, object? value, out string? error);
    }
}
