using System;
using System.Collections.Generic;
using TechCosmos.SkillSystem.Runtime;
using UnityEngine;

namespace TechCosmos.SkillSystem.Casting
{
    /// <summary>
    /// 引导机制：出手后启动持续态。
    /// 开始 / 成功 / 失败挂 Mechanism 或 EffectCore；持续阶段是一份 Buff。
    /// </summary>
    [Serializable]
    [MechanismMenu("⏳ 引导", DisplayName = "引导", Priority = 6)]
    [RequiredData("ChannelDuration", typeof(float), IsFormula = true, StaticValue = 0f,
        Description = "引导持续（秒），施加 Buff 时覆盖其 duration")]
    [RequiredData("ChannelCanBeInterrupted", typeof(bool), DefaultValue = "true",
        Description = "引导是否可被外部打断")]
    [RequiredData("ChannelBuffId", typeof(string), DefaultValue = "Channel",
        Description = "引导 Buff 名")]
    public class ChannelMechanism<T> : Mechanism<T>, INestedEffectEntryOwner where T : class, IUnit<T>
    {
        [SerializeReference]
        [NestedEffectList("开始时")]
        [Tooltip("引导开始时（打上 Buff 之前）")]
        public List<GameplayEffectEntryBase> onStart = new();

        [Tooltip("引导时：持续、间隔、标签、进出清理")]
        public BuffDataSO channelBuff;

        [Tooltip("技能数值层 → 引导 Buff 数值层，只影响本次实例")]
        public List<SkillToBuffDataBinding> buffDataBindings = new();

        [SerializeReference]
        [NestedEffectList("成功时")]
        [Tooltip("引导走完（Buff 到期）")]
        public List<GameplayEffectEntryBase> onSuccess = new();

        [SerializeReference]
        [NestedEffectList("失败时")]
        [Tooltip("引导失败（打断 / 驱散 / 卸技能）")]
        public List<GameplayEffectEntryBase> onFail = new();

        [Tooltip("没有 RequiredData 或读到 0 时的回退时长")]
        public float duration;

        [Tooltip("没有 RequiredData 时的回退")]
        public bool canBeInterrupted = true;

        [Tooltip("没有 RequiredData 时的回退 Buff 名")]
        public string buffId = "Channel";

        [Tooltip("引导 Buff 打在施法者上。取消则打在目标上")]
        public bool applyToCaster = true;

        public ModifierStackPolicy stackPolicy = ModifierStackPolicy.Replace;
        public int maxStacks = 1;

        public IEnumerable<GameplayEffectEntryBase> EnumerateNestedEntries()
        {
            if (onStart != null)
            {
                for (int i = 0; i < onStart.Count; i++)
                {
                    if (onStart[i] != null)
                        yield return onStart[i];
                }
            }

            if (onSuccess != null)
            {
                for (int i = 0; i < onSuccess.Count; i++)
                {
                    if (onSuccess[i] != null)
                        yield return onSuccess[i];
                }
            }

            if (onFail != null)
            {
                for (int i = 0; i < onFail.Count; i++)
                {
                    if (onFail[i] != null)
                        yield return onFail[i];
                }
            }
        }

