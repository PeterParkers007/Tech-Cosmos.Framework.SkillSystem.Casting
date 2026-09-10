using System.Collections.Generic;
using NUnit.Framework;
using TechCosmos.SkillSystem.Runtime;

namespace TechCosmos.SkillSystem.Casting.Tests
{
    public class ChannelMechanismTests
    {
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

        sealed class CountMechanism : Mechanism<HostUnit>
        {
            public static readonly Dictionary<int, int> Hits = new();
            public int id;

            public override void Execute(SkillContext<HostUnit> context, IDataLayer<HostUnit> dataLayer)
            {
                Hits.TryGetValue(id, out int n);
                Hits[id] = n + 1;
            }
        }

        [SetUp]
        public void SetUp()
        {
            CountMechanism.Hits.Clear();
            ChannelSessionService.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            ChannelSessionService.ClearAll();
        }

        static ChannelMechanism<HostUnit> CreateChannel(
            float duration,
            bool canBeInterrupted = true,
            int startId = 1,
            int successId = 2,
            int failId = 3)
        {
            return new ChannelMechanism<HostUnit>
            {
                duration = duration,
                canBeInterrupted = canBeInterrupted,
                onStart = { new CountMechanism { id = startId } },
                onSuccess = { new CountMechanism { id = successId } },
                onFail = { new CountMechanism { id = failId } }
            };
        }

        static void Execute(ChannelMechanism<HostUnit> channel, HostUnit host)
        {
            var context = new SkillContext<HostUnit>(host, host);
            channel.Execute(context, dataLayer: null);
        }

        [Test]
        public void ZeroDuration_RunsStartAndSuccessImmediately()
        {
            var host = new HostUnit();
            Execute(CreateChannel(0f), host);

            Assert.AreEqual(1, CountMechanism.Hits[1]);
            Assert.AreEqual(1, CountMechanism.Hits[2]);
            Assert.IsFalse(CountMechanism.Hits.ContainsKey(3));
            Assert.IsFalse(ChannelSessionService.IsActive(host));
        }

        [Test]
        public void Expire_RunsSuccessNotFail()
        {
            var host = new HostUnit();
            Execute(CreateChannel(0.5f), host);

            Assert.AreEqual(1, CountMechanism.Hits[1]);
            Assert.IsTrue(ChannelSessionService.IsActive(host));
            Assert.IsFalse(CountMechanism.Hits.ContainsKey(2));

            host.BuffSystem.BuffUpdate(0.6f);

            Assert.AreEqual(1, CountMechanism.Hits[2]);
            Assert.IsFalse(CountMechanism.Hits.ContainsKey(3));
            Assert.IsFalse(ChannelSessionService.IsActive(host));
            Assert.AreEqual(0.6f, ChannelSessionService.GetLastElapsed(host), 0.001f);
        }

        [Test]
        public void Interrupt_RunsFailNotSuccess()
        {
            var host = new HostUnit();
            Execute(CreateChannel(2f), host);
            host.BuffSystem.BuffUpdate(0.5f);

            Assert.IsTrue(ChannelSessionService.TryInterrupt(host, false));
            Assert.AreEqual(1, CountMechanism.Hits[3]);
            Assert.IsFalse(CountMechanism.Hits.ContainsKey(2));
            Assert.IsFalse(ChannelSessionService.IsActive(host));
            Assert.AreEqual(0.5f, ChannelSessionService.GetLastElapsed(host), 0.001f);
        }

        [Test]
        public void Uninterruptible_IgnoresSoftInterrupt()
        {
            var host = new HostUnit();
            Execute(CreateChannel(2f, canBeInterrupted: false), host);

            Assert.IsFalse(ChannelSessionService.TryInterrupt(host, false));
            Assert.IsTrue(ChannelSessionService.IsActive(host));
            Assert.IsTrue(ChannelSessionService.TryInterrupt(host, true));
            Assert.AreEqual(1, CountMechanism.Hits[3]);
            Assert.IsFalse(ChannelSessionService.IsActive(host));
        }

        [Test]
        public void Replace_FailsPreviousSession()
        {
            var host = new HostUnit();
            Execute(CreateChannel(2f, startId: 1, successId: 2, failId: 3), host);
            Execute(CreateChannel(2f, startId: 4, successId: 5, failId: 6), host);

            Assert.AreEqual(1, CountMechanism.Hits[3]);
            Assert.IsTrue(ChannelSessionService.IsActive(host));
            Assert.AreEqual(1, CountMechanism.Hits[4]);
        }
    }
}
