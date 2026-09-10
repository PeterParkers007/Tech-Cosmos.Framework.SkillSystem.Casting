using System.Collections.Generic;
using TechCosmos.SkillSystem.Runtime;

namespace TechCosmos.SkillSystem.Casting
{
    /// <summary>某单位当前引导占用。</summary>
    public readonly struct ChannelOccupancy
    {
        public readonly bool CanBeInterrupted;
        public readonly int ExecutionPriority;
        public readonly float Elapsed;

        public ChannelOccupancy(bool canBeInterrupted, int executionPriority, float elapsed)
        {
            CanBeInterrupted = canBeInterrupted;
            ExecutionPriority = executionPriority;
            Elapsed = elapsed;
        }
    }

    /// <summary>
    /// 进行中的引导会话。寿命跟引导 Buff：到期成功，被摘掉失败。
    /// </summary>
    public static class ChannelSessionService
    {
        static readonly Dictionary<object, IChannelSession> Sessions = new();
        static readonly Dictionary<object, float> LastElapsedByOwner = new();

        public static bool IsActive(object owner)
            => owner != null && Sessions.ContainsKey(owner);

        public static float GetElapsed(object owner)
            => owner != null && Sessions.TryGetValue(owner, out var session)
                ? session.Elapsed
                : 0f;

        public static float GetLastElapsed(object owner)
            => owner != null && LastElapsedByOwner.TryGetValue(owner, out var elapsed)
                ? elapsed
                : 0f;

        public static bool TryGetOccupancy(object owner, out ChannelOccupancy occupancy)
        {
            if (owner != null && Sessions.TryGetValue(owner, out var session))
            {
                occupancy = new ChannelOccupancy(
                    session.CanBeInterrupted,
                    session.ExecutionPriority,
                    session.Elapsed);
                return true;
            }

            occupancy = default;
            return false;
        }

        /// <summary>打断当前引导。force 为 true 时无视可打断（手动取消、死亡、卸技能）。</summary>
        public static bool TryInterrupt(object owner, bool force)
        {
            if (owner == null || !Sessions.TryGetValue(owner, out var session))
                return false;
            return session.TryInterrupt(force);
        }

        public static bool TryInterruptSkill(object skill, bool force)
        {
            if (skill == null)
                return false;

            foreach (var session in Sessions.Values)
            {
                if (!ReferenceEquals(session.SkillKey, skill))
                    continue;
                return session.TryInterrupt(force);
            }

            return false;
        }

        public static void ClearAll()
        {
            Sessions.Clear();
            LastElapsedByOwner.Clear();
        }

        internal static void RememberElapsed(object owner, float elapsed)
        {
            if (owner == null)
                return;
            LastElapsedByOwner[owner] = elapsed;
        }

        internal static void Register(IChannelSession session)
        {
            if (session?.Owner == null)
                return;

            if (Sessions.TryGetValue(session.Owner, out var existing)
                && !ReferenceEquals(existing, session))
                existing.TryInterrupt(true);

            Sessions[session.Owner] = session;
        }

        internal static void Unregister(IChannelSession session, float elapsed)
        {
            if (session?.Owner == null)
                return;

            if (Sessions.TryGetValue(session.Owner, out var current)
                && ReferenceEquals(current, session))
                Sessions.Remove(session.Owner);

            LastElapsedByOwner[session.Owner] = elapsed;
        }
    }

    interface IChannelSession
    {
        object Owner { get; }
        object SkillKey { get; }
        bool CanBeInterrupted { get; }
        int ExecutionPriority { get; }
        float Elapsed { get; }
        bool TryInterrupt(bool force);
    }
}
