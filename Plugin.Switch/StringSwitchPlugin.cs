using Core.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Plugin.Switch
{
    [Display(Name = "Switch", GroupName = "逻辑控制", ShortName = "\uf074")]
    public class StringSwitchPlugin : BranchPluginBase
    {

        // 输入端口：接收上游识别到的条码、字符或料号
        public InputPort<string> TargetValueInput { get; } = new InputPort<string>("TargetValue");

        public override void Dispose()
        {
            
        }

        public override void Initialize()
        {
            TargetValueInput.Value = string.Empty;
        }

        public override string SelectBranchKey()
        {
            string value = TargetValueInput.GetTypedValue();

            if (string.IsNullOrEmpty(value))
            {
                return "Default"; // 容错兜底
            }

            // 假设当前字典里有这个 Key 对应的分支，就走这个分支
            // 比如 value 是 "Product_A"，如果 Branches 字典里恰好有 "Product_A" 这个集合，就会切过去
            if (Branches.ContainsKey(value))
            {
                return value;
            }

            // 没匹配上，统一走 Default
            return "Default";
        }
    }
}
