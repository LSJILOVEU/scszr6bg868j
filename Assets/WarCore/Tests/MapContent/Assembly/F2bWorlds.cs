using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // F2b fixtures. Seed, units, map, combat pack and match pack of both worlds are written out here; nothing is taken from
    // DemoRecipes, RwrPack or another fixture, so a change of a shipped default cannot move the recorded values.
    internal static class F2bWorlds
    {
        internal const string GoldenPath = "Assets/WarCore/Tests/MapContent/Assembly/Golden/f2b-ticks.txt";
        internal const int LastTick = 300;
        internal const ulong PlayerKey = 1;
        internal static readonly string[] Names = { "PlayerMatch", "BlockAiMatch" };

        private sealed class FixedHit : IHitResolver
        {
            public ulong PolicyKey => 0x4632420000000001;
            public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => maxHealthRaw / 40;
        }

        internal const long MaxHealthRaw = 100L * SimFix.Scale;

        internal static CombatPack Guns() =>
            new CombatPack(
                new[] { new WeaponDef(10000, 8, 60000, SimFix.FromInt(30), SimFix.FromInt(60), SimFix.Scale) },
                new FixedHit(), new NearestVisibleEnemy(), MaxHealthRaw, false, false, 0, 30000);

        internal static CombatPack Blades() =>
            new CombatPack(Array.Empty<WeaponDef>(), new FixedHit(), new NearestVisibleEnemy(), MaxHealthRaw, false, false, 0, 30000,
                new[] { new MeleeSpec(6, 2, 9, SimFix.FromInt(2), 500, SimFix.FromInt(1), SimFix.FromInt(10), 3, SimFix.Ratio(18, 10)) },
                new[] { new BlockSpec(500, 1000, 1000) });

        private static SimVec3 P(int x, int z) => SimVec3.Metres(x, 0, z);

        internal static MatchPack Match() =>
            new MatchPack(
                new[] { new ControlPointDef(7, P(40, 10), SimFix.FromInt(3)), new ControlPointDef(8, P(40, 30), SimFix.FromInt(3)) },
                new[] { new SpawnAnchor(1, P(8, 20)), new SpawnAnchor(2, P(72, 20)) },
                2, 1, false, 600, InterruptMode.Reset, 1, 90, 15, 9000, SimFix.FromInt(60), 4, 4, SimFix.Ratio(3, 2));

        internal static byte[] Map() => WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (39, 18, 2, 4)));

        // Team 1 holds the player (key 1) and three soldiers 20 m from team 2; each team keeps two reserves.
        internal static WorldRecipe PlayerMatch() =>
            new WorldRecipe(43, MoverKind.InterimCrowd,
                new[]
                {
                    new UnitSpawn(1, 0, 1, 30000, 20000, 4000, 1, 0),
                    new UnitSpawn(2, 0, 1, 30000, 16000, 4000, 1, 0),
                    new UnitSpawn(3, 0, 1, 30000, 24000, 4000, 1, 0),
                    new UnitSpawn(4, 0, 1, 28000, 20000, 4000, 1, 0),
                    new UnitSpawn(5, 0, 1, 0, 0, 4000, 1, 0, true),
                    new UnitSpawn(6, 0, 1, 0, 0, 4000, 1, 0, true),
                    new UnitSpawn(11, 0, 2, 50000, 20000, 4000, 2, 0),
                    new UnitSpawn(12, 0, 2, 50000, 16000, 4000, 2, 0),
                    new UnitSpawn(13, 0, 2, 50000, 24000, 4000, 2, 0),
                    new UnitSpawn(14, 0, 2, 52000, 20000, 4000, 2, 0),
                    new UnitSpawn(15, 0, 2, 0, 0, 4000, 2, 0, true),
                    new UnitSpawn(16, 0, 2, 0, 0, 4000, 2, 0, true),
                },
                Array.Empty<BridgeCommand>())
                .WithCombat(Guns()).WithMatch(Match()).WithCrowdPass(CrowdPassKind.TeamIdle).WithFacing().WithPlayer(PlayerKey);

        // Swordsmen with block specs, facing on, block AI on, under the same match rules; no player.
        internal static WorldRecipe BlockAiMatch() =>
            new WorldRecipe(47, MoverKind.InterimCrowd,
                new[]
                {
                    new UnitSpawn(1, 0, 1, 39250, 30000, 3000, 1, -1, false, 0, 0),
                    new UnitSpawn(2, 0, 1, 39250, 26000, 3000, 1, -1, false, 0, 0),
                    new UnitSpawn(3, 0, 1, 0, 0, 3000, 1, -1, true, 0, 0),
                    new UnitSpawn(11, 0, 2, 40750, 30000, 3000, 2, -1, false, 0, 0),
                    new UnitSpawn(12, 0, 2, 40750, 26000, 3000, 2, -1, false, 0, 0),
                    new UnitSpawn(13, 0, 2, 0, 0, 3000, 2, -1, true, 0, 0),
                },
                Array.Empty<BridgeCommand>())
                .WithCombat(Blades()).WithMatch(Match()).WithCrowdPass(CrowdPassKind.TeamIdle).WithFacing().WithBlockAi();

        internal static WorldRecipe Make(string name) => name == "PlayerMatch" ? PlayerMatch() : BlockAiMatch();

        // The player walks toward +X while firing at team 2, then stands and fires at another enemy.
        internal static void Script(SimSession session, string name)
        {
            if (name != "PlayerMatch") return;
            session.SetPlayerInput(1, new PlayerInput(1000, 0, 50000, 20000, true));
            session.SetPlayerInput(90, new PlayerInput(0, 0, 50000, 24000, true));
            session.SetPlayerInput(200, new PlayerInput(0, -1000, 50000, 16000, false));
        }

        internal static SimSession Build(WorldRecipe recipe, bool reverse = false) => AssemblyWorlds.Build(recipe, Map(), reverse);

        internal static T System<T>(Simulation sim) where T : class
        {
            foreach (object entry in (IEnumerable)typeof(Simulation).GetField("systems", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sim))
            {
                object system = entry.GetType().GetField("System", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                if (system is T value) return value;
            }
            return null;
        }

        internal sealed class Run
        {
            internal readonly List<ulong> Hashes = new List<ulong>();
            internal long FinalTick;
            internal bool Finished;
            internal int Fires, PlayerFires, HoldingTicks, Deaths;
        }

        // Tick 0 to LastTick: the hash after the build, then after every step. Branch evidence is counted along the way.
        internal static Run Play(string name, bool reverse)
        {
            SimSession session = Build(Make(name), reverse);
            Simulation sim = M3TestWorld.Sim(session);
            CombatSystem combat = System<CombatSystem>(sim);
            StableId player = StableEntityKey.Placed(1, PlayerKey).Id;
            Script(session, name);
            var run = new Run();
            run.Hashes.Add(session.StateHash);
            while (session.Tick < LastTick && !session.IsFinished)
            {
                session.StepOnce();
                run.Hashes.Add(session.StateHash);
                run.Fires += combat.Fires.Length;
                foreach (FireRecord fire in combat.Fires)
                    if (fire.Shooter.Equals(player)) run.PlayerFires++;
                foreach (EntityId unit in sim.Entities.SnapshotEntitiesForTests())
                    if (combat.CanBlock(unit) && combat.IsHolding(unit)) { run.HoldingTicks++; break; }
            }
            foreach (EntityId unit in sim.Entities.SnapshotEntitiesForTests())
                if (sim.Entities.TryGet(sim.Entities.Resolve<Combatant>(), unit, out _) && combat.IsDead(unit)) run.Deaths++;
            run.FinalTick = session.Tick;
            run.Finished = session.IsFinished;
            return run;
        }

        internal static Dictionary<string, ulong[]> Golden()
        {
            var result = new Dictionary<string, List<ulong>>();
            foreach (string line in File.ReadAllLines(GoldenPath))
            {
                string[] parts = line.Split('|');
                string key = parts[0] + "|" + parts[1];
                if (!result.TryGetValue(key, out List<ulong> list)) result[key] = list = new List<ulong>();
                Assert.That(int.Parse(parts[2]), Is.EqualTo(list.Count), line);
                list.Add(Convert.ToUInt64(parts[3], 16));
            }
            return result.ToDictionary(p => p.Key, p => p.Value.ToArray());
        }

        // SimSession's public surface: one line per member, "name|kind|signature", sorted by ordinal comparison.
        internal static string[] PublicSurface()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var lines = new List<string>();
            foreach (MemberInfo member in typeof(SimSession).GetMembers(flags))
            {
                switch (member)
                {
                    case ConstructorInfo c: lines.Add(c.Name + "|ctor|(" + Params(c.GetParameters()) + ")"); break;
                    case MethodInfo m: lines.Add(m.Name + "|method|" + N(m.ReturnType) + "(" + Params(m.GetParameters()) + ")" + (m.IsStatic ? " static" : "")); break;
                    case PropertyInfo p:
                        lines.Add(p.Name + "|property|" + N(p.PropertyType) + (p.GetMethod != null && p.GetMethod.IsPublic ? " get" : "")
                            + (p.SetMethod != null && p.SetMethod.IsPublic ? " set" : "")); break;
                    case FieldInfo f: lines.Add(f.Name + "|field|" + N(f.FieldType) + (f.IsLiteral ? " const" : f.IsStatic ? " static" : "")); break;
                    case EventInfo e: lines.Add(e.Name + "|event|" + N(e.EventHandlerType)); break;
                    default: lines.Add(member.Name + "|other|" + member.MemberType); break;
                }
            }
            lines.Sort(StringComparer.Ordinal);
            return lines.ToArray();
        }

        private static string Params(ParameterInfo[] parameters) =>
            string.Join(",", parameters.Select(p => N(p.ParameterType) + (p.IsOut ? " out" : "") + (p.HasDefaultValue ? "=" + (p.DefaultValue ?? "null") : "")));

        // Namespace-qualified names with generic arguments spelled out, free of assembly versions, so the runtime does not matter.
        private static string N(Type type)
        {
            if (type.IsByRef) return N(type.GetElementType()) + "&";
            if (type.IsArray) return N(type.GetElementType()) + "[]";
            if (!type.IsGenericType) return type.Namespace + "." + type.Name;
            string name = type.Name.Substring(0, type.Name.IndexOf('`'));
            return type.Namespace + "." + name + "<" + string.Join(",", type.GetGenericArguments().Select(N)) + ">";
        }
    }

    // Writes Golden/f2b-ticks.txt from the builder as it is (WARCORE_F2B_OUT names the output file; nothing is overwritten) and
    // prints the values the F2b tests keep as constants. Run once on the unmodified builder.
    public sealed class F2bRecorder
    {
        [Category("Manual"), Test, Explicit("Records the F2b per-tick lock.")]
        public void RecordF2bLock()
        {
            string path = Environment.GetEnvironmentVariable("WARCORE_F2B_OUT");
            Assert.That(string.IsNullOrEmpty(path), Is.False);
            Assert.That(File.Exists(path), Is.False);
            var lines = new List<string>();
            foreach (string name in F2bWorlds.Names)
                foreach (bool reverse in new[] { false, true })
                {
                    F2bWorlds.Run run = F2bWorlds.Play(name, reverse);
                    for (int t = 0; t < run.Hashes.Count; t++)
                        lines.Add(name + "|" + (reverse ? "reverse" : "forward") + "|" + t + "|" + run.Hashes[t].ToString("X16"));
                    TestContext.Out.WriteLine(name + " reverse=" + reverse + " tick=" + run.FinalTick + " finished=" + run.Finished
                        + " fires=" + run.Fires + " playerFires=" + run.PlayerFires + " holding=" + run.HoldingTicks + " deaths=" + run.Deaths);
                }
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            foreach (string line in F2bWorlds.PublicSurface()) TestContext.Out.WriteLine("SURFACE " + line);
            foreach (string line in F2bHookProbe.Record()) TestContext.Out.WriteLine("HOOK " + line);
            TestContext.Out.WriteLine("FIRES60 " + F2bFireProbe.PlainFires());
            TestContext.Out.WriteLine("FACING " + F2bFireProbe.PlayerFacing());
        }
    }
}
