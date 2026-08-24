using System;
using TechCosmos.SkillSystem.Runtime;

namespace TechCosmos.SkillSystem.Casting
{
    /// <summary>
    /// 把前摇 / 引导 / 可打断灌进技能数值层。
    /// 封闭类由本包菜单「Generate Cast Middleware」弹出 IUnit 列表生成，不走核心 Generate All。
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
