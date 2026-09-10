using System.Collections.Generic;
using NUnit.Framework;
using TechCosmos.SkillSystem.Runtime;

namespace TechCosmos.SkillSystem.Casting.Tests
{
    /// <summary>施法控制器：前摇 / 打断。引导见 ChannelMechanism。</summary>
    public class SkillExecutionControllerTests
    {
        sealed class MockUnit : IUnit<MockUnit>
        {
            public string[] GetSupportedEvents() => new[] { "OnAttack" };
            public void TriggerEvent(string eventName, SkillContext<MockUnit> context) { }
            public void AddSkill(ISkill<MockUnit> skill) { }
            public void RemoveSkill(ISkill<MockUnit> skill) { }
            public bool TryCast(SkillId skillId, SkillContext<MockUnit> context) => false;
            public bool TryCast(ISkill<MockUnit> skill, SkillContext<MockUnit> context) => false;
        }

        sealed class HostUnit : IUnit<HostUnit>, IBuffHost<HostUnit>
        {
            public BuffSystem<HostUnit> BuffSystem { get; }
            public TagContainer Tags { get; } = new TagContainer();

            public HostUnit()
            {
                BuffSystem = new BuffSystem<HostUnit>(this);
            }

            public string[] GetSupportedEvents() => new[] { "OnAttack" };
            public void TriggerEvent(string eventName, SkillContext<HostUnit> context) { }
            public void AddSkill(ISkill<HostUnit> skill) { }
            public void RemoveSkill(ISkill<HostUnit> skill) { }
            public bool TryCast(SkillId skillId, SkillContext<HostUnit> context) => false;
            public bool TryCast(ISkill<HostUnit> skill, SkillContext<HostUnit> context) => false;
        }

        static ISkill<MockUnit> CreateSkill(
            float castTime,
            bool canBeInterrupted = true,
            List<Condition<MockUnit>> conditions = null,
            int executionPriority = 0,
            string skillName = "TestSkill")
        {
            var data = new SkillData<MockUnit>
            {
                SkillId = "test.test_skill",
                SkillName = skillName,
                SkillType = SkillType.Active,
                TriggerEvents = new List<string> { "OnAttack" },
                Profile = new SkillProfile { executionPriority = executionPriority },
                Conditions = conditions ?? new List<Condition<MockUnit>>()
            };
            data.SetValue(SkillCastTiming.CastTimeKey, castTime);
            data.SetValue(SkillCastTiming.CastCanBeInterruptedKey, canBeInterrupted);
            return SkillFactory<MockUnit>.CreateSkill(data);
        }

        [SetUp]
        public void SetUp()
        {
            SkillSystemServices.Clock = new FixedSkillClock(0f, 0.5f);
            ChannelSessionService.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            SkillSystemServices.Clock = new UnitySkillClock();
            ChannelSessionService.ClearAll();
        }

        [Test]
        public void CompleteCast_FiresOnCastCompletedWhenPipelineSucceeds()
        {
            var skill = CreateSkill(1f);
            var controller = new SkillExecutionController<MockUnit>();
            int completed = 0;
            controller.OnCastCompleted += (_, __) => completed++;

            Assert.IsTrue(controller.TryExecute(skill, new SkillContext<MockUnit>(new MockUnit())));
            controller.Tick();
            controller.Tick();

            Assert.AreEqual(1, completed);
        }

        [Test]
        public void CompleteCast_SkipsOnCastCompletedWhenPipelineFails()
        {
            bool eligible = true;
            var skill = CreateSkill(
                1f,
                canBeInterrupted: true,
                conditions: new List<Condition<MockUnit>>
                {
                    new FuncCondition<MockUnit>(_ => eligible)
                });
            var controller = new SkillExecutionController<MockUnit>();
            int completed = 0;
            int failed = 0;
            controller.OnCastCompleted += (_, __) => completed++;
            controller.OnCastFailed += (_, __, ___) => failed++;

            Assert.IsTrue(controller.TryExecute(skill, new SkillContext<MockUnit>(new MockUnit())));
            eligible = false;
            controller.Tick();
            controller.Tick();

            Assert.AreEqual(0, completed);
            Assert.AreEqual(1, failed);
        }

        [Test]
        public void Interrupt_DoesNotFireOnCastCompleted()
        {
            var skill = CreateSkill(2f, canBeInterrupted: true);
            var controller = new SkillExecutionController<MockUnit>();
            int completed = 0;
            int interrupted = 0;
            controller.OnCastCompleted += (_, __) => completed++;
            controller.OnCastInterrupted += (_, __) => interrupted++;

            Assert.IsTrue(controller.TryExecute(skill, new SkillContext<MockUnit>(new MockUnit())));
            Assert.IsTrue(controller.TryInterrupt(InterruptReason.Manual));

            Assert.AreEqual(0, completed);
            Assert.AreEqual(1, interrupted);
        }

        [Test]
        public void InstantSkill_ExecutesWithoutEnteringCasting()
        {
            var skill = CreateSkill(0f);
            var controller = new SkillExecutionController<MockUnit>();
            int started = 0;
            controller.OnCastStarted += (_, __) => started++;

            Assert.IsTrue(controller.TryExecute(skill, new SkillContext<MockUnit>(new MockUnit())));
            Assert.AreEqual(SkillCastPhase.None, controller.Phase);
            Assert.AreEqual(0, started);
        }

