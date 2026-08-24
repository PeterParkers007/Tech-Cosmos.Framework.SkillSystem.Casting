using System;
using TechCosmos.SkillSystem.Runtime;

namespace TechCosmos.SkillSystem.Casting
{
    /// <summary>
    /// 把前摇 / 引导 / 可打断灌进技能数值层。
    /// 项目侧用 <c>[AutoGenerateMiddleware(typeof(YourUnit))]</c> 包一层封闭到具体 IUnit。
    /// </summary>
    [Serializable]
    [RequiredData("SkillCastTime", typeof(float), Shared = true, IsFormula = true, StaticValue = 0f,
        Description = "前摇 / 施法时间（秒）")]
    [RequiredData("SkillChannelTime", typeof(float), Shared = true, IsFormula = true, StaticValue = 0f,
        Description = "引导 / 持续施法时间（秒）")]
    [RequiredData("SkillCanBeInterrupted", typeof(bool), Shared = true, DefaultValue = "true",
        Description = "读条/引导是否可被外部打断")]
    public class SkillCastMiddleware<T> : Middleware<T> where T : class, IUnit<T>
    {
    }
}
