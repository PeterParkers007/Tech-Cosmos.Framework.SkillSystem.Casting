using System;
using TechCosmos.SkillSystem.Runtime;
using UnityEngine;

namespace TechCosmos.SkillSystem.Casting
{
    /// <summary>施法阶段。</summary>
    public enum SkillCastPhase
    {
        /// <summary>空闲。</summary>
        None,
        /// <summary>读条中。</summary>
        Casting,
        /// <summary>预留。引导已改由 <see cref="ChannelMechanism{T}"/>，控制器不再进入。打断事件转发引导时仍可能写这个值。</summary>
        Channeling,
        /// <summary>预留。当前管线同步执行，不会进入此状态。</summary>
        Executing
    }

    /// <summary>施法打断原因。</summary>
    public enum InterruptReason
    {
        /// <summary>手动取消。</summary>
        Manual,
        /// <summary>受到伤害。</summary>
        Damage,
        /// <summary>硬控（眩晕等）。</summary>
        HardCrowdControl,
        /// <summary>移动。</summary>
        Movement,
        /// <summary>沉默。</summary>
        Silence,
        /// <summary>死亡。</summary>
        Death
    }

    /// <summary>打断当时的阶段和已过时间。清会话前抄下来，回调里不要再读控制器上的 Elapsed。</summary>
    public readonly struct CastInterruptInfo
    {
        public readonly InterruptReason Reason;
        public readonly SkillCastPhase Phase;
        /// <summary>打断时所在阶段已过秒数。</summary>
        public readonly float Elapsed;
        /// <summary>前摇已走过的秒数。前摇中打断等于 <see cref="Elapsed"/>。</summary>
        public readonly float CastElapsed;
        /// <summary>本次施法一共走过的秒数（前摇 + 当前引导）。</summary>
        public readonly float TotalElapsed;

        public CastInterruptInfo(
            InterruptReason reason,
            SkillCastPhase phase,
            float elapsed,
            float castElapsed)
        {
            Reason = reason;
            Phase = phase;
            Elapsed = elapsed;
            CastElapsed = castElapsed;
            TotalElapsed = phase == SkillCastPhase.Channeling
                ? castElapsed + elapsed
                : elapsed;
        }
    }

    /// <summary>
    /// 施法控制器：只做释放前的读条和这段读条的打断。
    /// 引导由 <see cref="ChannelMechanism{T}"/> 在管线里启动。
    /// 挂到 <see cref="SkillHolder{T}.Executor"/>；Tick 由项目调用。
    /// </summary>
    public sealed class SkillExecutionController<T> : ISkillExecutor<T> where T : class, IUnit<T>
    {
        private readonly ISkillClock _clock;
        private ActiveCast _activeCast;
        private float _lastCastElapsed;
        private T _occupant;

        /// <summary>当前施法阶段。</summary>
        public SkillCastPhase Phase => _activeCast?.phase ?? SkillCastPhase.None;
        /// <summary>当前正在施放的技能。</summary>
        public ISkill<T> ActiveSkill => _activeCast?.skill;
        /// <summary>读条中，或该施法者身上有进行中的引导。</summary>
        public bool IsBusy => _activeCast != null || ChannelSessionService.IsActive(_occupant);
        /// <summary>当前前摇已过秒数。空闲为 0。</summary>
        public float Elapsed => _activeCast?.elapsed ?? 0f;
        /// <summary>本次开读条时抄下的前摇秒数。空闲为 0。</summary>
        public float CastTime => _activeCast?.castTime ?? 0f;
        /// <summary>当前前摇剩余秒数。空闲为 0。</summary>
        public float Remaining
        {
            get
            {
                if (_activeCast == null) return 0f;
                return Mathf.Max(0f, _activeCast.castTime - _activeCast.elapsed);
            }
        }
        /// <summary>当前前摇进度 0～1。空闲或时长为 0 时为 0。</summary>
        public float Progress
        {
            get
            {
                if (_activeCast == null || _activeCast.castTime <= 0f) return 0f;
                return Mathf.Clamp01(_activeCast.elapsed / _activeCast.castTime);
            }
        }
        /// <summary>当前前摇是否可被外部打断。空闲且无引导为 true。</summary>
        public bool CanBeInterrupted
        {
            get
            {
                if (_activeCast != null)
                    return _activeCast.canCastBeInterrupted;
                if (ChannelSessionService.TryGetOccupancy(_occupant, out var occupancy))
                    return occupancy.CanBeInterrupted;
                return true;
            }
        }
        /// <summary>本次施法开始时刻（时钟 Time）。空闲为 0。</summary>
        public float StartedAt => _activeCast?.startedAt ?? 0f;
        /// <summary>
        /// 上次成功出手时前摇实际走了多久。打断不改。
        /// 管线跑的时候读它，不要读 <see cref="Elapsed"/>。
        /// </summary>
        public float LastCastElapsed => _lastCastElapsed;