        [Test]
        public void ExecutionPriority_HigherSkillCanReplaceBusyCast()
        {
            SkillSystemServices.Clock = new FixedSkillClock(0f, 0.1f);

            var low = CreateSkill(2f, canBeInterrupted: true, executionPriority: 1);
            var high = CreateSkill(1f, canBeInterrupted: true, executionPriority: 10, skillName: "High");

            var controller = new SkillExecutionController<MockUnit>();
            int interrupted = 0;
            controller.OnCastInterrupted += (_, __) => interrupted++;

            Assert.IsTrue(controller.TryExecute(low, new SkillContext<MockUnit>(new MockUnit())));
            Assert.IsTrue(controller.TryExecute(high, new SkillContext<MockUnit>(new MockUnit())));

            Assert.AreEqual(1, interrupted);
            Assert.AreEqual("High", controller.ActiveSkill.InformationLayer.Name);
        }

        [Test]
        public void CompleteCast_WritesLastCastElapsedAndClearsLiveElapsed()
        {
            var skill = CreateSkill(1f);
            var controller = new SkillExecutionController<MockUnit>();
            float lastCast = -1f;
            float liveAtComplete = -1f;
            controller.OnCastCompleted += (_, __) =>
            {
                lastCast = controller.LastCastElapsed;
                liveAtComplete = controller.Elapsed;
            };

            Assert.IsTrue(controller.TryExecute(skill, new SkillContext<MockUnit>(new MockUnit())));
            controller.Tick();
            controller.Tick();

            Assert.AreEqual(1f, lastCast, 0.001f);
            Assert.AreEqual(0f, liveAtComplete, 0.001f);
            Assert.AreEqual(1f, controller.LastCastElapsed, 0.001f);
        }

        [Test]
        public void Interrupt_DoesNotOverwriteLastSnapshots()
        {
            var first = CreateSkill(1f);
            var second = CreateSkill(2f, skillName: "Second");
            var controller = new SkillExecutionController<MockUnit>();

            Assert.IsTrue(controller.TryExecute(first, new SkillContext<MockUnit>(new MockUnit())));
            controller.Tick();
            controller.Tick();
            Assert.AreEqual(1f, controller.LastCastElapsed, 0.001f);

            Assert.IsTrue(controller.TryExecute(second, new SkillContext<MockUnit>(new MockUnit())));
            Assert.IsTrue(controller.TryInterrupt(InterruptReason.Manual));
            Assert.AreEqual(1f, controller.LastCastElapsed, 0.001f);
        }

        [Test]
        public void Interrupt_ReportsPhaseAndElapsedInEvent()
        {
            var skill = CreateSkill(2f);
            var controller = new SkillExecutionController<MockUnit>();
            CastInterruptInfo info = default;
            controller.OnCastInterrupted += (_, captured) => info = captured;

            Assert.IsTrue(controller.TryExecute(skill, new SkillContext<MockUnit>(new MockUnit())));
            controller.Tick();
            Assert.IsTrue(controller.TryInterrupt(InterruptReason.Damage));

            Assert.AreEqual(InterruptReason.Damage, info.Reason);
            Assert.AreEqual(SkillCastPhase.Casting, info.Phase);
            Assert.AreEqual(0.5f, info.Elapsed, 0.001f);
            Assert.AreEqual(0.5f, info.CastElapsed, 0.001f);
            Assert.AreEqual(0.5f, info.TotalElapsed, 0.001f);
        }

        [Test]
        public void Interrupt_FallsBackToLegacyKeyWhenPhaseKeysMissing()
        {
            var data = new SkillData<MockUnit>
            {
                SkillId = "test.legacy_interrupt",
                SkillName = "Legacy",
                SkillType = SkillType.Active,
                TriggerEvents = new List<string> { "OnAttack" },
                Profile = new SkillProfile()
            };
            data.SetValue(SkillCastTiming.CastTimeKey, 2f);
            data.SetValue(SkillCastTiming.CanBeInterruptedKey, false);
            var skill = SkillFactory<MockUnit>.CreateSkill(data);
            var controller = new SkillExecutionController<MockUnit>();

            Assert.IsTrue(controller.TryExecute(skill, new SkillContext<MockUnit>(new MockUnit())));
            Assert.IsFalse(controller.TryInterrupt(InterruptReason.Damage));
            Assert.IsTrue(controller.IsBusy);
        }

        [Test]
        public void ChannelSession_KeepsControllerBusyAndAcceptsInterrupt()
        {
            var host = new HostUnit();
            var channel = new ChannelMechanism<HostUnit> { duration = 2f };
            var data = new SkillData<HostUnit>
            {
                SkillId = "test.channel",
                SkillName = "Channel",
                SkillType = SkillType.Active,
                TriggerEvents = new List<string> { "OnAttack" },
                Profile = new SkillProfile(),
                Mechanisms = { channel }
            };
            var skill = SkillFactory<HostUnit>.CreateSkill(data);
            var controller = new SkillExecutionController<HostUnit>();
            int interrupted = 0;
            controller.OnCastInterrupted += (_, __) => interrupted++;

            Assert.IsTrue(controller.TryExecute(skill, new SkillContext<HostUnit>(host, host)));
            Assert.IsTrue(controller.IsBusy);
            Assert.AreEqual(SkillCastPhase.None, controller.Phase);
            Assert.IsTrue(controller.TryInterrupt(InterruptReason.Damage));
            Assert.AreEqual(1, interrupted);
            Assert.IsFalse(controller.IsBusy);
        }
    }
}
