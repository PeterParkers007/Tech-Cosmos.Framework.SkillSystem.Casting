using System.Collections.Generic;
using NUnit.Framework;
using TechCosmos.SkillSystem.Runtime;

namespace TechCosmos.SkillSystem.Casting.Tests
{
    /// <summary>施法控制器：读条 / 引导 / 打断。</summary>
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

        static ISkill<MockUnit> CreateSkill(
            float castTime,
            float channelTime = 0f,
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
            data.SetValue(SkillCastTiming.ChannelTimeKey, channelTime);
            data.SetValue(SkillCastTiming.CanBeInterruptedKey, canBeInterrupted);
            return SkillFactory<MockUnit>.CreateSkill(data);
        }

        [SetUp]
        public void SetUp()
        {
            SkillSystemServices.Clock = new FixedSkillClock(0f, 0.5f);
        }

        [TearDown]
        public void TearDown()
        {
            SkillSystemServices.Clock = new UnitySkillClock();
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
        public void ChannelCast_CompletesAfterCastAndChannelTicks()
        {
            var skill = CreateSkill(0.5f, channelTime: 0.5f);
            var controller = new SkillExecutionController<MockUnit>();
            int completed = 0;
            controller.OnCastCompleted += (_, __) => completed++;

            Assert.IsTrue(controller.TryExecute(skill, new SkillContext<MockUnit>(new MockUnit())));
            Assert.AreEqual(SkillCastPhase.Casting, controller.Phase);

            controller.Tick();
            Assert.AreEqual(SkillCastPhase.Channeling, controller.Phase);

            controller.Tick();
            Assert.AreEqual(SkillCastPhase.None, controller.Phase);
            Assert.AreEqual(1, completed);
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
        public void CompleteCast_WritesLastElapsedBeforePipelineAndClearsLiveElapsed()
        {
            var skill = CreateSkill(1f);
            var controller = new SkillExecutionController<MockUnit>();
            float lastAtComplete = -1f;
            float liveAtComplete = -1f;
            controller.OnCastCompleted += (_, __) =>
            {
                lastAtComplete = controller.LastElapsed;
                liveAtComplete = controller.Elapsed;
            };

            Assert.IsTrue(controller.TryExecute(skill, new SkillContext<MockUnit>(new MockUnit())));
            controller.Tick();
            controller.Tick();

            Assert.AreEqual(1f, lastAtComplete, 0.001f);
            Assert.AreEqual(0f, liveAtComplete, 0.001f);
            Assert.AreEqual(1f, controller.LastElapsed, 0.001f);
            Assert.AreEqual(0f, controller.Elapsed, 0.001f);
        }

        [Test]
        public void TryRelease_FiresPipelineWithCurrentElapsed()
        {
            var skill = CreateSkill(2f);
            var controller = new SkillExecutionController<MockUnit>();
            int completed = 0;
            controller.OnCastCompleted += (_, __) => completed++;

            Assert.IsTrue(controller.TryExecute(skill, new SkillContext<MockUnit>(new MockUnit())));
            controller.Tick();
            Assert.AreEqual(0.5f, controller.Elapsed, 0.001f);
            Assert.IsTrue(controller.TryRelease());

            Assert.AreEqual(1, completed);
            Assert.AreEqual(0.5f, controller.LastElapsed, 0.001f);
            Assert.IsFalse(controller.IsBusy);
            Assert.IsFalse(controller.TryRelease());
        }

        [Test]
        public void TryRelease_WorksWhenCastCannotBeInterrupted()
        {
            var skill = CreateSkill(2f, canBeInterrupted: false);
            var controller = new SkillExecutionController<MockUnit>();
            int completed = 0;
            controller.OnCastCompleted += (_, __) => completed++;

            Assert.IsTrue(controller.TryExecute(skill, new SkillContext<MockUnit>(new MockUnit())));
            Assert.IsFalse(controller.TryInterrupt(InterruptReason.Damage));
            Assert.IsTrue(controller.TryRelease());
            Assert.AreEqual(1, completed);
        }

        [Test]
        public void TryRelease_DuringCastSkipsPendingChannel()
        {
            var skill = CreateSkill(2f, channelTime: 2f);
            var controller = new SkillExecutionController<MockUnit>();
            int completed = 0;
            controller.OnCastCompleted += (_, __) => completed++;

            Assert.IsTrue(controller.TryExecute(skill, new SkillContext<MockUnit>(new MockUnit())));
            Assert.AreEqual(SkillCastPhase.Casting, controller.Phase);
            Assert.IsTrue(controller.TryRelease());

            Assert.AreEqual(1, completed);
            Assert.AreEqual(SkillCastPhase.None, controller.Phase);
        }

        [Test]
        public void Interrupt_DoesNotOverwriteLastElapsed()
        {
            var first = CreateSkill(1f);
            var second = CreateSkill(2f, skillName: "Second");
            var controller = new SkillExecutionController<MockUnit>();

            Assert.IsTrue(controller.TryExecute(first, new SkillContext<MockUnit>(new MockUnit())));
            controller.Tick();
            controller.Tick();
            Assert.AreEqual(1f, controller.LastElapsed, 0.001f);

            Assert.IsTrue(controller.TryExecute(second, new SkillContext<MockUnit>(new MockUnit())));
            Assert.IsTrue(controller.TryInterrupt(InterruptReason.Manual));
            Assert.AreEqual(1f, controller.LastElapsed, 0.001f);
        }
    }
}