        /// <summary>开始读条时触发。没有前摇、直接进管线时不触发。</summary>
        public event Action<ISkill<T>, SkillContext<T>> OnCastStarted;
        /// <summary>读条完成且管线执行成功时触发。引导此时才刚开始。</summary>
        public event Action<ISkill<T>, SkillContext<T>> OnCastCompleted;
        /// <summary>
        /// 读条走完后管线执行失败时触发（蓝耗、条件、中间件取消等）。
        /// 与 <see cref="OnCastInterrupted"/> 不同：读条已正常结束，只是结算失败。
        /// </summary>
        public event Action<ISkill<T>, SkillContext<T>, SkillExecutionResult> OnCastFailed;
        /// <summary>前摇被打断，或正在引导时转发的打断。</summary>
        public event Action<ISkill<T>, CastInterruptInfo> OnCastInterrupted;

        public SkillExecutionController(ISkillClock clock = null)
        {
            _clock = clock ?? SkillSystemServices.Clock;
        }

        /// <summary>
        /// 尝试执行技能：有前摇则先预检再读条，否则立即执行管线。
        /// </summary>
        public bool TryExecute(ISkill<T> skill, SkillContext<T> context)
        {
            if (skill == null) return false;

            if (context.caster != null)
                _occupant = context.caster;

            var incomingPriority = GetExecutionPriority(skill);
            if (IsBusy && !CanInterruptCurrent(incomingPriority))
                return false;

            if (IsBusy)
                TryInterrupt(InterruptReason.Manual);

            float castTime = SkillCastTiming.GetCastTime(skill, context);
            if (castTime > 0f)
            {
                if (!SkillExecutionPipeline.CanExecute(skill, context))
                    return false;

                BeginCast(skill, context, castTime,
                    SkillCastTiming.GetCastCanBeInterrupted(skill, context),
                    incomingPriority);
                return true;
            }

            var result = SkillExecutionPipeline.Execute(skill, context);
            return result == SkillExecutionResult.Success;
        }

        /// <summary>每帧推进前摇进度。</summary>
        public void Tick()
        {
            if (_activeCast == null) return;

            _activeCast.elapsed += _clock.DeltaTime;
            if (_activeCast.elapsed >= _activeCast.castTime)
                CompleteCast();
        }

        /// <summary>尝试打断当前前摇；没有前摇则尝试打断该施法者的引导。</summary>
        public bool TryInterrupt(InterruptReason reason)
        {
            if (_activeCast != null)
            {
                if (!_activeCast.canCastBeInterrupted
                    && reason != InterruptReason.Manual
                    && reason != InterruptReason.Death)
                    return false;

                var info = new CastInterruptInfo(
                    reason, SkillCastPhase.Casting, _activeCast.elapsed, _activeCast.elapsed);
                var skill = _activeCast.skill;
                _activeCast = null;
                OnCastInterrupted?.Invoke(skill, info);
                return true;
            }

            if (!ChannelSessionService.TryGetOccupancy(_occupant, out var occupancy))
                return false;

            bool force = reason == InterruptReason.Manual || reason == InterruptReason.Death;
            if (!force && !occupancy.CanBeInterrupted)
                return false;

            float channelElapsed = occupancy.Elapsed;
            if (!ChannelSessionService.TryInterrupt(_occupant, force))
                return false;

            var channelInfo = new CastInterruptInfo(
                reason, SkillCastPhase.Channeling, channelElapsed, _lastCastElapsed);
            OnCastInterrupted?.Invoke(null, channelInfo);
            return true;
        }

        /// <summary>手动取消当前前摇或引导。</summary>
        public void Cancel() => TryInterrupt(InterruptReason.Manual);

        void BeginCast(
            ISkill<T> skill,
            SkillContext<T> context,
            float castTime,
            bool canCastBeInterrupted,
            int executionPriority)
        {
            _activeCast = new ActiveCast
            {
                skill = skill,
                context = context,
                castTime = castTime,
                canCastBeInterrupted = canCastBeInterrupted,
                executionPriority = executionPriority,
                phase = SkillCastPhase.Casting,
                startedAt = _clock.Time
            };
            OnCastStarted?.Invoke(skill, context);
        }

        void CompleteCast()
        {
            if (_activeCast == null) return;

            _lastCastElapsed = _activeCast.elapsed;
            var cast = _activeCast;
            _activeCast = null;
            var result = SkillExecutionPipeline.Execute(cast.skill, cast.context);
            if (result == SkillExecutionResult.Success)
                OnCastCompleted?.Invoke(cast.skill, cast.context);
            else
                OnCastFailed?.Invoke(cast.skill, cast.context, result);
        }

        bool CanInterruptCurrent(int incomingPriority)
        {
            if (_activeCast != null)
            {
                if (!_activeCast.canCastBeInterrupted) return false;
                return incomingPriority > _activeCast.executionPriority;
            }

            if (!ChannelSessionService.TryGetOccupancy(_occupant, out var occupancy))
                return true;
            if (!occupancy.CanBeInterrupted) return false;
            return incomingPriority > occupancy.ExecutionPriority;
        }

        static int GetExecutionPriority(ISkill<T> skill)
            => skill is Skill<T> concrete ? concrete.Profile.executionPriority : 0;

        sealed class ActiveCast
        {
            public ISkill<T> skill;
            public SkillContext<T> context;
            public float castTime;
            public bool canCastBeInterrupted;
            public int executionPriority;
            public SkillCastPhase phase;
            public float elapsed;
            public float startedAt;
        }
    }
}