        public override void Execute(SkillContext<T> context, IDataLayer<T> dataLayer)
        {
            RunHooks(onStart, context, dataLayer);

            var hostUnit = applyToCaster
                ? context.caster
                : context.target ?? context.caster;
            if (hostUnit == null)
            {
                SkillLog.Warning("[ChannelMechanism] 没有宿主，跳过引导。");
                return;
            }

            float channelDuration = dataLayer != null
                ? GetRequired("ChannelDuration", 0f)
                : 0f;
            if (channelDuration <= 0f)
                channelDuration = duration;

            bool interruptible = dataLayer != null
                ? GetRequired("ChannelCanBeInterrupted", canBeInterrupted)
                : canBeInterrupted;

            ChannelSessionService.TryInterrupt(hostUnit, true);

            if (channelDuration <= 0f)
            {
                RunHooks(onSuccess, context, dataLayer);
                ChannelSessionService.RememberElapsed(hostUnit, 0f);
                return;
            }

            if (hostUnit is not IBuffHost<T> host)
            {
                SkillLog.Warning("[ChannelMechanism] 宿主不是 IBuffHost，无法维持引导，按成功结束。");
                RunHooks(onSuccess, context, dataLayer);
                return;
            }

            var id = dataLayer != null
                ? GetRequired("ChannelBuffId", (string)null)
                : null;
            if (string.IsNullOrEmpty(id))
                id = string.IsNullOrEmpty(buffId) ? "Channel" : buffId;

            IBuff<T> buff;
            if (channelBuff != null)
            {
                var configurable = new ConfigurableBuff<T>(hostUnit, channelBuff, context.caster)
                    .SetBuffName(string.IsNullOrEmpty(id) ? channelBuff.buffName : id)
                    .WithDuration(channelDuration)
                    .SetMaxStacks(maxStacks > 0 ? maxStacks : channelBuff.maxStacks)
                    .SetStackPolicy(BuffStackPolicyMapper.ToGbfPolicy(stackPolicy));
                SkillToBuffDataBindingApplier.ApplyAll(configurable, buffDataBindings, dataLayer, context);
                buff = configurable;
            }
            else
            {
                buff = new SimpleBuff<T>(
                    hostUnit,
                    id,
                    channelDuration,
                    tags: new[] { "Channeling" },
                    modifiers: null,
                    context.caster,
                    BuffStackPolicyMapper.ToGbfPolicy(stackPolicy),
                    maxStacks > 0 ? maxStacks : 1);
            }

            int priority = context.skill is Skill<T> concrete
                ? concrete.Profile.executionPriority
                : 0;

            var session = new ChannelSession(
                hostUnit,
                host,
                buff,
                context,
                dataLayer,
                onSuccess,
                onFail,
                interruptible,
                priority);
            session.Begin();
        }

        public override void SkillBack(ISkill<T> skill, T owner = null)
        {
            if (skill != null && ChannelSessionService.TryInterruptSkill(skill, true))
                return;
            if (owner != null)
                ChannelSessionService.TryInterrupt(owner, true);
        }

        internal static void RunHooks(
            List<GameplayEffectEntryBase> hooks,
            SkillContext<T> context,
            IDataLayer<T> dataLayer)
        {
            if (hooks == null || hooks.Count == 0)
                return;

            SkillEffectScope.Run(dataLayer, context, () =>
            {
                for (int i = 0; i < hooks.Count; i++)
                    EffectCoreExecutor.ExecuteSkillEntry(hooks[i], context, dataLayer);
            });
        }

        sealed class ChannelSession : IChannelSession
        {
            readonly T _owner;
            readonly IBuffHost<T> _host;
            readonly IBuff<T> _buff;
            readonly SkillContext<T> _context;
            readonly IDataLayer<T> _dataLayer;
            readonly List<GameplayEffectEntryBase> _onSuccess;
            readonly List<GameplayEffectEntryBase> _onFail;
            bool _resolved;

            public object Owner => _owner;
            public object SkillKey => _context.skill;
            public bool CanBeInterrupted { get; }
            public int ExecutionPriority { get; }
            public float Elapsed => _buff is BaseBuff<T> baseBuff ? baseBuff.ElapsedTime : 0f;

            public ChannelSession(
                T owner,
                IBuffHost<T> host,
                IBuff<T> buff,
                SkillContext<T> context,
                IDataLayer<T> dataLayer,
                List<GameplayEffectEntryBase> onSuccess,
                List<GameplayEffectEntryBase> onFail,
                bool canBeInterrupted,
                int executionPriority)
            {
                _owner = owner;
                _host = host;
                _buff = buff;
                _context = context;
                _dataLayer = dataLayer;
                _onSuccess = onSuccess;
                _onFail = onFail;
                CanBeInterrupted = canBeInterrupted;
                ExecutionPriority = executionPriority;
            }

            public void Begin()
            {
                _buff.OnRemove += OnBuffRemoved;
                ChannelSessionService.Register(this);
                _host.BuffSystem.AddBuff(_buff);
            }

            public bool TryInterrupt(bool force)
            {
                if (_resolved)
                    return false;
                if (!force && !CanBeInterrupted)
                    return false;

                _host.BuffSystem.RemoveBuff(_buff);
                return _resolved;
            }

            void OnBuffRemoved(T _)
            {
                if (_resolved)
                    return;

                bool success = _buff.isOver;
                Complete(success);
            }

            void Complete(bool success)
            {
                if (_resolved)
                    return;
                _resolved = true;
                _buff.OnRemove -= OnBuffRemoved;
                ChannelSessionService.Unregister(this, Elapsed);
                RunHooks(success ? _onSuccess : _onFail, _context, _dataLayer);
            }
        }
    }
}
