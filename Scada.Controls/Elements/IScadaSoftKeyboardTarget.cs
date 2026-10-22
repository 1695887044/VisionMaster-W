namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 软键盘的<b>编辑目标</b>：运行窗口只认识这个接口，不直接依赖 TextField / IOField 的具体类型。
    ///
    /// 实现方负责"把键盘敲出来的字放进编辑框、提交/取消走原有校验与写变量路径"；
    /// 键盘本身不解析数值、不判上下限、不写变量——那些规则已经在控件里，复制一份必然分叉。
    ///
    /// 只读字段（<see cref="IsEditable"/> = false）不会成为目标：`BeginEdit()` 本身就不会进入编辑态，
    /// 键盘自然没有打开时机。
    /// </summary>
    public interface IScadaSoftKeyboardTarget
    {
        /// <summary>此刻是否处于就地编辑态（键盘打开/关闭的判据）</summary>
        bool IsEditing { get; }

        /// <summary>是否可写（只读/无写通道/无权限时为 false）</summary>
        bool IsEditable { get; }

        /// <summary>编辑框里的当前文本（键盘显示用；写入走 <see cref="SetEditText"/>）</summary>
        string? EditText { get; }

        /// <summary>最近一次提交失败的原因；空 = 没有错误</summary>
        string? EditError { get; }

        /// <summary>是否数值键盘（数字 + 小数点 + 正负号）</summary>
        bool IsNumeric { get; }

        /// <summary>文本长度上限（数值域给一个足够大的上限即可）</summary>
        int MaxLength { get; }

        /// <summary>单位（数值域用；文本域为 null）</summary>
        string? UnitText { get; }

        /// <summary>上下限/长度提示（如「范围 1–100」「最多 32 字」）</summary>
        string? RangeText { get; }

        /// <summary>进入编辑前的原值（给操作员做参照）</summary>
        string? OriginalText { get; }

        /// <summary>把键盘拼出的文本放进编辑框，并把光标放到末尾（不提交）</summary>
        void SetEditText(string text);

        /// <summary>提交：校验、类型转换、权限、写变量全部复用控件原有路径</summary>
        void CommitEdit();

        /// <summary>放弃编辑：退出编辑态、清错误，原值一点不动</summary>
        void CancelEdit();
    }
}
