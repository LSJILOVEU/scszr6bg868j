using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using WarCore.Tests.Stance;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests.Scale
{
    internal enum Verdict { Pass, Gap, Threw, NotApplicable }

    internal sealed class ProbeResult
    {
        internal readonly Verdict Verdict;
        internal readonly string Expected, Actual, Note;
        internal ProbeResult(Verdict verdict, string expected, string actual, string note)
        {
            Verdict = verdict; Expected = expected; Actual = actual; Note = note;
        }
        internal bool IsGap => Verdict == Verdict.Gap || Verdict == Verdict.Threw;
    }

    // HC-6: one thing that should follow the body. A ratio probe measures raw lengths (Measure) and expects the reference
    // body's measurement scaled by the body's Num / Den (or, with Want, values read from the body or the world); every part
    // within Tolerance raw units passes. A custom probe judges by itself. Any exception the probe raises is caught and becomes
    // Threw: a probe that crashes is a gap, and the next probe still runs in its own world.
    internal sealed class Probe
    {
        internal readonly string Id, Family, What, Method, Where;
        internal readonly string[] Parts;
        internal readonly long Tolerance;
        private readonly Func<ProbeBody, long[]> measure, want;
        private readonly Func<ProbeBody, ProbeResult> judge;

        internal Probe(string id, string family, string what, string method, string where, string[] parts, long tolerance,
            Func<ProbeBody, long[]> measure, Func<ProbeBody, long[]> want = null)
        {
            Id = id; Family = family; What = what; Method = method; Where = where; Parts = parts; Tolerance = tolerance;
            this.measure = measure; this.want = want;
        }

        internal Probe(string id, string family, string what, string method, string where, Func<ProbeBody, ProbeResult> judge)
        {
            Id = id; Family = family; What = what; Method = method; Where = where; Parts = new string[0]; this.judge = judge;
        }

        internal ProbeResult Run(ProbeBody body)
        {
            try
            {
                return judge != null ? judge(body) : Ratio(body);
            }
            catch (Exception e)
            {
                while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
                return new ProbeResult(Verdict.Threw, "-", "-", "threw " + e.GetType().Name);
            }
        }

        // The measurement of the reference body, scaled to this one.
        internal long[] Expected(ProbeBody body)
        {
            if (want != null) return want(body);
            long[] reference = measure(ProbeBodies.Reference);
            var scaled = new long[reference.Length];
            for (int i = 0; i < reference.Length; i++) scaled[i] = body.Scale(reference[i]);
            return scaled;
        }

        private ProbeResult Ratio(ProbeBody body)
        {
            long[] expected = Expected(body), actual = measure(body);
            bool pass = actual.Length == expected.Length;
            var off = new List<string>();
            for (int i = 0; pass && i < actual.Length; i++)
                if (!BodyProbes.Within(actual[i], expected[i], Tolerance))
                    off.Add((Parts[i].Length > 0 ? Parts[i] + " " : "") + BodyProbes.Metres(actual[i]) + " m, want " + BodyProbes.Metres(expected[i]));
            if (actual.Length != expected.Length) off.Add(actual.Length + " parts, want " + expected.Length);
            return new ProbeResult(off.Count == 0 ? Verdict.Pass : Verdict.Gap, Show(expected), Show(actual), string.Join("; ", off));
        }

        private string Show(long[] values)
        {
            var cells = new List<string>();
            for (int i = 0; i < values.Length; i++) cells.Add((i < Parts.Length && Parts[i].Length > 0 ? Parts[i] + " " : "") + BodyProbes.Metres(values[i]));
            return string.Join("; ", cells);
        }
    }

    internal static class BodyProbes
    {
        internal const long M = SimFix.Scale;
        // Raw values compared exactly up to the rounding of one scaling; float values (the picture) after rounding to raw.
        internal const long RawTolerance = 1, FloatTolerance = 2;

        internal static bool Within(long actual, long expected, long tolerance) => Math.Abs(actual - expected) <= tolerance;

        internal static string Metres(long raw) => (raw / (double)M).ToString("0.0000", CultureInfo.InvariantCulture);

        internal static long Raw(float metres) => (long)Math.Round(metres * (double)M, MidpointRounding.AwayFromZero);

        internal static long RawOfMm(long mm) => SimFix.Ratio(mm, 1000).Raw;

        internal static ProbeResult Pass(string expected, string actual) => new ProbeResult(Verdict.Pass, expected, actual, "");
        internal static ProbeResult Gap(string expected, string actual, string note) => new ProbeResult(Verdict.Gap, expected, actual, note);
        internal static ProbeResult NotApplicable(string why) => new ProbeResult(Verdict.NotApplicable, "-", "-", why);

        private static readonly string[] One = { "" };
        private static readonly string[] Levels = { "stand", "crouch", "prone" };

        // ---- B01, B02, B18: the reader path ----

        // Loads the body's layer; null when it is accepted, else the refusal's message (shown in the report, never compared).
        private static string Refusal(ProbeBody b)
        {
            try { ProbeBodies.LoadLayer(b); return null; }
            catch (ContentException e) { return e.Message; }
        }

        // B01: the reader's ceiling comes from the world's headroom: a layer whose world fits the body is accepted. No content
        // field sets the headroom today, so the giant's layer cannot make room for it (HC-2b changes this scenario, then
        // deletes the row). The tiny unit is under the ceiling: B02 covers it.
        private static ProbeResult B01(ProbeBody b)
        {
            if (b.Body.Height.Raw < ProbeBodies.Reference.Body.Height.Raw) return NotApplicable("below the headroom; B02 covers it");
            string refused = Refusal(b);
            return refused == null ? Pass("accepted", "accepted") : Gap("accepted", "refused: " + refused, "the reader caps the height at a headroom no layer can raise");
        }

        // B02: every lower bound of the reader (height, eye, stance heights) comes from the body's own width.
        private static ProbeResult B02(ProbeBody b)
        {
            if (b.Body.Height.Raw > ProbeBodies.Reference.Body.Height.Raw) return NotApplicable("above the reference; B01 covers the reader");
            string refused = Refusal(b);
            return refused == null ? Pass("accepted", "accepted") : Gap("accepted", "refused: " + refused, "the reader's lower bounds use a fixed radius");
        }

        // B18: a body taller than the world's headroom is refused (world sizes stay as they are, only the ratio is checked);
        // a body that fits is accepted.
        private static ProbeResult B18(ProbeBody b)
        {
            if (b.Body.Height.Raw < ProbeBodies.Reference.Body.Height.Raw) return NotApplicable("a smaller body always fits the headroom");
            bool fits = b.Body.Height.Raw <= WallSpan.ClearanceRaw;
            string refused = Refusal(b);
            string want = fits ? "accepted" : "refused", got = refused == null ? "accepted" : "refused";
            string shown = refused == null ? got : got + ": " + refused;
            return want == got ? Pass(want, shown) : Gap(want, shown, fits ? "a body under the headroom is refused" : "a body over the headroom is accepted");
        }

        // ---- worlds of the direct path ----

        private static long[] StartRadius(ProbeBody b) => new[] { ProbeBodies.Match(b).Read<Collider>(ProbeBodies.Player).Radius.Raw };

        private static long[] ReinforcedRadius(ProbeBody b)
        {
            M6World w = ProbeBodies.Match(b);
            ProbeBodies.Reinforce(w);
            return new[] { w.Read<Collider>(ProbeBodies.Reserve).Radius.Raw };
        }

        // B04: the clearance radius the match's mover (the one the deployer hands units to) tests every step with.
        private static long[] MoverFootprint(ProbeBody b)
        {
            M6World w = ProbeBodies.Match(b);
            var deployer = Ml7Worlds.Field<IUnitDeployer>(w.Reinforce, "deployer");
            var mover = Ml7Worlds.Field<InterimLocomotion>(deployer, "locomotion");
            return new[] { Ml7Worlds.Field<SimFix>(mover, "footprint").Raw };
        }

        // B22: the map gate stands units with the loaded pack's unit radius; it should be the reference radius scaled. A layer
        // the reader refuses (B01) never reaches the gate: a gap, not a crash.
        private static ProbeResult GateFootprint(ProbeBody b)
        {
            long want = b.Scale(ProbeBodies.LoadContent(ProbeBodies.Reference).UnitRadius.Raw);
            ContentPack pack;
            try { pack = ProbeBodies.LoadContent(b); }
            catch (ContentException) { return Gap(Metres(want), "refused", "the layer is refused (B01), the gate never sees this body"); }
            long got = pack.UnitRadius.Raw;
            return Within(got, want, RawTolerance) ? Pass(Metres(want), Metres(got)) : Gap(Metres(want), Metres(got), Metres(got) + " m, want " + Metres(want));
        }

        // B05: the deployer's ground (its DeployGround) on the flat match map: the least distance from the map's west edge at
        // which a unit may stand, found by bisection on raw x at z = 20 m.
        private static long[] DeployReach(ProbeBody b)
        {
            M6World w = ProbeBodies.Match(b);
            var deployer = Ml7Worlds.Field<IUnitDeployer>(w.Reinforce, "deployer");
            var ground = Ml7Worlds.Field<DeployGround>(deployer, "ground");
            long low = 0, high = 4 * M; // CanStand(low) is false at the edge itself, CanStand(high) true
            if (ground.CanStand(Point(low, 20 * M)) || !ground.CanStand(Point(high, 20 * M))) throw new InvalidOperationException("no edge to bisect");
            while (high - low > 1)
            {
                long mid = (low + high) / 2;
                if (ground.CanStand(Point(mid, 20 * M))) high = mid; else low = mid;
            }
            return new[] { high };
        }

        private static SimVec3 Point(long xRaw, long zRaw) => new SimVec3(SimFix.FromRaw(xRaw), default, SimFix.FromRaw(zRaw));

        // B06: the collider's height at each stance level: the spatial index's body of the unit after the stance is forced and
        // the world has taken one step (one new world per level).
        private static long[] ColliderHeights(ProbeBody b)
        {
            var heights = new long[b.Stances.Length + 1];
            for (int level = 0; level < heights.Length; level++)
            {
                SimSession s = ProbeBodies.Field(ProbeBodies.DirectPack(b));
                Simulation sim = Stance1Worlds.SimOf(s);
                EntityId unit = Stance1Worlds.Unit(s, ProbeBodies.A);
                Collider baseline = sim.Entities.Read(sim.Entities.Resolve<Collider>(), unit);
                if (level > 0) Stance1Worlds.Force(s, ProbeBodies.A, level);
                s.StepOnce();
                SimVec3 at = sim.Entities.Read(sim.Entities.Resolve<Position>(), unit).Value;
                heights[level] = sim.SpatialIndex.Postures.Body(unit, at, baseline).Collider.Height.Raw;
            }
            return heights;
        }

        // B07: where shots start over the foot, standing and crouched (one new world per level).
        private static long[] MuzzleHeights(ProbeBody b)
        {
            var heights = new long[2];
            for (int level = 0; level < heights.Length; level++)
            {
                SimSession s = ProbeBodies.Field(ProbeBodies.DirectPack(b));
                if (level > 0) Stance1Worlds.Force(s, ProbeBodies.A, level);
                CombatSystem combat = Stance1Worlds.CombatOf(s);
                EntityId unit = Stance1Worlds.Unit(s, ProbeBodies.A);
                heights[level] = combat.MuzzlePoint(unit).Y.Raw - combat.Point(unit).Y.Raw;
            }
            return heights;
        }

        // B08: the viewpoint over the foot (EyePoint), and the same height found by the sight test: a wall between the two
        // standing units just under the eye leaves them seeing each other, one just over it cuts the line. The walls are the
        // nearest even raw heights under and over the eye (1 or 2 raw away: the map writer halves a wall's height). The second
        // part is the eye when both walls agree with it, else 0. Both parts are compared with the body's own standing eye (which is
        // the reference eye scaled), so a sight test that is wrong for every body still fails.
        private static long[] EyeHeights(ProbeBody b)
        {
            CombatPack pack = ProbeBodies.DirectPack(b);
            SimSession open = ProbeBodies.Field(pack);
            CombatSystem combat = Stance1Worlds.CombatOf(open);
            EntityId unit = Stance1Worlds.Unit(open, ProbeBodies.A);
            long eye = combat.EyePoint(unit).Y.Raw - combat.Point(unit).Y.Raw;
            long below = (eye - 1) - ((eye - 1) & 1), above = (eye + 1) + ((eye + 1) & 1);
            bool under = Sees(pack, below), over = Sees(pack, above);
            return new[] { eye, under && !over ? eye : 0 };
        }

        private static bool Sees(CombatPack pack, long wallTopRaw)
        {
            SimSession s = ProbeBodies.Field(pack, new Stance1Worlds.Box(15000, 6000, 16000, 10000, wallTopRaw));
            CombatSystem combat = Stance1Worlds.CombatOf(s);
            return combat.Visible(Stance1Worlds.Unit(s, ProbeBodies.A), Stance1Worlds.Unit(s, ProbeBodies.B));
        }

        // B09: the picture's top, eye and muzzle at each stance level (BodySizes of the body's pack).
        private static long[] DrawnHeights(ProbeBody b)
        {
            BodySizes sizes = BodySizes.For(ProbeBodies.DirectPack(b));
            var values = new List<long>();
            for (int level = 0; level < sizes.Levels; level++)
            {
                var e = new ViewEntity(1, 0, 0f, 0f, 0f, team: 1, stance: level);
                values.Add(Raw(sizes.Top(e))); values.Add(Raw(sizes.Eye(e))); values.Add(Raw(sizes.Muzzle(e)));
            }
            return values.ToArray();
        }

        private static readonly string[] DrawnParts =
            { "stand top", "stand eye", "stand muzzle", "crouch top", "crouch eye", "crouch muzzle", "prone top", "prone eye", "prone muzzle" };

        // B11: the farthest an enemy may be from a straight-down mouse ray and still be snapped to, found by bisection, with the
        // AIM-2 reach a pack's "pick" section gives (here the least one, 2 x the body radius: 0.5 m for the reference body, the
        // old fixed reach). A pack without the section keeps the fixed AimPick.SnapRadius (see the report).
        private static long[] PickReach(ProbeBody b)
        {
            BodySizes sizes = BodySizes.For(ProbeBodies.DirectPack(b));
            var reach = new PickReach(PickRules.MinRadiusPermille, PickRules.MinHeightPermille);
            var shown = new FrameView(1, new[] { new ViewEntity(1, 0, 2f, 0f, 4.5f, team: 1), new ViewEntity(2, 0, 9f, 0f, 4.5f, team: 2) });
            float above = sizes.StandingHeight + 10f;
            Func<double, bool> snaps = d => AimPick.PickRay((float)(9.0 + d), above, 4.5f, 0f, -1f, 0f, 0f, null, shown, 1, sizes: sizes, reach: reach).Kind == AimKind.Unit;
            double low = 0, high = 10;
            if (!snaps(low) || snaps(high)) throw new InvalidOperationException("no reach to bisect");
            for (int i = 0; i < 60; i++)
            {
                double mid = (low + high) / 2;
                if (snaps(mid)) low = mid; else high = mid;
            }
            return new[] { (long)Math.Round(low * M, MidpointRounding.AwayFromZero) };
        }

        // B16: a sword swing's half height in a world of the body's pack carrying the shipped rwr-melee swing.
        private static long[] MeleeHalfHeight(ProbeBody b)
        {
            MeleeSpec sword = RwrPack.Load("rwr-melee").BuildCombat().Melee[0];
            SimSession s = ProbeBodies.Field(ProbeBodies.DirectPack(b, new[] { sword }));
            return new[] { Stance1Worlds.CombatOf(s).Pack.Melee[0].HalfHeight.Raw };
        }

        // B20: the navigation grid of the match takes a clearance query with the body's radius (the reference radius scaled).
        private static ProbeResult NavRadius(ProbeBody b)
        {
            long radius = b.Scale(StartRadius(ProbeBodies.Reference)[0]);
            M6World w = ProbeBodies.Match(b);
            var deployer = Ml7Worlds.Field<IUnitDeployer>(w.Reinforce, "deployer");
            var grid = Ml7Worlds.Field<NavGrid>(Ml7Worlds.Field<DeployGround>(deployer, "ground"), "grid");
            SimVec3 at = Point(20 * M, 20 * M);
            grid.SegmentClear(at, at, SimFix.FromRaw(radius));
            return Pass("query of radius " + Metres(radius), "answered");
        }

        private static readonly Probe[] all =
        {
            new Probe("B01", "H2", "reader ceiling comes from the world's headroom", "behaviour (reader)", "Content/Data/Sections/CombatSection.cs:43", B01),
            new Probe("B02", "H1", "reader accepts the tiny body (lower bounds from its width)", "behaviour (reader)", "Content/Data/Sections/CombatSection.cs:40-44,82-85", B02),
            new Probe("B03", "H1", "collider radius, unit placed at the start", "API Read<Collider>", "Presentation/Bridge/Features/MovementFeature.cs:54; cap Sim/BodyRadius.cs:13",
                One, RawTolerance, StartRadius),
            new Probe("B04", "H1", "mover clearance from walls", "API the match mover's footprint", "Interim/InterimLocomotion.Terrain.cs:22,34; cap Sim/BodyRadius.cs:13",
                One, RawTolerance, MoverFootprint),
            new Probe("B05", "H1", "deploy ground footprint", "behaviour DeployGround.CanStand", "Interim/DeployGround.cs:9; cap Sim/BodyRadius.cs:13",
                One, RawTolerance, DeployReach),
            new Probe("B06", "-", "collider height per stance", "API Read<Collider> + posture body", "Presentation/Bridge/Features/WorldAssembly.cs:111; Sim/PostureGeometry.cs:38",
                Levels, RawTolerance, ColliderHeights),
            new Probe("B07", "-", "muzzle height, stand and crouch", "API CombatSystem.MuzzlePoint", "Combat/CombatSystem.Sight.cs:33",
                new[] { "stand", "crouch" }, RawTolerance, MuzzleHeights),
            new Probe("B08", "-", "eye height and the sight line over a wall", "API CombatSystem.EyePoint + Visible", "Combat/CombatSystem.Sight.cs:29,127",
                new[] { "eye", "sight" }, RawTolerance, EyeHeights, b => new[] { b.Body.Eye.Raw, b.Body.Eye.Raw }),
            new Probe("B09", "-", "drawn top, eye and muzzle per stance", "API BodySizes", "Presentation/Bridge/Features/BodySizes.cs:38-57",
                DrawnParts, FloatTolerance, DrawnHeights),
            new Probe("B10", "H1", "drawn radius", "API BodySizes.Radius", "Presentation/Bridge/Features/BodySizes.cs:25,54; cap Sim/BodyRadius.cs:13",
                One, FloatTolerance, b => new[] { Raw(BodySizes.For(ProbeBodies.DirectPack(b)).Radius) }),
            new Probe("B11", "H1", "mouse snap reach", "behaviour AimPick.PickRay with a PickReach", "Presentation/Camera/PickReach.cs; cap Sim/BodyRadius.cs:13",
                One, FloatTolerance, PickReach),
            new Probe("B12", "M1", "see-through ring height (standing eye)", "constant LayerCut.MinSeeHeight", "Presentation/Camera/LayerCut.cs:47",
                One, FloatTolerance, b => new[] { Raw(LayerCut.MinSeeHeight) }, b => new[] { b.Body.Eye.Raw }),
            new Probe("B13", "H2", "layer cut headroom equals the world's headroom", "constant LayerCut.Headroom", "Presentation/Camera/LayerCut.cs:43",
                One, FloatTolerance, b => new[] { Raw(LayerCut.Headroom) }, b => new[] { WallSpan.ClearanceRaw }),
            new Probe("B14", "M2", "AI guard slot spacing (pack default)", "constant AiRules.DefaultSlotSpacingMm", "Content/Data/Sections/AiSection.cs:36",
                One, RawTolerance, b => new[] { RawOfMm(AiRules.DefaultSlotSpacingMm) }),
            new Probe("B15", "M2", "vertical spread snap (pack default)", "constant VerticalSpread.DefaultSnapMm", "Combat/VerticalSpread.cs:18",
                One, RawTolerance, b => new[] { RawOfMm(VerticalSpread.DefaultSnapMm) }),
            new Probe("B16", "M2", "sword swing half height", "API CombatSystem.Pack.Melee", "Content/Rwr/Data/rwr-melee.json:5-6; Combat/CombatSystem.Melee.cs:144",
                One, RawTolerance, MeleeHalfHeight),
            new Probe("B17", "M2", "crowd slot spacing", "constant WorldBuilder.CrowdSlotSpacing", "Presentation/Bridge/WorldBuilder.cs:14",
                One, RawTolerance, b => new[] { WorldBuilder.CrowdSlotSpacing.Raw }),
            new Probe("B18", "H2", "body over the world's headroom is refused", "behaviour (reader)", "Content/Data/Sections/CombatSection.cs:43", B18),
            new Probe("B19", "H1", "collider radius, reinforcement", "API Read<Collider>", "Presentation/Bridge/Features/MatchFeature.cs:139,210; cap Sim/BodyRadius.cs:13",
                One, RawTolerance, ReinforcedRadius),
            new Probe("B20", "H1", "navigation clearance takes the body's radius", "behaviour NavGrid.SegmentClear", "Sim/NavGrid.cs:92,125; Sim/BodyRadius.cs", NavRadius),
            new Probe("B21", "H1", "wall probe below the walk band", "constant WallSpan.BelowRaw", "Content/WallSpan.cs:17",
                One, RawTolerance, b => new[] { WallSpan.BelowRaw }),
            new Probe("B22", "H1", "map gate footprint (the pack's unit radius)", "API ContentPack.UnitRadius of the body's layer", "Content/MapTools/MapGate.cs; Content/Data/ContentPack.cs:62",
                GateFootprint),
            new Probe("B23", "M1", "low end of the unit seen through walls", "constant LayerCut.FootLift", "Presentation/Camera/LayerCut.cs:44",
                One, FloatTolerance, b => new[] { Raw(LayerCut.FootLift) }),
        };

        internal static IReadOnlyList<Probe> All => all;

        internal static Probe Find(string id)
        {
            foreach (Probe p in all) if (p.Id == id) return p;
            throw new ArgumentException("no probe " + id);
        }
    }
}
