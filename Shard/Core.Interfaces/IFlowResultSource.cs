namespace Core.Interfaces
{
    /// <summary>
    /// 流程判定来源：由「结果输出」步骤实现，宿主在每轮结束时读取。
    ///
    /// 【为什么单独一个接口，而不是让宿主按插件类名匹配】
    /// 宿主（VisionMaster）在编译期不引用插件工程，插件是运行时从 Modules 反射加载的；
    /// 按类名字符串匹配一旦插件改名/换命名空间就静默失联。接口放在 Core.Interfaces，
    /// 插件与宿主都引用它，宿主只判 `plugin is IFlowResultSource`，不认具体类型。
    ///
    /// 【语义边界】
    /// - 这里只表达"本轮业务判定"，不表达技术成败；步骤本身应始终 Success。
    /// - 产品 NG（判定表 NG、卡尺超差）不是技术故障，不能靠步骤 State=Failed 反推。
    /// - 未接线/未执行时，宿主应记"未判定"，不得沿用上一轮。
    /// </summary>
    public interface IFlowResultSource
    {
        /// <summary>本轮业务判定：true = OK，false = NG。</summary>
        bool IsOk { get; }

        /// <summary>判定原因/摘要（可为空）。</summary>
        string Reason { get; }
    }
}
