using UnityEngine;
using TechCosmos.SkillSystem.Runtime;

namespace TechCosmos.SkillSystem.Casting
{
    /// <summary>
    /// 前摇 / 引导 / 可打断：中间件灌进数值层的键名，以及从 DataLayer 读取的约定。
    /// </summary>
    public static class SkillCastTiming
    {
        public const string CastTimeKey = "SkillCastTime";
        public const string ChannelTimeKey = "SkillChannelTime";
        public const string CanBeInterruptedKey = "SkillCanBeInterrupted";

        public static float GetCastTime<T>(ISkill<T> skill, SkillContext<T> context)
            where T : class, IUnit<T>
            => GetFloat(skill, CastTimeKey, context, 0f);

        public static float GetChannelTime<T>(ISkill<T> skill, SkillContext<T> context)
            where T : class, IUnit<T>
            => GetFloat(skill, ChannelTimeKey, context, 0f);

        /// <summary>缺键时视为可打断。</summary>
        public static bool GetCanBeInterrupted<T>(ISkill<T> skill, SkillContext<T> context)
            where T : class, IUnit<T>
        {
            if (skill?.DataLayer == null)
                return true;
            if (!skill.DataLayer.TryGetValue(CanBeInterruptedKey, context, out bool value))
                return true;
            return value;
        }

        static float GetFloat<T>(ISkill<T> skill, string key, SkillContext<T> context, float fallback)
            where T : class, IUnit<T>
        {
            if (skill?.DataLayer == null)
                return fallback;
            if (!skill.DataLayer.TryGetValue(key, context, out float value))
                return fallback;
            return Mathf.Max(0f, value);
        }
    }
}
