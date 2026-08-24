using System;
using TechCosmos.SkillSystem.Runtime;

namespace TechCosmos.SkillSystem.Casting
{
    /// <summary>施法阶段。</summary>
    public enum SkillCastPhase
    {
        /// <summary>空闲。</summary>
        None,
        /// <summary>读条中。</summary>
        Casting,
        /// <summary>引导中。</summary>
        Channeling,
        /// <summary>执行中。</summary>
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

    /// <summary>
    /// 施法控制器：读条、引导、打断，结束后走执行管线。
    /// 挂到 <see cref="SkillHolder{T}.Executor"/>；Tick 由项目调用。
    /// </summary>
    public sealed class SkillExecutionController<T> : ISkillExecutor<T> where T : class, IUnit<T>
    {
        private readonly ISkillClock _clock;
        private ActiveCast _activeCast;

        /// <summary>当前施法阶段。</summary>
        public SkillCastPhase Phase => _activeCast?.phase ?? SkillCastPhase.None;
        /// <summary>当前正在施放的技能。</summary>
        public ISkill<T> ActiveSkill => _activeCast?.skill;
        /// <summary>是否正在读条或引导。</summary>
        public bool IsBusy => _activeCast != null;

        /// <summary>开始读条/引导时触发。</summary>
        public event Action<ISkill<T>, SkillContext<T>> OnCastStarted;
        /// <summary>读条/引导完成且管线执行成功时触发。</summary>
        public event Action<ISkill<T>, SkillContext<T>> OnCastCompleted;
        /// <summary>
        /// 读条/引导走完后管线执行失败时触发（蓝耗、条件、中间件取消等）。
        /// 与 <see cref="OnCastInterrupted"/> 不同：读条已正常结束，只是结算失败。
        /// </summary>
        public event Action<ISkill<T>, SkillContext<T>, SkillExecutionResult> OnCastFailed;
        /// <summary>施法被打断时触发。</summary>
        public event Action<ISkill<T>, InterruptReason> OnCastInterrupted;

        public SkillExecutionController(ISkillClock clock = null)
        {
            _clock = clock ?? SkillSystemServices.Clock;
        }

        /// <summary>
        /// 尝试执行技能：有读条/引导则先预检再进入施法，否则立即执行。
        /// </summary>
        public bool TryExecute(ISkill<T> skill, SkillContext<T> context)
        {
            if (skill == null) return false;

            var incomingPriority = GetExecutionPriority(skill);
            if (IsBusy && !CanInterruptCurrent(incomingPriority))
                return false;

            float castTime = SkillCastTiming.GetCastTime(skill, context);
            float channelTime = SkillCastTiming.GetChannelTime(skill, context);
            if (castTime > 0f || channelTime > 0f)
            {
                if (!SkillExecutionPipeline.CanExecute(skill, context))
                    return false;

                BeginCast(skill, context, castTime, channelTime,
                    SkillCastTiming.GetCanBeInterrupted(skill, context), incomingPriority);
                return true;
            }

            var result = SkillExecutionPipeline.Execute(skill, context);
            return result == SkillExecutionResult.Success;
        }

        /// <summary>每帧推进读条/引导进度。</summary>
        public void Tick()
        {
            if (_activeCast == null) return;

            _activeCast.elapsed += _clock.DeltaTime;

            switch (_activeCast.phase)
            {
                case SkillCastPhase.Casting:
                    if (_activeCast.elapsed >= _activeCast.castTime)
                    {
                        if (_activeCast.channelTime > 0f)
                        {
                            _activeCast.phase = SkillCastPhase.Channeling;
                            _activeCast.elapsed = 0f;
                        }
                        else
                        {
                            CompleteCast();
                        }
                    }
                    break;

                case SkillCastPhase.Channeling:
                    if (_activeCast.elapsed >= _activeCast.channelTime)
                        CompleteCast();
                    break;
            }
        }

        /// <summary>尝试打断当前施法。</summary>
        public bool TryInterrupt(InterruptReason reason)
        {
            if (_activeCast == null) return false;
            if (!_activeCast.canBeInterrupted && reason != InterruptReason.Manual && reason != InterruptReason.Death)
                return false;

            var skill = _activeCast.skill;
            _activeCast = null;
            OnCastInterrupted?.Invoke(skill, reason);
            return true;
        }

        /// <summary>手动取消当前施法。</summary>
        public void Cancel() => TryInterrupt(InterruptReason.Manual);

        void BeginCast(
            ISkill<T> skill,
            SkillContext<T> context,
            float castTime,
            float channelTime,
            bool canBeInterrupted,
            int executionPriority)
        {
            if (_activeCast != null)
                TryInterrupt(InterruptReason.Manual);

            _activeCast = new ActiveCast
            {
                skill = skill,
                context = context,
                castTime = castTime,
                channelTime = channelTime,
                canBeInterrupted = canBeInterrupted,
                executionPriority = executionPriority,
                phase = SkillCastPhase.Casting,
                startedAt = _clock.Time
            };

            OnCastStarted?.Invoke(skill, context);
        }

        void CompleteCast()
        {
            if (_activeCast == null) return;

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
            if (_activeCast == null) return true;
            if (!_activeCast.canBeInterrupted) return false;
            return incomingPriority > _activeCast.executionPriority;
        }

        static int GetExecutionPriority(ISkill<T> skill)
            => skill is Skill<T> concrete ? concrete.Profile.executionPriority : 0;

        sealed class ActiveCast
        {
            public ISkill<T> skill;
            public SkillContext<T> context;
            public float castTime;
            public float channelTime;
            public bool canBeInterrupted;
            public int executionPriority;
            public SkillCastPhase phase;
            public float elapsed;
            public float startedAt;
        }
    }
}
