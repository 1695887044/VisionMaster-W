namespace VisionMaster.Scada
{
    /// <summary>
    /// 组态事件钩子的种类（"画面加载""按钮按下""值改变"这类）。
    ///
    /// 为什么这个枚举住在领域层而不是控件层：它是要写进 .vms 的长期标识之一
    /// （将来"哪个事件配了什么动作"落盘，落的就是这个值），而落盘格式必须比界面稳定——
    /// 属性面板换模板、控件换实现，都不该动摇文件里的取值。本层不依赖 WPF，
    /// 放这里正好让控件层和主程序都往上看，依赖方向不变。
    ///
    /// 数值一旦发布不许改（与 <c>Hmi.</c>TypeKey 同一条规矩），新增只能往后追加。
    /// 名字刻意不带 Page / Button 前缀：Loaded 就是"这个对象装载完了"，
    /// 画面用它是画面加载完，图元用它是图元建完，语义同一个，不必各造一套词。
    ///
    /// S5 起 <see cref="Loaded"/>（画面级，宿主在首帧画完后上报）、<see cref="Pressed"/> 与
    /// <see cref="Released"/>（图元级，画布代发）三条真能触发；<see cref="Unloaded"/> 要等 S8 画面导航，
    /// <see cref="ValueChanged"/> 要等 S6 数据泵把变量值刷进图元属性。
    /// 在这之前它们<b>不许出现在任何图元/画面的事件清单里</b>——清单里有一条发不出来的事件，
    /// 用户配上的就是个永远不响的钩子（声明口径见 <c>BuiltInElements</c> 类注释）。
    /// </summary>
    public enum ScadaEventType
    {
        /// <summary>画面加载完成（运行态把该画面显示出来、首帧画完之后触发一次）</summary>
        Loaded = 0,

        /// <summary>卸载（画面被切走或运行停止）；S8 画面导航接</summary>
        Unloaded = 1,

        /// <summary>按下（鼠标按下／触屏按下）；图元级</summary>
        Pressed = 2,

        /// <summary>释放（在同一个图元上抬起）；图元级</summary>
        Released = 3,

        /// <summary>绑定的值改变；图元级，S6 数据泵接</summary>
        ValueChanged = 4,
    }
}
