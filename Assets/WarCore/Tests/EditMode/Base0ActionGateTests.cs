using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // BASE-0 V14-V17: the "may this unit do this" table.
    public sealed class Base0ActionGateTests
    {
        private sealed class Fixed : IActionLimit
        {
            private readonly UnitAction denied;
            internal readonly List<EntityId> Asked = new List<EntityId>();
            internal Fixed(UnitAction denied) => this.denied = denied;
            public UnitAction Denied(EntityId unit)
            {
                Asked.Add(unit);
                return denied;
            }
        }

        private sealed class OnlyFor : IActionLimit
        {
            private readonly EntityId unit;
            private readonly UnitAction denied;
            internal OnlyFor(EntityId unit, UnitAction denied) { this.unit = unit; this.denied = denied; }
            public UnitAction Denied(EntityId asked) => asked.Equals(unit) ? denied : UnitAction.None;
        }

        private static readonly EntityId Unit = new EntityId(3, 1);
        private static readonly EntityId Other = new EntityId(4, 1);

        [Test]
        public void V14_EmptyGateAllowsEverything()
        {
            var gate = new ActionGate();
            Assert.That(gate.Count, Is.EqualTo(0));
            Assert.That(gate.Denied(Unit), Is.EqualTo(UnitAction.None));
            Assert.That(gate.Allows(Unit, UnitAction.Fire), Is.True);
            Assert.That(gate.Allows(Unit, (UnitAction)2047), Is.True);
        }

        [Test]
        public void V15_DeniedIsTheUnion()
        {
            var gate = new ActionGate();
            var first = new Fixed(UnitAction.Dash | UnitAction.Traverse);
            var second = new Fixed(UnitAction.Fire);
            gate.Add(first);
            gate.Add(second);
            Assert.That(gate.Count, Is.EqualTo(2));
            Assert.That(gate.Denied(Unit), Is.EqualTo(UnitAction.Dash | UnitAction.Traverse | UnitAction.Fire));
            Assert.That(gate.Allows(Unit, UnitAction.Fire), Is.False);
            Assert.That(gate.Allows(Unit, UnitAction.Dash), Is.False);
            Assert.That(gate.Allows(Unit, UnitAction.Move), Is.True);
            Assert.That(gate.Allows(Unit, UnitAction.Move | UnitAction.Fire), Is.False);
            Assert.That(gate.Allows(Unit, UnitAction.Move | UnitAction.Reload), Is.True);
            Assert.That(first.Asked, Does.Contain(Unit));
            Assert.That(second.Asked, Does.Contain(Unit));
        }

        [Test]
        public void V15_EachUnitIsAskedForItself()
        {
            var gate = new ActionGate();
            gate.Add(new OnlyFor(Unit, UnitAction.Fire));
            gate.Add(new OnlyFor(Other, UnitAction.Melee));
            Assert.That(gate.Denied(Unit), Is.EqualTo(UnitAction.Fire));
            Assert.That(gate.Denied(Other), Is.EqualTo(UnitAction.Melee));
            Assert.That(gate.Allows(Other, UnitAction.Fire), Is.True);
            Assert.That(gate.Allows(Unit, UnitAction.Melee), Is.True);
        }

        [Test]
        public void V16_Rejections()
        {
            var gate = new ActionGate();
            var limit = new Fixed(UnitAction.Fire);
            Assert.That(Assert.Throws<ArgumentNullException>(() => gate.Add(null)).ParamName, Is.EqualTo("limit"));
            gate.Add(limit);
            Assert.That(Assert.Throws<ArgumentException>(() => gate.Add(limit)).ParamName, Is.EqualTo("limit"));
            Assert.That(gate.Count, Is.EqualTo(1));
            Assert.DoesNotThrow(() => gate.Add(new Fixed(UnitAction.Fire)), "an equal but different limit is a second one");
            Assert.That(gate.Count, Is.EqualTo(2));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => gate.Allows(Unit, UnitAction.None)).ParamName, Is.EqualTo("action"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new ActionGate().Allows(Unit, UnitAction.None)).ParamName, Is.EqualTo("action"));
        }

        [Test]
        public void V17_ActionValuesAreFixed()
        {
            Assert.That((int)UnitAction.None, Is.EqualTo(0));
            Assert.That((int)UnitAction.Move, Is.EqualTo(1));
            Assert.That((int)UnitAction.Dash, Is.EqualTo(2));
            Assert.That((int)UnitAction.Traverse, Is.EqualTo(4));
            Assert.That((int)UnitAction.Fire, Is.EqualTo(8));
            Assert.That((int)UnitAction.Melee, Is.EqualTo(16));
            Assert.That((int)UnitAction.Block, Is.EqualTo(32));
            Assert.That((int)UnitAction.Throw, Is.EqualTo(64));
            Assert.That((int)UnitAction.Reload, Is.EqualTo(128));
            Assert.That((int)UnitAction.Stance, Is.EqualTo(256));
            Assert.That((int)UnitAction.Interact, Is.EqualTo(512));
            Assert.That((int)UnitAction.Capture, Is.EqualTo(1024));
            Assert.That(Enum.GetValues(typeof(UnitAction)).Length, Is.EqualTo(12), "None and the eleven actions");
            Assert.That(typeof(UnitAction).IsDefined(typeof(FlagsAttribute), false), Is.True);
        }
    }
}
