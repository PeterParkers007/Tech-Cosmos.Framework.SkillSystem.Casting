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
        public const string CastCanBeInterruptedKey = "SkillCastCanBeInterrupted";
        public const string ChannelCanBeInterruptedKey = "SkillChannelCanBeInterrupted";
        /// <summary>旧键。新键缺失时两阶段都回退到它。</summary>
        public const string CanBeInterruptedKey = "SkillCanBeInterrupted";

        public static float GetCastTime<T>(ISkill<T> skill, SkillContext<T> context)
            where T : class, IUnit<T>
            => GetFloat(skill, CastTimeKey, context, 0f);

        public static float GetChannelTime<T>(ISkill<T> skill, SkillContext<T> context)
            where T : class, IUnit<T>
            => GetFloat(skill, ChannelTimeKey, context, 0f);

        /// <summary>前摇可否被外部打断。缺新键则回退旧键，再缺视为可打断。</summary>
        public static bool GetCastCanBeInterrupted<T>(ISkill<T> skill, SkillContext<T> context)
            where T : class, IUnit<T>
            => GetInterruptedFlag(skill, context, CastCanBeInterruptedKey);

        /// <summary>引导可否被外部打断。缺新键则回退旧键，再缺视为可打断。</summary>
        public static bool GetChannelCanBeInterrupted<T>(ISkill<T> skill, SkillContext<T> context)
            where T : class, IUnit<T>
            => GetInterruptedFlag(skill, context, ChannelCanBeInterruptedKey);

        /// <summary>旧接口：只读旧键。新代码请用分阶段读取。</summary>
        public static bool GetCanBeInterrupted<T>(ISkill<T> skill, SkillContext<T> context)
            where T : class, IUnit<T>
        {
            if (skill?.DataLayer == null)
                return true;
            if (!skill.DataLayer.TryGetValue(CanBeInterruptedKey, context, out bool value))
                return true;
            return value;
        }

        static bool GetInterruptedFlag<T>(ISkill<T> skill, SkillContext<T> context, string key)
            where T : class, IUnit<T>
        {
            if (skill?.DataLayer == null)
                return true;
            if (skill.DataLayer.TryGetValue(key, context, out bool value))
                return value;
            if (skill.DataLayer.TryGetValue(CanBeInterruptedKey, context, out bool legacy))
                return legacy;
            return true;
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
