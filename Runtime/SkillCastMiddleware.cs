using System;
using TechCosmos.SkillSystem.Runtime;

namespace TechCosmos.SkillSystem.Casting
{
    /// <summary>
    /// 把前摇 / 前摇可打断灌进技能数值层。
    /// 引导改由 <see cref="ChannelMechanism{T}"/> 声明。
    /// 封闭类由本包菜单「生成 Casting 封闭类」弹出 IUnit 列表生成，不走核心 Generate All。
    /// </summary>
    [Serializable]
    [RequiredData("SkillCastTime", typeof(float), Shared = true, IsFormula = true, StaticValue = 0f,
        Description = "前摇 / 施法时间（秒）")]
    [RequiredData("SkillCastCanBeInterrupted", typeof(bool), Shared = true, DefaultValue = "true",
        Description = "前摇是否可被外部打断", SeedFromKey = SkillCastTiming.CanBeInterruptedKey)]
    public class SkillCastMiddleware<T> : Middleware<T> where T : class, IUnit<T>
    {
    }
}
