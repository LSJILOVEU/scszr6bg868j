using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;
using static WarCore.Tests.Down1.Down1World;

namespace WarCore.Tests.Down1
{
    // DOWN-1 D9b: the damage commands of one step reach the reserved N * k + P + B exactly when every unit bleeds out in the step
    // where all projectiles land and a blast hits everyone; no throw. Every unit is downed on tick 10 (bleeding out on tick 21,
    // grace to the end, so the hits of tick 21 are zero-damage commands), each has a fire intent on tick 21 and the melee unit
    // had a swing due on tick 21 (started on tick 1, windup 20). Skipping the casualties in the fire loop is what keeps the
    // count: a casualty that fired or swung would overflow (the mutants of the card).
    public sealed class Down1CapacityTests
    {
        internal const long Due = 21;
        private const int Down = 10, Bleed = 11;

        // The settlement's damage facts of the tick just run.
        private static int DamageFacts(Down1World w)
        {
            int n = 0;
            long tick = w.Sim.Clock.Tick;
            int count = w.Sim.Events.Read(tick).Length;
            for (int e = 0; e < count; e++)
                if (w.Sim.ReadStableEvent(tick, e).Type.Equals(GameplayEvents.Damage)) n++;
            return n;
        }

        // Fires every unit's intent at target on tick Due (taken for a casualty, not fired).
        private static void IntentsOnDue(Down1World w, params (int Shooter, int Target)[] pairs) =>
            w.At(Due, SimSystemPhase.Queries, 27, c => { foreach (var (s, t) in pairs) w.Combat.Submit(c, FireIntent.AtTarget(w.Id(s), w.Id(t))); });

        // A projectile weapon of capacity 1: 3 m of range at 0.1 m a tick is 30 ticks of flight, far below its 60 s interval.
        private static WeaponDef Slow() => WarCore.Tests.Projectile.Proj1World.Weapon(falloffEnd: 3, intervalMilli: 60000, magazine: 1000);

        // A huge blast (inner 50 m, friendly) of one per tick: B = N.
        private static BlastRules OneBlast() => new BlastRules(new[] { new BlastSpec("t.all", 50000, 100000, 1, true, false) }, perTick: 1);

        private static void Blast(Down1World w) => w.Blasts.Enqueue(Raw(0, 0, 0), 0, default, 0);

        // k = 1, N = 4: M (team 1, melee, at the origin) swings at G (team 2, 1 m east); J (team 1, a projectile weapon of capacity
        // 1, at (0, 3) m) fires on tick 3 at X (team 2, at (2, 3) m), 2 m at 0.1 m a tick to its body: it lands on tick 21. P = 1, B = 4.
        internal static Down1World K1()
        {
            var weapons = new[] { Gun(), Slow() };
            var pack = Pack(Rules(ticks: Bleed, finishAfter: Bleed), new ScriptedDamage { Fallback = 0 }, 100 * M, OneBlast(),
                melee: new[] { Swing(20, 1) }, weapons: weapons, projectileSpeeds: new[] { 0, 3000 });
            var w = new Down1World(pack, new[] { U(0, 0, 1, 0, 0), U(1 * M, 0, 2, 0), U(0, 3 * M, 1, 1), U(2 * M, 3 * M, 2, 0) });
            w.At(3, SimSystemPhase.Queries, 27, c => w.Combat.Submit(c, FireIntent.AtTarget(w.Id(2), w.Id(3))));
            w.At(Down, SimSystemPhase.Queries, 26, c => { for (int i = 0; i < 4; i++) w.Combat.Down(c, w.Entity(i)); });
            IntentsOnDue(w, (0, 1), (1, 0), (2, 3), (3, 2));
            return w;
        }

        [Test]
        public void D9b_KIsOne_TheStepFillsTheReservation()
        {
            Down1World w = K1();
            Assert.That(w.Combat.Pack.MaxHitsPerAction, Is.EqualTo(1));
            Assert.That(w.Combat.Pack.ProjectileCapacity(new[] { 0, 0, 1, 0 }), Is.EqualTo(1));
            w.StepTo(2);
            Assert.That(w.MeleeOf(0).Pending, Is.True, "the swing due on tick 21");
            w.StepTo(Due - 1);
            for (int i = 0; i < 4; i++) Assert.That(w.Downed(i), Is.True, "unit " + i);
            Blast(w);
            Assert.DoesNotThrow(() => w.Step());
            Assert.That(DamageFacts(w), Is.EqualTo(4 * 1 + 1 + 4), "N * k + P + B");
            for (int i = 0; i < 4; i++) Assert.That(w.Dead(i), Is.True, "unit " + i);
            Assert.That(w.FiresOn(1).Count + w.FiresOn(0).Count + w.FiresOn(2).Count + w.FiresOn(3).Count, Is.EqualTo(0));
        }

        // k = 2, N = 5: strikers S1, S2 (team 1, at (0, 0) and (0, 0.6) m) and bleeders B1, B2 (team 2, at (1, 0) and (1, 0.6) m),
        // all with a two-target swing started on tick 1 and due on tick 21; J (team 2, the projectile weapon, at (-2, 0) m) fires on
        // tick 3 at S1, landing on tick 21. B1, B2 and J are downed on tick 10. On tick 21: 3 bleed-outs, 2 x 2 strikes, P = 1,
        // B = 5: 13 of 2 * 5 + 1 + 5 = 16 (k >= 2 cannot fill it, see the card's appendix H); the bleeders' own swings would add 4.
        internal static Down1World K2()
        {
            var weapons = new[] { Gun(), Slow() };
            var pack = Pack(Rules(ticks: Bleed, finishAfter: Bleed), new ScriptedDamage { Fallback = 0 }, 100 * M, OneBlast(),
                melee: new[] { Swing(20, 1, maxTargets: 2) }, weapons: weapons, projectileSpeeds: new[] { 0, 3000 });
            var w = new Down1World(pack, new[]
            {
                U(0, 0, 1, 0, 0), U(0, M * 6 / 10, 1, 0, 0), U(1 * M, 0, 2, 0, 0), U(1 * M, M * 6 / 10, 2, 0, 0), U(-2 * M, 0, 2, 1),
            });
            w.At(3, SimSystemPhase.Queries, 27, c => w.Combat.Submit(c, FireIntent.AtTarget(w.Id(4), w.Id(0))));
            w.At(Down, SimSystemPhase.Queries, 26, c => { for (int i = 2; i < 5; i++) w.Combat.Down(c, w.Entity(i)); });
            IntentsOnDue(w, (2, 0), (3, 1), (4, 0));
            return w;
        }

        [Test]
        public void D9b_KIsTwo_HalfBleedHalfStrike()
        {
            Down1World w = K2();
            Assert.That(w.Combat.Pack.MaxHitsPerAction, Is.EqualTo(2));
            w.StepTo(Due - 1);
            for (int i = 0; i < 4; i++) Assert.That(w.MeleeOf(i).Pending, Is.EqualTo(i < 2), "unit " + i);
            Blast(w);
            Assert.DoesNotThrow(() => w.Step());
            Assert.That(w.StrikesBy(0, StrikeKind.Landed), Is.EquivalentTo(new[] { 2, 3 }));
            Assert.That(w.StrikesBy(1, StrikeKind.Landed), Is.EquivalentTo(new[] { 2, 3 }));
            Assert.That(DamageFacts(w), Is.EqualTo(3 + 2 * 2 + 1 + 5));
            for (int i = 2; i < 5; i++) Assert.That(w.Dead(i), Is.True, "unit " + i);
        }
    }
}
