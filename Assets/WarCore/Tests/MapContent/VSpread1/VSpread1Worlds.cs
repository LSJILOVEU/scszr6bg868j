using System;
using System.Collections;
using System.Reflection;
using WarCore.Combat;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using WarCore.Tests.Projectile;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests.VSpread1
{
    // VSPREAD-1 fixtures: packs made here on the shipped body and stances (rwr-stance), the shipped layers, the range demo, a
    // counter of combat.spread draws and the two turns written out as the card gives them.
    internal static class VSpread1Worlds
    {
        // The shipped vertical spread (rwr-vspread): standing 1000, crouch 800, prone 600, aim center, snap 600 mm.
        internal static VerticalSpread Shipped(VerticalAim aim = VerticalAim.Center) => new VerticalSpread(1000, new[] { 800, 600 }, aim, 600);

        internal static CombatPack Layer(string layer) => RwrPack.Load(layer).BuildCombat();

        // A fixed 2000 millidegree gun (no growth, no recovery), every tick, 1000 rounds, no damage.
        internal static WeaponDef Fixed(int falloffEnd = 67, int growth = 0, int max = 2000) =>
            new WeaponDef(1000, 1000, 60000, default, SimFix.FromInt(falloffEnd), 0, new SpreadSpec(2000, growth, 0, max));

        // A pack on the shipped body and stances (or none) with one weapon, a damage resolver, nearest visible enemy, and the
        // given vertical spread (null = off); speed > 0 makes the weapon fire projectiles.
        internal static CombatPack VPack(VerticalSpread vertical, WeaponDef? weapon = null, bool stanced = true, int speed = 0, bool friendlyFire = false)
        {
            CombatPack shipped = Layer("rwr-stance");
            return new CombatPack(new[] { weapon ?? Fixed() }, new FalloffDamage(), new NearestVisibleEnemy(), 100 * SimFix.Scale, friendlyFire,
                body: shipped.Body, projectileSpeeds: speed == 0 ? null : new[] { speed }, stances: stanced ? shipped.Stances.ToArray() : null,
                verticalSpread: vertical);
        }

        // The no-spread weapon of the stance tests (Proj1World.Weapon: 30 ticks a shot, 10 damage).
        internal static WeaponDef NoSpread(int falloffEnd = 60) => Proj1World.Weapon(falloffEnd);

        internal static CombatSystem CombatOf(SimSession s) => WarCore.Tests.Stance.Stance1Worlds.CombatOf(s);
        internal static Simulation SimOf(SimSession s) => M3TestWorld.Sim(s);
        internal static EntityId Unit(SimSession s, ulong key) => M3TestWorld.Unit(s, key);

        // The number of combat.spread draws a world made so far (seed 41): replays a fresh stream until its state matches.
        internal static int SpreadDraws(SimSession s, int max = 20000) => SpreadDraws(SimOf(s), max);

        internal static int SpreadDraws(Simulation sim, int max = 20000)
        {
            ulong live = StreamHash(sim.Rng, "combat.spread");
            var fresh = new SimRandom(41);
            RandomStreamId id = fresh.Stream("combat.spread");
            for (int n = 0; n <= max; n++)
            {
                if (StreamHash(fresh, "combat.spread") == live) return n;
                fresh.NextInt(id, 65536);
            }
            return -1;
        }

        private static ulong StreamHash(SimRandom rng, string name)
        {
            var streams = (IDictionary)typeof(SimRandom).GetField("streams", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rng);
            var hash = new StateHash();
            ((IStateHash)streams[name]).WriteState(hash);
            return hash.Value;
        }

        // The first n numbers of combat.spread from seed 41.
        internal static int[] SpreadRolls(int n)
        {
            var rng = new SimRandom(41);
            RandomStreamId id = rng.Stream("combat.spread");
            var rolls = new int[n];
            for (int i = 0; i < n; i++) rolls[i] = rng.NextInt(id, 65536);
            return rolls;
        }

        // CombatSystem.Rotate (private) written out from its formula: the horizontal turn.
        internal static SimVec3 Rotate(SimVec3 direction, int angleMilli, int roll)
        {
            long offset = angleMilli * (2L * roll - 65535) / 65535;
            SimFix turns = SimFix.Ratio(offset, 360000);
            SimFix sin = SimMath.Sin(turns), cos = SimMath.Cos(turns);
            if (Math.Abs(direction.X.Raw) < SimFix.Scale && Math.Abs(direction.Z.Raw) < SimFix.Scale) direction = direction.Normalized;
            return new SimVec3(direction.X * cos - direction.Z * sin, direction.Y, direction.X * sin + direction.Z * cos);
        }

        // The card's tilt written out (C2 steps 1-3): the rotated horizontal length and height of a normalized direction.
        internal static (SimFix Across, SimFix Up, SimFix H, SimVec3 D) TiltParts(SimVec3 direction, int angleMilli, int roll)
        {
            long offset = angleMilli * (2L * roll - 65535) / 65535;
            SimFix turns = SimFix.Ratio(offset, 360000);
            SimFix sin = SimMath.Sin(turns), cos = SimMath.Cos(turns);
            SimVec3 d = direction.Normalized;
            SimFix h = new SimVec3(d.X, default, d.Z).Length;
            return (h * cos - d.Y * sin, h * sin + d.Y * cos, h, d);
        }

        internal static SimSession Range() => WorldBuilder.Build(VspreadDemo.Recipe(), VspreadDemo.MapBytes(), false);
    }
}
