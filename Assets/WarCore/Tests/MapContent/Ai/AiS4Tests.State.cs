using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AiS4Tests: the content fields, the saved state and whole battles with cover.
    public sealed partial class AiS4Tests
    {
        private static readonly string[] CoverFields = { "coverRadiusMm", "coverStepMm", "coverDepthMm" };
        private static readonly int[] CoverValues = { 9000, 1000, 2000 };
        private static string CoverJson(int[] values, int skip = -1, string before = null)
        {
            var parts = new List<string>();
            if (before != null) parts.Add(before);
            for (int i = 0; i < CoverFields.Length; i++) if (i != skip) parts.Add("\"" + CoverFields[i] + "\": " + values[i]);
            return "{ " + string.Join(", ", parts) + " }";
        }
        private const string Head = "content pack 's4-test' layer 's4-test' ai ";
        private static string Pressed => BoundAi + ", " + PressAi();

        // ---- Content ----

        [Test]
        public void Content_CoverFieldsAreReadAllOrNone()
        {
            foreach (string none in new[] { null, "{}", S3() })
            {
                AiRules off = Pack(none).Section<AiRules>();
                Assert.That(new[] { off.CoverRadiusMm, off.CoverStepMm, off.CoverDepthMm }, Is.EqualTo(new[] { 0, 0, 0 }));
            }
            AiRules on = Pack(CoverJson(new[] { 8000, 700, 1500 }, -1, Pressed)).Section<AiRules>();
            Assert.That(new[] { on.CoverRadiusMm, on.CoverStepMm, on.CoverDepthMm }, Is.EqualTo(new[] { 8000, 700, 1500 }));
            for (int skip = 0; skip < 3; skip++)
            {
                var e = Assert.Throws<ContentException>(() => Pack(CoverJson(CoverValues, skip, Pressed)));
                Assert.That(e.Message, Is.EqualTo(Head + "is missing field '" + CoverFields[skip] + "'"));
            }
            // One field alone: the first of the others is reported missing.
            for (int only = 0; only < 3; only++)
            {
                var e = Assert.Throws<ContentException>(() => Pack("{ " + Pressed + ", \"" + CoverFields[only] + "\": " + CoverValues[only] + " }"));
                Assert.That(e.Message, Is.EqualTo(Head + "is missing field '" + CoverFields[only == 0 ? 1 : 0] + "'"));
            }
            // Range edges are accepted.
            Assert.That(Pack(CoverJson(new[] { 1000, 250, 250 }, -1, Pressed)).Section<AiRules>().CoverRadiusMm, Is.EqualTo(1000));
            Assert.That(Pack(CoverJson(new[] { 30000, 5000, 10000 }, -1, Pressed)).Section<AiRules>().CoverDepthMm, Is.EqualTo(10000));
        }

        [TestCase(0, 999)] [TestCase(0, 30001)]
        [TestCase(1, 249)] [TestCase(1, 5001)]
        [TestCase(2, 249)] [TestCase(2, 10001)]
        public void Content_CoverOutOfRangeNamesTheField(int index, int value)
        {
            int[] values = (int[])CoverValues.Clone(); values[index] = value;
            var e = Assert.Throws<ContentException>(() => Pack(CoverJson(values, -1, Pressed)));
            Assert.That(e.Message, Does.StartWith(Head + "field '" + CoverFields[index] + "': "));
        }

        // Cover belongs to engage "bound" and needs the pressure fields: only a pinned unit takes cover.
        [Test]
        public void Content_CoverNeedsBoundAndThePressureFields()
        {
            var e = Assert.Throws<ContentException>(() => Pack(CoverJson(CoverValues, -1, BoundAi)));
            Assert.That(e.Message, Is.EqualTo(Head + "field 'coverRadiusMm': needs the pressure fields (pressureDeathRadiusMm ...)"));
            // Under "hold" (written or left out) the fields are refused, one by one, like every field of a part that is not chosen.
            foreach (string engage in new[] { "", "\"engage\": \"hold\", " })
                for (int i = 0; i < 3; i++)
                {
                    e = Assert.Throws<ContentException>(() => Pack("{ " + engage + PressAi() + ", \"" + CoverFields[i] + "\": " + CoverValues[i] + " }"));
                    Assert.That(e.Message, Does.StartWith(Head + "field '" + CoverFields[i] + "': "), engage);
                    Assert.That(e.Message, Does.Contain("bound"), engage);
                }
        }

        // The parts carry the three numbers in raw units; on a terrain map the cover fields are refused by name.
        [Test]
        public void Content_PartsCarryCoverAndRefuseItOnTerrain()
        {
            AiRules ai = Pack(CoverJson(new[] { 8000, 700, 1500 }, -1, Pressed)).Section<AiRules>();
            AiParts parts = ContentPolicies.Parts(ai);
            Assert.That(parts.CoverRadiusRaw, Is.EqualTo(8L * SimFix.Scale));
            Assert.That(parts.CoverStepRaw, Is.EqualTo(SimFix.Ratio(700, 1000).Raw));
            Assert.That(parts.CoverDepthRaw, Is.EqualTo(SimFix.Ratio(1500, 1000).Raw));
            Assert.That(ContentPolicies.Parts(ai, default, 1, true).CoverRadiusRaw, Is.EqualTo(8L * SimFix.Scale));
            var e = Assert.Throws<ContentException>(() => ContentPolicies.Parts(ai, default, 1, false));
            Assert.That(e.Message, Is.EqualTo(Head + "field 'coverRadiusMm': is not supported on a terrain map"));
            // Without cover a terrain map takes the same rules.
            AiParts plain = ContentPolicies.Parts(Pack(S3()).Section<AiRules>(), default, 1, false);
            Assert.That(new[] { plain.CoverRadiusRaw, plain.CoverStepRaw, plain.CoverDepthRaw }, Is.EqualTo(new long[] { 0, 0, 0 }));
            Assert.That(ContentPolicies.Parts(null, default, 1, false).CoverRadiusRaw, Is.EqualTo(0));
        }

        // ---- Saved state ----

        // With sources the entry's source is saved and hashed (version 2); without, the state is what it was (version 1).
        [Test]
        public void State_TheSourceIsSavedOnlyWithSources()
        {
            M6World w = Yard(null);
            Pressure with = new Pressure(w.Combat, Rules(), true), without = new Pressure(w.Combat, Rules());
            Assert.That(with.StateVersion, Is.EqualTo(2));
            Assert.That(without.StateVersion, Is.EqualTo(1));
            FireRecord[] shots = { Near(w, East, At(w, 3)), Near(w, North, At(w, 7)) };
            with.Advance(shots); without.Advance(shots);
            Assert.That(without.TryPressedFrom(Id(w, 3), out _), Is.False);
            byte[] Save(Pressure p) { var writer = new SimStateWriter(); p.SaveState(writer); return writer.ToArray(); }
            ulong Hash(Pressure p) { var hash = new StateHash(); p.WriteState(hash); return hash.Value; }
            byte[] full = Save(with), bare = Save(without);
            // Two entries, three 8-byte numbers each.
            Assert.That(full.Length, Is.EqualTo(bare.Length + 2 * 24));
            Assert.That(Hash(with), Is.Not.EqualTo(Hash(without)));
            // A round trip gives the same sources and the same hash.
            var again = new Pressure(w.Combat, Rules(), true);
            again.LoadState(new SimStateReader(full));
            Assert.That(From(again, Id(w, 3)), Is.EqualTo(East));
            Assert.That(From(again, Id(w, 7)), Is.EqualTo(North));
            Assert.That(Hash(again), Is.EqualTo(Hash(with)));
            Assert.That(Save(again), Is.EqualTo(full));
            // The source is part of the hash: the same shots from somewhere else.
            var other = new Pressure(w.Combat, Rules(), true);
            other.Advance(new[] { Near(w, East, At(w, 3)), Near(w, East, At(w, 7)) });
            Assert.That(other.Value(Id(w, 7)), Is.EqualTo(with.Value(Id(w, 7))));
            Assert.That(Hash(other), Is.Not.EqualTo(Hash(with)));
            // Each of the three coordinates.
            foreach (SimVec3 from in new[] { SimVec3.Metres(111, 0, 20), SimVec3.Metres(110, 1, 20), SimVec3.Metres(110, 0, 21) })
            {
                var moved = new Pressure(w.Combat, Rules(), true);
                moved.Advance(new[] { Near(w, from, At(w, 3)), Near(w, North, At(w, 7)) });
                Assert.That(Hash(moved), Is.Not.EqualTo(Hash(with)), from.ToString());
                var back = new Pressure(w.Combat, Rules(), true);
                back.LoadState(new SimStateReader(Save(moved)));
                Assert.That(From(back, Id(w, 3)), Is.EqualTo(from));
            }
            var loaded = new Pressure(w.Combat, Rules());
            loaded.LoadState(new SimStateReader(bare));
            Assert.That(Hash(loaded), Is.EqualTo(Hash(without)));
        }

        // Each cover number is part of the battle's policy: battles that differ in one have different hashes and refuse
        // each other's snapshots, and so do a battle with cover and one without.
        [Test]
        public void State_EveryCoverNumberIsInTheBattle()
        {
            string[] variants = { S4(), S4(radius: 8999), S4(step: 999), S4(depth: 1999), S3() };
            var worlds = new M6World[variants.Length]; var saved = new byte[variants.Length][];
            var hashes = new HashSet<ulong>();
            for (int i = 0; i < variants.Length; i++)
            {
                worlds[i] = Yard(variants[i], Walls);
                worlds[i].Step();
                saved[i] = worlds[i].Sim.SaveRetainedSnapshot();
                hashes.Add(worlds[i].Hash);
            }
            Assert.That(hashes.Count, Is.EqualTo(variants.Length));
            for (int i = 0; i < variants.Length; i++)
                for (int j = 0; j < variants.Length; j++)
                {
                    if (i == j) Assert.DoesNotThrow(() => worlds[j].Sim.RestoreRetainedSnapshot(saved[i]));
                    else Assert.Catch(() => worlds[j].Sim.RestoreRetainedSnapshot(saved[i]), i + " into " + j);
                }
        }

        // ---- Whole battles ----

        // The control battle (150 a side, real weapons) on a field with six walls where the teams meet.
        private static readonly (int, int, int, int)[] Yards = { (64, 46, 1, 8), (96, 46, 1, 8), (66, 38, 1, 6), (94, 56, 1, 6), (40, 46, 1, 8), (120, 46, 1, 8) };
        private const string Real = "\"pressureDeathRadiusMm\": 3000, \"pressureDeathShock\": 2000, \"pressureNearRadiusMm\": 3000, \"pressureNearShock\": 1000, "
            + "\"pressureDecayPermille\": 12, \"pressureBrokenAt\": 7000, \"pressureRecoverAt\": 1000, \"pressurePinnedAt\": 2000, "
            + "\"pressureSpreadPermille\": 500, \"pressureBreakSharePermille\": 300";
        private const string BattleS3 = "{ " + BoundAi + ", " + Real + " }";
        private const string BattleS4 = "{ " + BoundAi + ", " + Real + ", \"coverRadiusMm\": 9000, \"coverStepMm\": 1000, \"coverDepthMm\": 2000 }";
        private static M6World Battle(ulong seed, bool? fair, string ai) =>
            new M6World(FairEFixture.Recipe("control", seed, fair).WithAiRules(Pack(ai)), WcMapWriter.Write(M3TestWorld.Boxes(160, 100, Yards)));

        // Living pinned units that stand with a wall within 2 m toward where their fire came from, and those walking there.
        private static (int standing, int walking) InCover(M6World w)
        {
            Pressure p = w.System<Pressure>(); int standing = 0, walking = 0;
            for (ulong key = 1; key <= 300; key++)
            {
                EntityId unit = w.Unit(key); StableId id = Id(w, key);
                if (w.Combat.IsDead(unit) || p.Level(id) != AiNerve.Pinned || !p.TryPressedFrom(id, out SimVec3 from)) continue;
                MoveIntent order = w.Read<MoveIntent>(key);
                if (!order.Active) { if (w.Combat.WallWithin(At(w, key), from, SimFix.FromInt(2))) standing++; }
                else if (w.Combat.WallWithin(order.Target, from, SimFix.FromInt(2))) walking++;
            }
            return (standing, walking);
        }

        [TestCase(null)]
        [TestCase(true)]
        public void Battle_RepeatAndRestoreWhileInCover(bool? fair)
        {
            M6World first = Battle(41, fair, BattleS4), repeat = Battle(41, fair, BattleS4);
            Assert.That(repeat.Hash, Is.EqualTo(first.Hash));
            int stepped = 0;
            while (true)
            {
                (int standing, int walking) = InCover(first);
                if (standing >= 3 && walking >= 1) break;
                Assert.That(stepped, Is.LessThan(3000), "nobody took cover");
                first.Step(); repeat.Step(); stepped++;
                Assert.That(repeat.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
            byte[] snapshot = first.Sim.SaveRetainedSnapshot();
            M6World restored = Battle(41, fair, BattleS4);
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(first.Hash));
            Assert.That(InCover(restored), Is.EqualTo(InCover(first)));
            for (int tick = 0; tick < 600 && !first.Sim.IsFinished; tick++)
            {
                first.Step(); restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
        }

        // The same battle without the cover fields goes the same way until the first unit takes cover, and another way after.
        [Test]
        public void Battle_CoverChangesTheBattleFromTheFirstUnitInCover()
        {
            M6World on = Battle(41, null, BattleS4), off = Battle(41, null, BattleS3);
            bool same = true; int stepped = 0;
            while (same)
            {
                Assert.That(stepped, Is.LessThan(3000), "cover changed nothing");
                (int standing, int walking) = InCover(on);
                on.Step(); off.Step(); stepped++;
                // Reserves have no place until they come in.
                for (ulong key = 1; key <= 300 && same; key++)
                    same = on.Has<Position>(key) == off.Has<Position>(key) && (!on.Has<Position>(key) || At(on, key).Equals(At(off, key)));
                if (same) Assert.That(walking, Is.EqualTo(0), "tick " + stepped + ": a unit walked to cover and the battles stayed the same");
            }
            Assert.That(stepped, Is.GreaterThan(Think));
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        // Units look for cover, walk there and stand there: a step must not allocate.
        [Test]
        public void Battle_StepWithCoverAllocatesZero()
        {
            M6World w = Battle(41, null, BattleS4);
            int stepped = 0;
            while (InCover(w).walking == 0)
            {
                Assert.That(stepped, Is.LessThan(3000), "nobody took cover");
                w.Step(); stepped++;
            }
            // The same battle once through the measured stretch first, so everything that runs has run before.
            M6World warm = Battle(41, null, BattleS4);
            warm.Step(stepped + 150);
            AllocationMeter.Stabilize();
            long total = 0; int during = 0;
            for (int i = 0; i < 150; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                w.Sim.Step(default);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
                (int standing, int walking) = InCover(w); during += walking;
            }
            Assert.That(total, Is.EqualTo(0L));
            Assert.That(during, Is.GreaterThan(0), "somebody must walk to cover while it is measured");
        }
#endif
    }
}
