using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-P5a with the real scheduler: the battle's own parts are wrapped, a member follows its leader slot by slot, and
    // the squads system has an order of its own in a world with everything on.
    public sealed partial class AiP5aTests
    {
        // Every order the scheduler gives, by unit.
        private sealed class Orders : IMoveOrders
        {
            private readonly IMoveOrders inner;
            private readonly WarCore.Sim.Sim sim;
            internal readonly List<(long tick, ulong unit, SimVec3 target)> Given = new List<(long, ulong, SimVec3)>();
            internal Orders(IMoveOrders inner, WarCore.Sim.Sim sim) { this.inner = inner; this.sim = sim; }
            public void Order(StableId id, SimVec3 target) { Given.Add((sim.Clock.Tick, id.Value, target)); inner.Order(id, target); }
            public void Stop(StableId id) => inner.Stop(id);
            public bool TryGetOrder(StableId id, out SimVec3 target) => inner.TryGetOrder(id, out target);
            public bool CanStandAt(SimVec3 at) => inner.CanStandAt(at);
            public bool IsCommanded(StableId id) => inner.IsCommanded(id);
            public bool HasFailed(StableId id) => inner.HasFailed(id);
            public bool TryRouteCost(SimVec3 from, SimVec3 to, long limitCost, out long cost) => inner.TryRouteCost(from, to, limitCost, out cost);
            internal List<(long tick, SimVec3 target)> To(ulong unit)
            {
                var list = new List<(long, SimVec3)>();
                foreach (var order in Given) if (order.unit == unit) list.Add((order.tick, order.target));
                return list;
            }
        }
        private static Orders Watch(M6World w)
        {
            SimpleAiSystem ai = w.System<SimpleAiSystem>();
            FieldInfo field = typeof(SimpleAiSystem).GetField("moves", BindingFlags.NonPublic | BindingFlags.Instance);
            var orders = new Orders((IMoveOrders)field.GetValue(ai), w.Sim);
            field.SetValue(ai, orders);
            return orders;
        }

        // The follow fixture: one point of team 2 at (60, 30), radius 2 m: 19 slots, 1 m apart, in rows z = 30 (x = 58..62),
        // z = 30 +- 0.866 (x = 58.5..61.5) and z = 30 +- 1.732 (x = 59..61). A squad of two (sizes 1..1, no skirmishers,
        // formed on tick 1, no spread), everybody thinks every 10 ticks, the point is never taken. k0 leads.
        private static readonly ControlPointDef[] FollowPoint = { new ControlPointDef(1, SimVec3.Metres(60, 0, 30), SimFix.FromInt(2), 2) };
        private static M6World Follow(out ulong[] k, UnitSpawn leader, UnitSpawn member, string more = null)
        {
            k = new[] { leader.Key, member.Key };
            return Field(Json(With(Min, 1, Max, 1, Skirmishers, 0, FormTicks, 1), more), new[] { leader, member, Man(90, 2, 150, 50) }, think: 10, points: FollowPoint);
        }
        private static bool IsSlot(SimVec3 at)
        {
            foreach (SimVec3 slot in GuardSlots.Build(FollowPoint[0].Center, FollowPoint[0].Radius))
                if (slot.X.Raw == at.X.Raw && slot.Z.Raw == at.Z.Raw) return true;
            return false;
        }
        private static string Mm(SimVec3 at) => at.X.Raw * 1000 / SimFix.Scale + "," + at.Z.Raw * 1000 / SimFix.Scale;
        private static string Targets(List<(long tick, SimVec3 target)> orders)
        {
            var parts = new List<string>();
            foreach (var order in orders) parts.Add(Mm(order.target));
            return string.Join(" ", parts);
        }

        // 3. The worst case for a member that has arrived: the inner placement is nearest-slot, whose anchor is where the
        // unit stands, so the member's anchor is where its leader stands now. The leader (0.05 m a tick) is on his way to
        // the slot at the far end of the middle row, (62, 30), and the mover takes him along z = 30.5, the centre line of
        // the map cells; the member stands on the slot at the near end, (58, 30). The slots nearest to that line are the
        // four of the row z = 30.866 (0.37 m off it; the middle row is 0.5 m off): x = 58.5, 59.5, 60.5, 61.5. So the
        // member is sent on once for each of them, at its first think after the leader has come nearer to that slot than
        // to the member's own, and never again once the leader stands: 4 orders, the number of slots of that row.
        [Test]
        public void AMemberFollowsItsLeaderSlotBySlot()
        {
            ulong[] e = ByElect(1, 2);
            M6World w = Follow(out ulong[] k, Man(e[0], 1, 50, 30, speed: 1500), Man(e[1], 1, 58, 30, speed: 3000));
            L4.Moves(w).Order(Id(w, k[0]), SimVec3.Metres(62, 0, 30));
            Orders orders = Watch(w);
            long stood = 0;
            for (int tick = 1; tick <= 300; tick++)
            {
                w.Step();
                Assert.That(Band(w, k[1]), Is.EqualTo(k[0]), "tick " + tick);
                if (stood == 0 && !L4.Moves(w).TryGetOrder(Id(w, k[0]), out _)) stood = tick;
            }
            List<(long tick, SimVec3 target)> member = orders.To(Stable(k[1]));
            Assert.That(Targets(member), Is.EqualTo("58500,30866 59500,30866 60500,30866 61500,30866"));
            foreach (var order in member) Assert.That(IsSlot(order.target), "an order names a slot");
            Assert.That(member.Count, Is.LessThanOrEqualTo(4 + 1), "the slots of the row and one");
            Assert.That(orders.To(Stable(k[0])), Is.Empty, "the leader keeps the slot he was going for");
            Assert.That(stood, Is.InRange(200, 290));
            Assert.That(member[member.Count - 1].tick, Is.LessThan(stood), "no order once the leader stands");
            Assert.That(CombatSystem.Horizontal(At(w, k[0]) - SimVec3.Metres(62, 0, 30)).LengthSquared,
                Is.LessThanOrEqualTo(new SimVec3(SimFix.Ratio(1, 4), default, default).LengthSquared));
        }

        // 3, control one. The leader stands on his slot, (62, 30), from the start; the member stands on the free slot
        // nearest to him, (61.5, 30.866) (as near as the slot on the other side, and the lower slot number). Nobody is
        // given an order in 300 ticks.
        [Test]
        public void AMemberOfALeaderWhoStandsIsLeftAlone()
        {
            ulong[] e = ByElect(1, 2);
            M6World w = Follow(out ulong[] k, Man(e[0], 1, 62, 30, speed: 1500), Man(e[1], 1, 0, 0, xMm: 61500, zMm: 30866, speed: 3000));
            Orders orders = Watch(w);
            for (int tick = 1; tick <= 300; tick++) { w.Step(); Assert.That(Band(w, k[1]), Is.EqualTo(k[0]), "tick " + tick); }
            Assert.That(orders.Given, Is.Empty);
        }

        // 3, control two. The same walk under the scatter placement, whose anchor does not depend on where the unit stands:
        // the member goes for the slot nearest to its leader's spot at its first think (the leader is still on his way to
        // the far end and keeps his slot), and is given no other order while the leader walks his 12 m and after.
        [Test]
        public void AMemberUnderAnAnchorThatStaysIsOrderedOnce()
        {
            ulong[] e = ByElect(1, 2);
            M6World w = Follow(out ulong[] k, Man(e[0], 1, 50, 30, speed: 1500), Man(e[1], 1, 58, 30, speed: 3000),
                "\"placement\": \"scatter\", \"scatterMm\": 1500");
            L4.Moves(w).Order(Id(w, k[0]), SimVec3.Metres(62, 0, 30));
            Orders orders = Watch(w);
            SimVec3 start = At(w, k[0]);
            for (int tick = 1; tick <= 300; tick++) { w.Step(); Assert.That(Band(w, k[1]), Is.EqualTo(k[0]), "tick " + tick); }
            List<(long tick, SimVec3 target)> member = orders.To(Stable(k[1]));
            Assert.That(member, Has.Count.EqualTo(1));
            Assert.That(member[0].tick, Is.LessThanOrEqualTo(10));
            Assert.That(IsSlot(member[0].target));
            // The spot is the leader's (the member asks as him), so it is the slot nearest to the leader's scatter anchor.
            SimVec3 anchor = new ScatterPlacement(1500).Anchor(new AiUnit(Id(w, k[0]), 1, default), 0, FieldOf(w));
            SimVec3 nearest = default; SimWide best = default; bool any = false;
            foreach (SimVec3 slot in GuardSlots.Build(FollowPoint[0].Center, FollowPoint[0].Radius))
            {
                if (slot.Equals(SimVec3.Metres(62, 0, 30))) continue;
                SimWide distance = CombatSystem.Horizontal(anchor - slot).LengthSquared;
                if (!any || distance < best) { nearest = slot; best = distance; any = true; }
            }
            Assert.That(Mm(member[0].target), Is.EqualTo(Mm(nearest)));
            Assert.That(CombatSystem.Horizontal(At(w, k[0]) - start).LengthSquared, Is.GreaterThan(new SimVec3(SimFix.FromInt(10), default, default).LengthSquared));
        }
        private static AiField FieldOf(M6World w) =>
            (AiField)typeof(SimpleAiSystem).GetField("field", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<SimpleAiSystem>());

        // Every system of a battle has an order of its own, or the world does not build (Sim.Register). The playtest world
        // with a player and every AI rule on, squads included, builds and runs; the squads system steps before every other
        // gameplay system, so the scheduler never meets a squad whose leader fell on the tick before.
        [Test]
        public void EverySystemHasAnOrderOfItsOwnWithAPlayerAndEverythingOn()
        {
            const string all = "\"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000, "
                + "\"pressureDeathRadiusMm\": 3000, \"pressureDeathShock\": 2000, \"pressureNearRadiusMm\": 3000, \"pressureNearShock\": 1000, \"pressureDecayPermille\": 12, "
                + "\"pressureBrokenAt\": 7000, \"pressureRecoverAt\": 1000, \"pressurePinnedAt\": 2000, \"pressureSpreadPermille\": 500, \"pressureBreakSharePermille\": 300, "
                + "\"coverRadiusMm\": 9000, \"coverStepMm\": 1000, \"coverDepthMm\": 2000, \"traitSpreadPermille\": 300";
            ContentPack layer = Pack(Json(With(Max, 9, SpreadMin, 1500, SpreadMax, 6000), all), DemoRecipes.PlaytestLayer);
            WorldRecipe recipe = DemoRecipes.MpRespawnDemo(layer).WithPack(layer);
            Assert.That(recipe.PlayerKey, Is.Not.Null, "the world has a player");
            var w = new M6World(recipe, DemoRecipes.M7MapBytes());
            var orders = new Dictionary<int, string>();
            var ids = new Dictionary<string, int>();
            foreach (string entry in AssemblyWorlds.Systems(w.Sim).Split(';'))
            {
                string[] parts = entry.Split('|');
                int order = int.Parse(parts[2]);
                Assert.That(orders.ContainsKey(order), Is.False, parts[0] + " shares order " + order + " with " + (orders.TryGetValue(order, out string other) ? other : null));
                orders[order] = parts[0]; ids[parts[0]] = order;
            }
            foreach (string id in new[] { "battle.ai-squads", "control.player", "battle.ai", "battle.ai-pressure", "battle.respawn" })
                Assert.That(ids.ContainsKey(id), id + " is registered: " + string.Join(" ", ids.Keys));
            Assert.That(ids["battle.ai-squads"], Is.EqualTo(3));
            foreach (KeyValuePair<string, int> pair in ids)
                if (pair.Key != "battle.ai-squads") Assert.That(pair.Value, Is.GreaterThan(3), pair.Key);
            Assert.That(Sq(w).Order, Is.EqualTo(3));
            w.Step(60);
            Assert.That(Sq(w).SquadCount, Is.GreaterThan(0));
        }
    }
}
