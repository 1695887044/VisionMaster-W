namespace VisionMaster.Scada
{
    /// <summary>
    /// 操作角色（S12）。三档，<b>从低到高</b>：操作员 &lt; 工程师 &lt; 管理员。
    ///
    /// 为什么是"固定三档"而不是可配置的角色表
    /// ---------
    /// 角色表（"新建角色、勾权限点"）是 MES / 数据库类软件的模型，它要一套权限点清单、
    /// 一套角色-权限映射、一套管理界面。组态画面的权限需求却只有一句话：
    /// <b>"这个按钮不是谁都能按的"</b>。三档固定角色在工业 HMI 里是几十年的事实标准
    /// （Operator / Engineer / Administrator），现场人员一看就懂，配起来不用思考。
    /// 真要做权限点，那是 S13 之后按项目需求单独开的活，不该把本阶段拖成半个用户管理系统。
    ///
    /// 数值一旦发布不许改（与 <see cref="ScadaEventType"/> 同一条规矩），新增只能往后追加。
    /// 权限是<b>要落盘</b>的（<see cref="ScadaElement.RequiredRole"/>），改数值等于让旧文件里的
    /// "需要工程师"变成"需要管理员"——静默的权限放大，比报错严重得多。
    ///
    /// <see cref="Undefined"/> 不是"比操作员更低"，而是<b>非法值</b>：<c>default(ScadaRole)</c> 落在这里。
    /// 它存在的唯一理由是让"文件里存了个不认识的数值"（高版本存、低版本读）能被识别出来——
    /// 见 <see cref="ScadaRoleExtensions.IsDefined"/>。判定侧对它的处理是<b>一律拒绝</b>（fail-safe），
    /// 而不是"当成操作员放行"。
    /// </summary>
    public enum ScadaRole
    {
        /// <summary>非法/未定义（<c>default</c> 落点）。判定侧遇到它一律拒绝</summary>
        Undefined = 0,

        /// <summary>
        /// 操作员：看画面、按普通按钮。<b>未登录时的默认档</b>——
        /// 这不是"没权限"，而是"任何在场的人都有的那部分权限"。
        /// 现场开机自动运行、无人值守时，画面加载钩子照常跑，靠的就是这一条。
        /// </summary>
        Operator = 1,

        /// <summary>工程师：改参数、切维护画面、停机复位。需要登录</summary>
        Engineer = 2,

        /// <summary>管理员：改配置、改配方、改权限本身。需要登录</summary>
        Administrator = 3,
    }

    /// <summary>
    /// 角色的显示名与比较口径（属性面板那一行、状态栏、审计文件都用它）。
    ///
    /// 为什么放在领域层而不是界面侧的转换器：与 <see cref="ScadaEventTypeExtensions.DisplayName"/>
    /// 同一个理由——"工程师"这个词在属性面板上、状态栏里、审计文件里必须是同一个词。
    /// 三处各写一遍，早晚出现"面板叫工程师、日志叫 Engineer"，用户对不上号。
    /// </summary>
    public static class ScadaRoleExtensions
    {
        /// <summary>
        /// 是不是一个<b>认识的</b>角色。只用来识别"文件里存了非法数值"这一种情况，
        /// 不要拿它当权限判定——判定走 <see cref="IScadaAccessPolicy.CanOperate"/>。
        /// </summary>
        public static bool IsDefined(this ScadaRole role)
            => role == ScadaRole.Operator
            || role == ScadaRole.Engineer
            || role == ScadaRole.Administrator;

        /// <summary>
        /// 显示名。非法数值回落成"未知角色(N)"而不是抛异常：属性面板要能把一行坏数据画出来，
        /// 抛异常的表现是"画面打不开"，而实际只是某个图元上多了一个不认识的数字。
        /// </summary>
        public static string DisplayName(this ScadaRole role) => role switch
        {
            ScadaRole.Operator => "操作员",
            ScadaRole.Engineer => "工程师",
            ScadaRole.Administrator => "管理员",
            _ => $"未知角色({(int)role})",
        };

        /// <summary>
        /// 角色高低比较（<b>高角色包含低角色</b>）。
        ///
        /// 为什么是"包含"而不是"精确匹配"：管理员去按一个"工程师按钮"还要先降级，
        /// 是纯添堵——现场没人会接受。这条口径也决定了 <see cref="IScadaAccessPolicy.CanOperate"/>
        /// 只需一次比较，不必维护"角色 → 允许的权限点集合"那张表。
        /// </summary>
        /// <returns>true = <paramref name="current"/> 达到或高于 <paramref name="required"/></returns>
        public static bool Satisfies(this ScadaRole current, ScadaRole required)
            => (int)current >= (int)required;

        /// <summary>
        /// <b>权限判定的唯一一份实现</b>：以 <paramref name="current"/> 的身份，
        /// 能不能操作一个"要求 <paramref name="required"/>"的对象。
        ///
        /// 为什么把规则放在这里、而不是让每个 <see cref="IScadaAccessPolicy"/> 实现各写一遍
        /// ---------
        /// 这条规则要被两个地方用到：领域层"没人挂会话"时的兜底（<see cref="DefaultScadaAccessPolicy"/>），
        /// 以及 WPF 侧带登录状态的那一份实现。两处各写一遍的后果不是"多敲几行"，
        /// 而是<b>两条判定会分头演化</b>——将来给 <see cref="ScadaRole"/> 加一档，
        /// 改了一处忘了另一处，表现是"预览里能按、运行起来不能按"，查起来要人命。
        /// 规则的副本数就是将来 bug 的分布数。
        ///
        /// 三档判定（<paramref name="required"/> 为 <c>null</c> → 非法值 → 正常比较）的顺序是刻意的：
        /// <b>先答"要不要管"，再答"配得对不对"，最后才答"够不够格"</b>。
        /// 反过来先比大小的话，非法值（<see cref="ScadaRole.Undefined"/> 的数值 0）会因为
        /// "0 比谁都小"而被判成"要管理员"，报出来的原因就完全指错了方向。
        /// </summary>
        /// <param name="current">此刻生效的角色（未登录时由实现方传 <see cref="ScadaRole.Operator"/>）</param>
        /// <param name="required">对象上声明的要求角色；<c>null</c> = 不限制</param>
        /// <param name="reason">拒绝时的一句话原因，放行时为 <c>null</c>（口径见 <see cref="IScadaAccessPolicy.CanOperate"/>）</param>
        public static bool Allows(this ScadaRole current, ScadaRole? required, out string? reason)
        {
            // ① 不限制：绝大多数图元走这一条，热路径上最该便宜的一档排最前
            if (required == null)
            {
                reason = null;
                return true;
            }

            var want = required.Value;

            // ② 文件里的要求值不认识（高版本存的、或者被手工改坏）：fail-safe 拒绝。
            //    放行等于把"一个不认识的数字"解释成"谁都能按"，那是静默的权限放大。
            if (!want.IsDefined())
            {
                reason = $"图元要求的权限值非法（{(int)want}），已按安全策略拒绝";
                return false;
            }

            // ③ 当前角色本身非法：同样是配置/接线出错，同样拒绝——但原因要说清是"你这边不对"，
            //    与②的"图元那边不对"是两句话，否则现场照着日志去改图元，越改越乱。
            if (!current.IsDefined())
            {
                reason = $"当前角色非法（{(int)current}），已按安全策略拒绝";
                return false;
            }

            if (current.Satisfies(want))
            {
                reason = null;
                return true;
            }

            // 拒绝原因要能直接念给操作员听，也要能直接写进日志：
            // "需要「工程师」权限，当前是「操作员」"比"权限不足"多出的那两句，正是现场要知道的。
            reason = $"需要「{want.DisplayName()}」权限，当前是「{current.DisplayName()}」";
            return false;
        }
    }
}
