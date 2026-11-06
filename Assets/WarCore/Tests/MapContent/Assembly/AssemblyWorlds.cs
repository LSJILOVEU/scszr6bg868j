using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // F2a assembly lock fixtures: every world is built through WorldBuilder.Build (reflection, as the other world fixtures do)
    // and described by values that do not depend on how the builder is organised inside: snapshot bytes, the component and
    // system manifests, and the order in which entities were created.
    internal static class AssemblyWorlds
    {
        internal static readonly string[] Names =
        {
            "M2b", "M3", "M4", "M5", "M6", "M7", "MeleeDuel", "MpDemo", "Terrain", "FacingMotionStanding", "BlockAi",
        };

        // Seed, units, map and pack of these three are written out here and inherit nothing from DemoRecipes.
        private static readonly UnitSpawn[] FmsUnits =
        {
            new UnitSpawn(1, 0, 1, 3500, 3500, 4000),
            new UnitSpawn(2, 0, 1, 3500, 6500, 4000),
            new UnitSpawn(3, 0, 2, 30500, 3500, 4000),
            new UnitSpawn(4, 0, 3, 20500, 20500, 4000),
            new UnitSpawn(5, 0, 3, 22500, 20500, 4000),
        };

        private static readonly BridgeCommand[] FmsScript =
        {
            BridgeCommand.MoveTo(1, 1, 20500, 3500), BridgeCommand.MoveTo(1, 2, 3500, 15500),
            BridgeCommand.Hold(5, 3), BridgeCommand.Follow(8, 2, 1),
        };

        internal static byte[] FmsMap() => WcMapWriter.Write(M3TestWorld.Boxes(40, 24, (14, 8, 2, 8)));

        internal static WorldRecipe FmsRecipe() =>
            new WorldRecipe(41, MoverKind.InterimCrowd, FmsUnits, FmsScript).WithFacing().WithMotion().WithStandingOrders();

        private sealed class FixedHit : IHitResolver
        {
            public ulong PolicyKey => 0x4632410000000001;
            public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => maxHealthRaw / 20;
        }

        internal static CombatPack BlockPack() =>
            new CombatPack(Array.Empty<WeaponDef>(), new FixedHit(), new NearestVisibleEnemy(), 100 * SimFix.Scale, false, false, 0, 30000,
                new[]
                {
                    new MeleeSpec(6, 2, 9, SimFix.FromInt(2), 500, SimFix.FromInt(1), SimFix.FromInt(10), 3, SimFix.Ratio(18, 10)),
                },
                new[] { new BlockSpec(500, 1000, 1000) });

        internal static WorldRecipe BlockAiRecipe() =>
            new WorldRecipe(41, MoverKind.InterimCrowd,
                new[]
                {
                    // Unit 2 faces +Z until something turns it, so the swing from unit 1 comes from the front.
                    new UnitSpawn(1, 0, 1, 20000, 21000, 3000, 1, -1, false, 0),
                    new UnitSpawn(2, 0, 2, 20000, 20000, 3000, 2, -1, false, -1, 0),
                    new UnitSpawn(3, 0, 2, 26000, 26000, 3000, 2, -1, false, -1, 0),
                },
                Array.Empty<BridgeCommand>()).WithCombat(BlockPack()).WithFacing().WithBlockAi();

        internal static byte[] PlainMap(int width = 80, int depth = 40) => WcMapWriter.Write(M3TestWorld.Boxes(width, depth));

        internal static (WorldRecipe Recipe, byte[] Map) Make(string name)
        {
            switch (name)
            {
                case "M2b": return (DemoRecipes.M2b(), DemoRecipes.M2bMapBytes());
                case "M3": return (DemoRecipes.M3(), DemoRecipes.M2bMapBytes());
                case "M4": return (DemoRecipes.M4(), DemoRecipes.M2bMapBytes());
                case "M5": return (DemoRecipes.M5(), DemoRecipes.M2bMapBytes());
                case "M6": return (DemoRecipes.M6(), DemoRecipes.M6MapBytes());
                case "M7": return (DemoRecipes.M7(), DemoRecipes.M7MapBytes());
                case "MeleeDuel": return (DemoRecipes.MeleeDuel(), PlainMap());
                case "MpDemo": return (DemoRecipes.MpDemo(), DemoRecipes.M7MapBytes());
                case "Terrain":
                    return (new WorldRecipe(41, MoverKind.InterimCrowd,
                        new[]
                        {
                            new UnitSpawn(1, 0, 1, 3500, 16500, 4000), new UnitSpawn(2, 0, 1, 3500, 15500, 4000),
                            new UnitSpawn(3, 0, 2, 44500, 16500, 4000),
                        },
                        new[] { BridgeCommand.MoveTo(1, 1, 22500, 16500), BridgeCommand.MoveTo(1, 2, 20500, 16500) })
                        .WithTerrainRules(Wp23.Pack()), WcMapWriter.Write(TerrainDemoMap.Build()));
                case "FacingMotionStanding": return (FmsRecipe(), FmsMap());
                case "BlockAi": return (BlockAiRecipe(), PlainMap());
                default: throw new ArgumentException("Unknown world " + name, nameof(name));
            }
        }

        internal static SimSession Build(WorldRecipe recipe, byte[] map, bool reverse)
        {
            try
            {
                return (SimSession)typeof(WorldBuilder).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { recipe, map, reverse });
            }
            catch (TargetInvocationException e)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        internal static string Sha(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        private static object Field(object owner, string name) =>
            owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner);

        private static object Prop(object owner, string name) => owner.GetType().GetProperty(name).GetValue(owner);

        // Component id, serializer type id and serializer version, in the store's own (sorted) order.
        internal static string Components(Simulation sim)
        {
            var parts = new List<string>();
            foreach (object storage in (IEnumerable)Field(sim.Entities, "storages"))
                parts.Add(Prop(storage, "Name") + ":" + Prop(storage, "SerializerId") + ":" + Prop(storage, "SerializerVersion"));
            return string.Join(";", parts);
        }

        // System id, phase and order, in execution order.
        internal static string Systems(Simulation sim)
        {
            var parts = new List<string>();
            foreach (object entry in (IEnumerable)Field(sim, "systems"))
                parts.Add(Field(entry, "Id") + "|" + (int)Field(entry, "Phase") + "|" + Field(entry, "Order"));
            return string.Join(";", parts);
        }

        // Stable identities in physical slot order, unsorted: this is the creation order the snapshot (sorted by identity) hides.
        internal static string Creation(Simulation sim)
        {
            var ids = new StringBuilder();
            int count = 0;
            foreach (EntityId entity in sim.Entities.SnapshotEntitiesForTests())
            {
                ids.Append(sim.Entities.StableOf(entity).Value).Append(',');
                count++;
            }
            return count + ":" + Sha(Encoding.ASCII.GetBytes(ids.ToString()));
        }

        internal static Dictionary<string, string> Describe(string name)
        {
            (WorldRecipe recipe, byte[] map) = Make(name);
            var result = new Dictionary<string, string>();
            SimSession session = Build(recipe, map, false);
            Simulation sim = M3TestWorld.Sim(session);
            result["snap0"] = Sha(sim.SaveRetainedSnapshot());
            result["components"] = Components(sim);
            result["systems"] = Systems(sim);
            result["creation"] = Creation(sim);
            session.StepOnce();
            result["snap1"] = Sha(sim.SaveRetainedSnapshot());
            while (session.Tick < 100) session.StepOnce();
            result["snap100"] = Sha(sim.SaveRetainedSnapshot());
            (WorldRecipe again, byte[] mapAgain) = Make(name);
            Simulation reverse = M3TestWorld.Sim(Build(again, mapAgain, true));
            result["snapReverse0"] = Sha(reverse.SaveRetainedSnapshot());
            result["creationReverse"] = Creation(reverse);
            return result;
        }
    }
}
