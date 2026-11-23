using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using WarCore.Tests.Stance;

namespace WarCore.Tests.Scale
{
    // HC-6: a test body. Every height and the radius of the reference body and its stances (read from the shipped layer
    // rwr-stance, never written here) are multiplied by Num / Den, rounded down: Num is the target standing height and Den the
    // reference one, both raw. The reference itself has Num == Den. RadiusWanted is the scaled radius; the body carries it
    // clamped to what a body may declare (BodyRadius.MinRaw to MaxRaw), so a radius the base cannot hold shows as a gap.
    internal sealed class ProbeBody
    {
        internal readonly string Name;
        internal readonly long Num, Den;
        internal readonly CombatBody Body;
        internal readonly StanceDef[] Stances;
        internal readonly long RadiusWanted;

        internal ProbeBody(string name, long num, long den, CombatBody body, StanceDef[] stances, long radiusWanted)
        {
            Name = name; Num = num; Den = den; Body = body; Stances = stances; RadiusWanted = radiusWanted;
        }

        // A raw length of the reference body scaled to this body (positive integer division rounds down).
        internal long Scale(long raw) => raw * Num / Den;

        internal bool IsReference => Num == Den;
    }

    internal static class ProbeBodies
    {
        internal const long M = SimFix.Scale;
        internal const string ReferenceLayer = "rwr-stance";
        internal const string GiantName = "giant", TinyName = "tiny", ReferenceName = "reference";
        // The two target standing heights, written only here: a 3 m giant and a 0.5 m tiny unit.
        internal const long GiantStandingRaw = 3 * M, TinyStandingRaw = M / 2;

        private static CombatPack shipped;
        private static ProbeBody reference, giant, tiny;

        // The shipped body and stances everything is scaled from.
        internal static CombatPack Shipped => shipped ??= RwrPack.Load(ReferenceLayer).BuildCombat();

        internal static ProbeBody Reference => reference ??= Make(ReferenceName, Shipped.Body.Value.Height.Raw);
        internal static ProbeBody Giant => giant ??= Make(GiantName, GiantStandingRaw);
        internal static ProbeBody Tiny => tiny ??= Make(TinyName, TinyStandingRaw);

        // The giant and the tiny unit, in the order the ledger lists them.
        internal static ProbeBody[] Scaled => new[] { Giant, Tiny };

        // The body whose standing height is targetRaw: the reference body and every stance scaled by targetRaw / reference.
        internal static ProbeBody Make(string name, long targetRaw)
        {
            CombatPack pack = Shipped;
            CombatBody body = pack.Body.Value;
            long num = targetRaw, den = body.Height.Raw;
            long radiusWanted = body.Radius.Raw * num / den;
            SimFix radius = SimFix.FromRaw(Math.Min(Math.Max(radiusWanted, BodyRadius.MinRaw), BodyRadius.MaxRaw));
            var stances = new StanceDef[pack.Stances.Length];
            for (int i = 0; i < stances.Length; i++)
            {
                StanceDef s = pack.Stances[i];
                stances[i] = new StanceDef(s.Id, ScaleBody(s.Body, num, den, radius), s.SpeedPermille, s.SpreadPermille, s.Ticks, s.Deny);
            }
            return new ProbeBody(name, num, den, ScaleBody(body, num, den, radius), stances, radiusWanted);
        }

        private static CombatBody ScaleBody(CombatBody b, long num, long den, SimFix radius) =>
            new CombatBody(SimFix.FromRaw(b.Height.Raw * num / den), SimFix.FromRaw(b.Eye.Raw * num / den), SimFix.FromRaw(b.Muzzle.Raw * num / den), radius);

        // The body of stance level n (0 = standing).
        internal static CombatBody Level(ProbeBody b, int level) => level == 0 ? b.Body : b.Stances[level - 1].Body;

        // ---- the reader path: an in-memory layer over rwr-stance carrying the body and its stances ----

        // UnitAction bit i is ActionNames[i], as the combat section reads "deny".
        private static readonly string[] ActionNames =
            { "move", "dash", "traverse", "fire", "melee", "block", "throw", "reload", "stance", "interact", "capture" };

        internal static string LayerId(ProbeBody b) => "hc6-" + b.Name;

