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
        private float _lastCastElapsed;
        private float _lastChannelElapsed;
        /// <summary>当前施法阶段。</summary>
        public SkillCastPhase Phase => _activeCast?.phase ?? SkillCastPhase.None;
        /// <summary>当前正在施放的技能。</summary>
        public ISkill<T> ActiveSkill => _activeCast?.skill;
        /// <summary>是否正在读条或引导。</summary>
        public bool IsBusy => _activeCast != null;
        /// <summary>当前阶段已过秒数。切引导时清零。空闲为 0。</summary>
        public float Elapsed => _activeCast?.elapsed ?? 0f;
        /// <summary>本次开读条时抄下的前摇秒数。空闲为 0。</summary>
        public float CastTime => _activeCast?.castTime ?? 0f;
        /// <summary>本次开读条时抄下的引导秒数。空闲为 0。</summary>
        public float ChannelTime => _activeCast?.channelTime ?? 0f;
        /// <summary>当前阶段剩余秒数。空闲为 0。</summary>
        public float Remaining
        {
            get
            {
                if (_activeCast == null) return 0f;
                return Mathf.Max(0f, CurrentPhaseDuration - _activeCast.elapsed);
            }
        }
        /// <summary>当前阶段进度 0～1。空闲或时长为 0 时为 0。切引导后从 0 再走。</summary>
        public float Progress
        {
            get
            {
                float duration = CurrentPhaseDuration;
                if (duration <= 0f) return 0f;
                return Mathf.Clamp01(_activeCast.elapsed / duration);
            }
        }
        /// <summary>本次施法是否可被外部打断。空闲为 true。</summary>
        public bool CanBeInterrupted => _activeCast?.canBeInterrupted ?? true;
        /// <summary>本次施法开始时刻（时钟 Time）。空闲为 0。</summary>
        public float StartedAt => _activeCast?.startedAt ?? 0f;
        /// <summary>
        /// 上次成功出手时前摇实际走了多久。打断不改。
        /// 管线跑的时候读它，不要读 <see cref="Elapsed"/>。
        /// </summary>
        public float LastCastElapsed => _lastCastElapsed;
        /// <summary>
        /// 上次成功出手时引导实际走了多久。只有前摇则为 0。打断不改。
        /// 管线跑的时候读它，不要读 <see cref="Elapsed"/>。
        /// </summary>
        public float LastChannelElapsed => _lastChannelElapsed;
        float CurrentPhaseDuration
        {
            get
            {
                if (_activeCast == null) return 0f;
                return _activeCast.phase switch
                {
                    SkillCastPhase.Casting => _activeCast.castTime,
                    SkillCastPhase.Channeling => _activeCast.channelTime,
                    _ => 0f
                };
            }
        }

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
                            _activeCast.committedCastElapsed = _activeCast.elapsed;
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

        /// <summary>
        /// 提前结束当前引导并结算。不是打断。
        /// 只在引导阶段成功。前摇是硬门槛，前摇中或空闲返回 false。
        /// 不看 <see cref="CanBeInterrupted"/>。
        /// </summary>
        public bool TryRelease()
        {
            if (_activeCast == null || _activeCast.phase != SkillCastPhase.Channeling)
                return false;

            CompleteCast();
            return true;
        }

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
                phase = castTime > 0f ? SkillCastPhase.Casting : SkillCastPhase.Channeling,
                startedAt = _clock.Time
            };
            OnCastStarted?.Invoke(skill, context);
        }

        void CompleteCast()
        {
            if (_activeCast == null) return;

            if (_activeCast.phase == SkillCastPhase.Channeling)
            {
                _lastCastElapsed = _activeCast.committedCastElapsed;
                _lastChannelElapsed = _activeCast.elapsed;
            }
            else
            {
                _lastCastElapsed = _activeCast.elapsed;
                _lastChannelElapsed = 0f;
            }

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
            public float committedCastElapsed;
        }
    }
}
