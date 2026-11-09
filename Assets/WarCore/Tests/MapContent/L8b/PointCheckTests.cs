using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L8b: the control points of a session against the map Sim uses and against the drawn frame (PointCheck).
    // Expected values are written down in the work package; a test that disagrees is a stop, not a value to copy.
    public sealed class PointCheckTests
    {
        private const long S = SimFix.Scale;
        private static long H(int cell) => cell * S + S / 2;
        private static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));

        private static void Expect(PointCheckResult r, int positions, bool count, int off, int blocked, int heights, int views, string what)
        {
            Assert.That(r.Positions, Is.EqualTo(positions), what + ": positions\n" + r);
            Assert.That(r.CountMismatch, Is.EqualTo(count), what + ": count\n" + r);
            Assert.That(r.OffMap, Is.EqualTo(off), what + ": off map\n" + r);
            Assert.That(r.Blocked, Is.EqualTo(blocked), what + ": blocked\n" + r);
            Assert.That(r.Heights, Is.EqualTo(heights), what + ": heights\n" + r);
            Assert.That(r.Views, Is.EqualTo(views), what + ": views\n" + r);
        }

        private static void Dirty(PointCheckResult r, int positions, bool count, int off, int blocked, int heights, int views, string what)
        {
            Expect(r, positions, count, off, blocked, heights, views, what);
            Assert.That(r.IsClean, Is.False, what + ": IsClean");
        }

        private static void Clean(PointCheckResult r, int positions, string what)
        {
            Expect(r, positions, false, 0, 0, 0, 0, what);
            Assert.That(r.IsClean, Is.True, what + ": IsClean");
        }

        // (1) Production and the demo recipes are clean.
        [Test]
        public void PointCheck_ProductionTerrainMatchIsCleanFromStartToEnd()
        {
            SimSession s = SimSession.Create(DemoRecipes.TerrainMatch(), TerrainDemo.MapBytes());
            Clean(PointCheck.Run(s.Current, s), 6, "tick 0");
            Assert.That(TerrainCheck.Run(s.Map, s).IsClean, Is.True, TerrainCheck.Run(s.Map, s).ToString());
            while (!s.IsFinished && s.Tick < 9000) s.StepOnce();
            Assert.That(s.IsFinished, Is.True, "the match ends by 9000");
            Clean(PointCheck.Run(s.Current, s), 6, "end tick " + s.Tick);
        }

        [Test]
        public void PointCheck_DemoRecipesAreClean()
        {
            SimSession m6 = SimSession.Create(DemoRecipes.M6(), DemoRecipes.M6MapBytes());
            Clean(PointCheck.Run(m6.Current, m6), 8, "M6");
            SimSession m7 = SimSession.Create(DemoRecipes.M7(), DemoRecipes.M7MapBytes());
            Clean(PointCheck.Run(m7.Current, m7), 12, "M7");
            SimSession a3a = SimSession.Create(A3aDemo.Recipe(), A3aDemo.MapBytes());
            Clean(PointCheck.Run(a3a.Current, a3a), 4, "A3a");
        }

        // (2) Counterexamples on the L6 fixture: point 1 or one anchor changed, point 2 as L6.
        private static SimSession L6With(ControlPointDef first, SpawnAnchor? team2 = null)
        {
            SpawnAnchor[] bases = L6.Bases();
            if (team2.HasValue) bases[1] = team2.Value;
            ControlPointDef[] points = { first, L6.Points()[1] };
            return SimSession.Create(L6.Recipe().WithMatch(L6.Pack().BuildMatch(points, bases)), L6.MapBytes());
        }

        private static PointCheckResult Check(SimSession s) => PointCheck.Run(s.Current, s);
        private static readonly SimVec3 Crest = Raw(H(22), 376832, H(16));

        [Test]
        public void PointCheck_CentreBelowTheCrest()
        {
            Dirty(Check(L6With(new ControlPointDef(1, Raw(H(22), 0, H(16)), SimFix.FromInt(3), 0, true, Crest))), 6, false, 0, 0, 1, 0, "(i)");
        }

        [Test]
        public void PointCheck_CentreOnASteepCell()
        {
            SimSession s = L6With(new ControlPointDef(1, Raw(H(20), 376832, H(22)), SimFix.FromInt(3)));
            // Hand value: corners 10, 11, 12, 13 units; the centre lies on the diagonal, (10 + 13) / 2 x 32768 = 376832.
            Assert.That(s.Terrain.Ground.TryHeightAt(SimFix.FromRaw(H(20)), SimFix.FromRaw(H(22)), out SimFix y), Is.True);
            Assert.That(y.Raw, Is.EqualTo(376832));
            Assert.That(s.Terrain.Nav.IsPassable(20, 22), Is.False, "cell (20, 22) is too steep");
            Dirty(Check(s), 5, false, 0, 1, 0, 0, "(ii)");
        }

        [Test]
        public void PointCheck_CentreInAWall()
        {
            Dirty(Check(L6With(new ControlPointDef(1, Raw(H(6), 0, H(10)), SimFix.FromInt(3)))), 5, false, 0, 1, 0, 0, "(iii)");
        }

        [Test]
        public void PointCheck_SpawnBelowTheCrest()
        {
            Dirty(Check(L6With(new ControlPointDef(1, Crest, SimFix.FromInt(3), 0, true, Raw(H(22), 0, H(16))))), 6, false, 0, 0, 1, 0, "(iv)");
        }

        [Test]
        public void PointCheck_CentreOffTheMap()
        {
            Dirty(Check(L6With(new ControlPointDef(1, Raw(-32768, 0, H(16)), SimFix.FromInt(3)))), 5, false, 1, 0, 0, 0, "(v)");
            Dirty(Check(L6With(new ControlPointDef(1, Raw(H(48), 0, H(16)), SimFix.FromInt(3)))), 5, false, 1, 0, 0, 0, "(v')");
        }

        // (vi) The production frame at tick 0, copied with one field of point 1 changed.
        private static FrameView Copy(FrameView f, Func<ViewPoint, ViewPoint> first, bool onlyFirst = false)
        {
            var points = new ViewPoint[onlyFirst ? 1 : f.Points.Length];
            for (int i = 0; i < points.Length; i++) points[i] = i == 0 ? first(f.Points[i]) : f.Points[i];
            return new FrameView(f.Tick, f.Entities.ToArray(), f.Fires, points, f.Match, f.Respawn);
        }

        [Test]
        public void PointCheck_DrawnPointDiffers()
        {
            SimSession s = SimSession.Create(DemoRecipes.TerrainMatch(), TerrainDemo.MapBytes());
            FrameView f = s.Current;
            Clean(PointCheck.Run(Copy(f, p => p), s), 6, "unchanged copy");
            Dirty(PointCheck.Run(Copy(f, p => new ViewPoint(p.Id, p.X, p.Z + 1f, p.Radius, p.Owner, p.Capturing, p.Progress)), s), 6, false, 0, 0, 0, 1, "(vi)");
            Dirty(PointCheck.Run(Copy(f, p => new ViewPoint(p.Id, p.X + 1f, p.Z, p.Radius, p.Owner, p.Capturing, p.Progress)), s), 6, false, 0, 0, 0, 1, "(vi-x)");
            Dirty(PointCheck.Run(Copy(f, p => new ViewPoint(9, p.X, p.Z, p.Radius, p.Owner, p.Capturing, p.Progress)), s), 6, false, 0, 0, 0, 1, "(vi-id)");
            Dirty(PointCheck.Run(Copy(f, p => new ViewPoint(p.Id, p.X, p.Z, p.Radius + 1f, p.Owner, p.Capturing, p.Progress)), s), 6, false, 0, 0, 0, 1, "(vi-r)");
            Dirty(PointCheck.Run(Copy(f, p => p, onlyFirst: true), s), 6, true, 0, 0, 0, 0, "(vi')");
        }

        [Test]
        public void PointCheck_AnchorInAWall()
        {
            Dirty(Check(L6With(L6.Points()[0], new SpawnAnchor(2, Raw(H(6), 0, H(10))))), 6, false, 0, 1, 0, 0, "(vii)");
        }

        [Test]
        public void PointCheck_FlatPointInAWall()
        {
            ContentPack pack = RwrPack.Load(RwrPack.M6Layer);
            MatchPack m7 = RwrMatch.CreateM7(pack);
            var points = m7.Points.ToArray();
            var bases = m7.Bases.ToArray();
            ControlPointDef p = points[0];
            points[0] = new ControlPointDef(p.Id, Raw(48 * S + S / 2, 0, 20 * S + S / 2), p.Radius, p.InitialOwner);
            SimSession s = SimSession.Create(DemoRecipes.M7().WithMatch(pack.BuildMatch(points, bases)), DemoRecipes.M7MapBytes());
            Assert.That(s.BlockedCell(48, 20), Is.True, "(48, 20) is a wall cell");
            (int x, int z)? first = null;
            for (int z = 0; z < s.Map.Depth && first == null; z++)
                for (int x = 0; x < s.Map.Width && first == null; x++)
                    if (s.BlockedCell(x, z)) first = (x, z);
            Assert.That(first, Is.EqualTo(((int, int)?)(48, 20)), "the first wall cell, row by row");
            Dirty(Check(s), 11, false, 0, 1, 0, 0, "(viii)");
        }

        [Test]
        public void PointCheck_NoMatchIsEmpty()
        {
            SimSession s = SimSession.Create(DemoRecipes.M2b(), DemoRecipes.M2bMapBytes());
            Clean(Check(s), 0, "(ix)");
        }

        [Test]
        public void PointCheck_NullArgumentsThrow()
        {
            SimSession s = SimSession.Create(DemoRecipes.M2b(), DemoRecipes.M2bMapBytes());
            var view = Assert.Throws<ArgumentNullException>(() => PointCheck.Run(null, s));
            Assert.That(view.ParamName, Is.EqualTo("view"));
            var session = Assert.Throws<ArgumentNullException>(() => PointCheck.Run(s.Current, null));
            Assert.That(session.ParamName, Is.EqualTo("session"));
        }

        // (3) The terrain entrance builds the production terrain match from the named layer.
        [Test]
        public void PointCheck_ContentLayersTerrainIsTheDemoMatch()
        {
            SimSession a = SimSession.Create(ContentLayers.Terrain("rwr-terrain-demo"), TerrainDemo.MapBytes());
            SimSession b = SimSession.Create(DemoRecipes.TerrainMatch(), TerrainDemo.MapBytes());
            Assert.That(a.StateHash, Is.EqualTo(b.StateHash), "tick 0");
            for (int t = 0; t < 300; t++) { a.StepOnce(); b.StepOnce(); }
            Assert.That(a.Tick, Is.EqualTo(300));
            Assert.That(a.StateHash, Is.EqualTo(b.StateHash), "tick 300");
            var terrain = Assert.Throws<ContentException>(() => ContentLayers.Terrain("missing"));
            var m6 = Assert.Throws<ContentException>(() => ContentLayers.M6("missing"));
            Assert.That(terrain.Message, Is.EqualTo(m6.Message));
        }
    }
}