        internal static string LayerJson(ProbeBody b)
        {
            var json = new StringBuilder();
            json.Append("{ \"id\": \"").Append(LayerId(b)).Append("\", \"extends\": \"").Append(ReferenceLayer).Append("\", \"combat\": { ");
            json.Append("\"body\": { ").Append(Heights(b.Body)).Append(", \"radius\": ").Append(Metres(b.Body.Radius.Raw)).Append(" }, \"stances\": [");
            for (int i = 0; i < b.Stances.Length; i++)
            {
                StanceDef s = b.Stances[i];
                json.Append(i == 0 ? " " : ", ").Append("{ \"id\": \"").Append(s.Id).Append("\", ").Append(Heights(s.Body));
                json.Append(", \"speedPermille\": ").Append(s.SpeedPermille.ToString(CultureInfo.InvariantCulture));
                json.Append(", \"spreadPermille\": ").Append(s.SpreadPermille.ToString(CultureInfo.InvariantCulture));
                json.Append(", \"ticks\": ").Append(s.Ticks.ToString(CultureInfo.InvariantCulture)).Append(", \"deny\": [");
                bool first = true;
                for (int bit = 0; bit < ActionNames.Length; bit++)
                    if (((int)s.Deny & (1 << bit)) != 0) { json.Append(first ? "\"" : ", \"").Append(ActionNames[bit]).Append('"'); first = false; }
                json.Append("] }");
            }
            json.Append(" ] } }");
            return json.ToString();
        }

        private static string Heights(CombatBody b) =>
            "\"height\": " + Metres(b.Height.Raw) + ", \"eye\": " + Metres(b.Eye.Raw) + ", \"muzzle\": " + Metres(b.Muzzle.Raw);

        // A raw length as a decimal with 10 places, rounded up, so the reader (which truncates) gives the same raw back.
        internal static string Metres(long raw)
        {
            const long Places = 10000000000L;
            long scaled = (raw * Places + M - 1) / M;
            return (scaled / Places).ToString(CultureInfo.InvariantCulture) + "." + (scaled % Places).ToString("D10", CultureInfo.InvariantCulture);
        }

        // Loads the body's layer (its content pack, and the combat pack built from it); a refused layer throws ContentException.
        internal static ContentPack LoadContent(ProbeBody b)
        {
            string id = LayerId(b);
            return ContentLoader.Load(new Trv2Maps.Source((id, LayerJson(b))), id);
        }

        internal static CombatPack LoadLayer(ProbeBody b) => LoadContent(b).BuildCombat();

        // ---- the direct path: a combat pack made with the public constructor (as Stance1Worlds.Exact) ----

        internal static CombatPack DirectPack(ProbeBody b, MeleeSpec[] melee = null) =>
            new CombatPack(new[] { WarCore.Tests.Projectile.Proj1World.Weapon() }, new WarCore.Tests.Projectile.FalloffDamage(),
                new NearestVisibleEnemy(), 100 * M, melee: melee, body: b.Body, stances: b.Stances);

        // Two enemies on the STANCE-1 field (40 x 16 m): unit 1 at (10.5, 8) m, unit 2 at (20.5, 8) m, both standing.
        internal const ulong A = 1, B = 2;

        internal static SimSession Field(CombatPack pack, params Stance1Worlds.Box[] walls) =>
            Stance1Worlds.World(pack, new[] { Stance1Worlds.Soldier(A, 1, 10500, 8000, 1), Stance1Worlds.Soldier(B, 2, 20500, 8000, 2) },
                Stance1Worlds.Map(walls));

        // A match on the flat 80 x 40 m map, as WorldScaleTests.Match: the player and an enemy deployed at the start and a
        // reserve of the player's team waiting, with the body's combat pack.
        internal const ulong Player = 1, Enemy = 10, Reserve = 20;

        internal static M6World Match(ProbeBody b)
        {
            MatchPack match = RespawnWorld.Pack(RespawnWorld.Points(RespawnWorld.Spawn(1, 30, 10)),
                RespawnWorld.Bases(new SpawnAnchor(1, RespawnWorld.P(5, 38))), 300);
            var units = new[]
            {
                new UnitSpawn(Player, 0, 1, 20000, 20000, 3000, 1, -1), new UnitSpawn(Enemy, 0, 2, 78000, 38000, 3000, 2, -1),
                new UnitSpawn(Reserve, 0, 1, 30000, 10000, 3000, 1, -1, true)
            };
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithMatch(match).WithCombat(DirectPack(b)).WithCrowdPass(CrowdPassKind.TeamIdle).WithPlayer(Player);
            return new M6World(recipe);
        }

        // Deploys the reserve through the real deployer, as a reinforcement does.
        internal static void Reinforce(M6World w)
        {
            var deployer = Ml7Worlds.Field<IUnitDeployer>(w.Reinforce, "deployer");
            if (!deployer.TryDeploy(w.Unit(Reserve), RespawnWorld.P(30, 10))) throw new InvalidOperationException("the reserve was not deployed");
        }

    }
}
