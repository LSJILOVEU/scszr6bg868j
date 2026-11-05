using System;
using System.Linq;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    internal static class M2MapFixture
    {
        internal const string GoldenHex = 
            "57434D5002000B0001004600000001006D01006300000000000000000000000000000000000000000000000000000800" +
            "0000000000000400000000000000040000000000000008000000000000000100000000008C7FBAE605C767D2DF6CD264" +
            "23E01CBF25ABBA068219096FA9296FA558772ADD02000A0000000100050077616C6C730168343186E377A43802EEE895" +
            "126A54F81BF9E32A395745264FFE1DEC429235EA0300120000000000000000000000000000000000000000002E3F4F9C" +
            "C0627174DA553A1C1685ED41A62F401CB81D70C6B75A971799C83ACE04005A0000000100000000000000000000000100" +
            "000000000000040077616C6C050077616C6C730100800400000000000000010000000000000002000000000000800000" +
            "0000000000000200000000000000010000000000000000000000000055EAD37A244FB866150A5196B1FC405569240F04" +
            "687306736024E90580F140D00500000000007E81C8A995B00016A0DBE585E59C0486856725527C94FE2B71B9A3AB1F64" +
            "EEB50600020000000000E28E9D10F2438D8E331F101AE8C073FBB0F00D2590157475B99FDA53C3FA44B7070013000000" +
            "010800000004000000010004000000FFEFEFFF3B57758F5A602702261C686DC58CA7006167123D287CC6E7777FC39078" +
            "23D4A30800000000002C570CC065BBB1010B10D2935B2F11BD01B1C250E19E4CA9CD8B5DFB4A26C92C090000000000DE" +
            "2AEB86DE0B0D8AB2D688DA5642FA03C5E8C4402A5FD21543718D6CB8B4097D0A00440000000100000000000000000000" +
            "00000000000000000000000000000000000000000000000000040000000300A40000000400DC00000006008201000007" +
            "00AA0100000000000077A3D34C0AD4B8557B926F2CC3CFF3C522776165660506AAE8A39FAD6647C95E0B00000000003B" +
            "11A81F0A2AA56E3C408CF6F645ACD78E096545895D060DEF992E9593E559D6DA7CF438D1DE5378CC49C227CEC6A8B721" +
            "A57542CDCB9C895564DF68F8B08C95";
        internal static byte[] GoldenBytes => Enumerable.Range(0, GoldenHex.Length / 2)
            .Select(i => Convert.ToByte(GoldenHex.Substring(i * 2, 2), 16)).ToArray();

        internal static WcMap Model(bool withWall = true)
        {
            var wall = new WcMapWall(0, 1, "wall", "walls", new SimVec3(SimFix.Ratio(9, 2), SimFix.FromInt(1), SimFix.FromInt(2)),
                SimFix.Ratio(1, 2), SimFix.FromInt(2), SimFix.FromInt(1));
            return new WcMap("m", "c", default, SimVec3.Metres(8, 4, 4), SimFix.FromInt(8), default, 8, 4,
                new[] { new WcMapLayer("walls", 1) }, withWall ? new[] { wall } : Array.Empty<WcMapWall>(),
                withWall ? new byte[] { 255, 239, 239, 255 } : new byte[] { 255, 255, 255, 255 });
        }

        internal static WcMap Copy(WcMap source, WcMapWall[] walls = null, WcMapLayer[] layers = null, byte[] bits = null)
            => new WcMap(source.MapId, source.ContentId, source.Min, source.Max, source.ChunkSize,
                source.FlatHeight, source.Width, source.Depth, layers ?? source.Layers.ToArray(),
                walls ?? source.Walls.ToArray(), bits ?? source.Passability.ToArray());

        internal static SimVec3 Point(long x, long z) => new SimVec3(SimFix.FromInt(x), SimFix.Ratio(1, 2), SimFix.FromInt(z));

        // This is the only map-to-world assembly path in M2a; production WorldBuilder stays unchanged.
        internal sealed class World
        {
            internal readonly Simulation Sim;
            internal readonly WcMap Map;
            internal readonly EntityId Unit;
            internal readonly InterimLocomotion Locomotion;
            internal readonly ComponentType<Position> Positions;
            internal readonly ComponentType<MoveIntent> Intents;
            internal readonly SimFix Radius;
            internal readonly SimFix Step;
            internal SimVec3 Position => Sim.Entities.Get(Positions, Unit).Value;
            internal bool Active => Sim.Entities.Get(Intents, Unit).Active;

            internal World(byte[] bytes, SimVec3 start, long speed = 3, uint? wallMask = null, SimFix? radius = null)
            {
                Map = WcMapReader.Read(bytes);
                Radius = radius ?? SimFix.Ratio(1, 4);
                Step = SimFix.Ratio(speed, SimClock.TickRate);
                var content = new StableContentManifest(1);
                var entries = Map.Walls.Select(w => new PlacedCatalogEntry(w.Chunk, w.Placement)).ToList();
                entries.Add(new PlacedCatalogEntry(0, ulong.MaxValue));
                Sim = Simulation.CreateRetainedWorld(41, new PlacedCatalog(content, entries.ToArray()), content: content);
                Positions = Sim.Entities.Register<Position>("interim.position", new PositionSerializer());
                var colliders = Sim.Entities.Register<Collider>("interim.collider", new ColliderSerializer());
                Intents = Sim.Entities.Register<MoveIntent>(MoveIntent.ComponentId, new MoveIntentSerializer());
                Sim.ConfigureSpatial(Positions, colliders, SimFix.FromInt(2));
                Locomotion = new InterimLocomotion(Sim.Entities, wallMask ?? Map.LayerMask("walls"));
                Sim.RegisterGameplay("interim.orders", new InterimOrders(Sim.Entities, Locomotion), SimSystemPhase.Requests);
                Sim.RegisterGameplay("interim.locomotion", Locomotion, SimSystemPhase.Movement);
                foreach (WcMapWall wall in Map.Walls)
                {
                    EntityId entity = Sim.Entities.Create(wall.Key);
                    var origin = new SimVec3(Map.Min.X, Map.FlatHeight, Map.Min.Z);
                    Sim.Entities.Set(Positions, entity, new Position(origin + wall.LocalCenter));
                    Sim.Entities.Set(colliders, entity, Collider.Box(wall.HalfX, wall.Height, wall.HalfZ,
                        BoxYaw.Identity, layer: Map.LayerMask(wall.LayerName), isStatic: true));
                }
                Unit = Sim.Entities.Create(StableEntityKey.Placed(0, ulong.MaxValue));
                Sim.Entities.Set(Positions, Unit, new Position(start));
                Sim.Entities.Set(colliders, Unit, new Collider(Radius, SimFix.FromInt(1), layer: 1u));
                Sim.Entities.Set(Intents, Unit, new MoveIntent(start, Step, 1, false));
                Sim.CompleteSpatialBoundary();
            }

            internal void Tick(int tick, SimVec3 target)
            {
                var input = tick == 1 ? new InputFrame(default, default,
                    squad: new SquadCommand(SquadCommandKind.MoveTo, 1, target, default)) : default;
                Sim.Step(input);
            }
        }
    }
}
