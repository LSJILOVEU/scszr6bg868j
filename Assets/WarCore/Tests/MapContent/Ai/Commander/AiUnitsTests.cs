using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.CommanderBoard;

namespace WarCore.Tests
{
    // RC2-3 R0 at its one home, AiUnits.Steered. The commander and the test checker both ask it, so the checker cannot see a
    // condition lost there: each condition is held here on its own. A unit the computer steers has a team (1 or more), is
    // alive, has a position, is not the unit the player controls and is not commanded.
    public sealed class AiUnitsTests
    {
        private sealed class Orders : IMoveOrders
        {
            internal ulong Commanded;
            public bool IsCommanded(StableId unit) => unit.Value == Commanded;
            public void Order(StableId unit, SimVec3 target) { }
            public void Stop(StableId unit) { }
            public bool TryGetOrder(StableId unit, out SimVec3 target) { target = default; return false; }
        }

        // 1 is steered; 2 is the player's; 3 has team 0; 4 fell on tick 2; 5 is commanded; 6 has no position. The world is
        // stepped to tick 3, then 3's team and 6's position are changed and nothing is stepped again.
        [Test]
        public void ASteeredUnitHasATeamLifeAPlaceAndNoOtherHand()
        {
            var units = new[] { Man(1, 1, 30), Man(2, 1, 32), Man(3, 1, 34), Man(4, 1, 36), Man(5, 1, 38), Man(6, 1, 40), Man(100, 2, 110) };
            var b = new CommanderBoard(new[] { Point(1, 20, 1), Point(2, 100, 2) }, units, new[] { Base(1, 4), Base(2, 116) }, 120, player: 2, check: false);
            b.Fall(2, 4);
            b.To(3);
            EntityStore store = b.W.Sim.Entities;
            ComponentType<Combatant> combatants = store.Resolve<Combatant>();
            ComponentType<Position> positions = store.Resolve<Position>();
            Combatant three = store.Read(combatants, b.W.Unit(3));
            store.Set(combatants, b.W.Unit(3), new Combatant(0, three.Weapon, three.Ammo, three.DueMilli, three.SpreadStored, three.SpreadLastMilli));
            store.Remove(positions, b.W.Unit(6));
            var orders = new Orders { Commanded = b.Id(5).Value };
            SimVec3 one = store.Read(positions, b.W.Unit(1)).Value;

            Assert.That(AiUnits.Steered(b.W.Combat, combatants, positions, orders, b.Controlled, b.W.Unit(1), out StableId id, out int team, out SimVec3 at), Is.True);
            Assert.That(id, Is.EqualTo(b.Id(1)));
            Assert.That(team, Is.EqualTo(1));
            Assert.That(at, Is.EqualTo(one));
            Assert.That(b.W.Combat.IsDead(b.W.Unit(4)), Is.True, "4 fell");
            foreach (ulong key in new ulong[] { 2, 3, 4, 5, 6 })
            {
                Assert.That(AiUnits.Steered(b.W.Combat, combatants, positions, orders, b.Controlled, b.W.Unit(key), out id, out team, out at), Is.False, "unit " + key);
                Assert.That(id, Is.EqualTo(default(StableId)), "unit " + key);
                Assert.That(team, Is.EqualTo(0), "unit " + key);
            }
            // Without the commanded mark and with the player's hand elsewhere, 5 is steered: only the condition keeps it out.
            orders.Commanded = 0;
            Assert.That(AiUnits.Steered(b.W.Combat, combatants, positions, orders, b.Controlled, b.W.Unit(5), out _, out team, out _), Is.True);
            Assert.That(team, Is.EqualTo(1));
            Assert.That(AiUnits.Steered(b.W.Combat, combatants, positions, orders, null, b.W.Unit(2), out _, out _, out _), Is.True, "2 without a player");
        }
    }
}
