using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.Ml11b1World;

namespace WarCore.Tests
{
    // AI-M1, the scheduler side (content field meleeNerve): a melee unit that is pressed and has an enemy to chase asks the
    // engage part first. Advance chases as before, Hold stands, Cover takes cover or stands, Fallback goes straight to the
    // refuge. A calm unit is not asked, and without the field nothing is asked at all.
    public sealed class AiM1MeleeTests
    {
        internal const string Pressed = "\"pressureDeathRadiusMm\": 3000, \"pressureDeathShock\": 2000, \"pressureNearRadiusMm\": 3000, \"pressureNearShock\": 2500, "
            + "\"pressureDecayPermille\": 12, \"pressureBrokenAt\": 7000, \"pressureRecoverAt\": 1000, \"pressurePinnedAt\": 2000, "
            + "\"pressureSpreadPermille\": 0, \"pressureBreakSharePermille\": 1000";
        internal const string Bound = "\"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000";
        private const string Nerve = "{ \"meleeChaseRange\": 12, \"meleeNerve\": true, " + Pressed + " }";
        private const string NoNerve = "{ \"meleeChaseRange\": 12, " + Pressed + " }";

        // What the scheduler asked, and what it is told.
        private sealed class Asked : IAiEngage
        {
            internal AiEngage MeleeAnswer = AiEngage.Advance, UnseenAnswer = AiEngage.Hold;
            internal int Melees, Unseens, Decides;
            internal AiNerve Nerve; internal SimVec3 Enemy; internal StableId Unit;
            public ulong Fold(ulong digest) => digest;
            public AiEngage Decide(in AiUnit unit, SimVec3 enemy, AiField field) { Decides++; return AiEngage.Hold; }
            public AiEngage Unseen(in AiUnit unit, AiField field) { Unseens++; return UnseenAnswer; }
            public AiEngage Melee(in AiUnit unit, SimVec3 enemy, AiField field) { Melees++; Nerve = unit.Nerve; Enemy = enemy; Unit = unit.Id; return MeleeAnswer; }
        }

        // A blade (A) 8 m from an enemy that stands still (B); team 1 owns the point at (20.5, 20.5) unless told otherwise and
        // has its base at (5, 30). The AI thinks every tick.
        private static M6World Duel(string ai, bool ownPoint = true, Asked part = null)
        {
            var points = new List<ControlPointDef> { PointDown(1, 60500, 20500) };
            if (ownPoint) points.Add(PointDown(2, 20500, 20500, 3, 1));
            M6World w = World(new[] { Blade(A, 30500, 20500), Still(B, 38500, 20500) }, ai, Guns(8, MeleeWorld.Spec(radius: 3, maxTargets: 1, engage: 1.8)),
                plates: false, points: points.ToArray());
            if (part != null) Install(w, "engage", part);
            return w;
        }

        // Enemy shots that end at the unit's feet: each raises its pressure by 2500 (pinned at 2000, broken at 7000).
        private static void Press(M6World w, ulong key, int shots)
        {
            Pressure p = w.System<Pressure>();
            for (int i = 0; i < shots; i++)
                p.Advance(new[] { new FireRecord(Id(w, B), default, At(w, B), At(w, key), default, 0, 2, FireOutcome.Open) }, new StrikeRecord[0]);
        }
        private static AiNerve Level(M6World w, ulong key) => w.System<Pressure>().Level(Id(w, key));

        // 6 (M5). A calm unit chases as before and the part is not asked.
        [Test]
        public void ACalmUnitChasesWithoutAsking()
        {
            var part = new Asked { MeleeAnswer = AiEngage.Hold };
            M6World w = Duel(Nerve, part: part);
            w.Step();
            Assert.That(GoesTo(w, A, At(w, B), 0), Is.True);
            Assert.That((part.Melees, part.Unseens, part.Decides), Is.EqualTo((0, 0, 0)));
        }

        // 6 (M4). Advance: the pressed unit chases; the part is told the unit, its nerve and where the enemy is.
        [TestCase(1, AiNerve.Pinned)]
        [TestCase(3, AiNerve.Broken)]
        public void AdvanceChases(int shots, AiNerve nerve)
        {
            var part = new Asked { MeleeAnswer = AiEngage.Advance };
            M6World w = Duel(Nerve, part: part);
            Press(w, A, shots);
            Assert.That(Level(w, A), Is.EqualTo(nerve));
            w.Step();
            Assert.That(GoesTo(w, A, At(w, B), 0), Is.True);
            Assert.That((part.Melees, part.Unseens), Is.EqualTo((1, 0)));
            Assert.That((part.Nerve, part.Enemy, part.Unit), Is.EqualTo((nerve, At(w, B), Id(w, A))));
        }

        // 6 (M4). Hold and Cover (this battle has no cover rules): the unit stands, and a unit already chasing stops.
        [TestCase(AiEngage.Hold)]
        [TestCase(AiEngage.Cover)]
        public void HoldAndCoverStand(AiEngage answer)
        {
            var part = new Asked { MeleeAnswer = answer, UnseenAnswer = AiEngage.Fallback };
            M6World w = Duel(Nerve, part: part);
            w.Step();
            Assert.That(GoesTo(w, A, At(w, B), 0), Is.True, "calm: chasing");
            Press(w, A, 1);
            w.Step();
            Assert.That(Ordered(w, A), Is.False, "pressed: stopped");
            SimVec3 stood = At(w, A);
            w.Step(5);
            Assert.That((Ordered(w, A), At(w, A)), Is.EqualTo((false, stood)));
            Assert.That(part.Unseens, Is.EqualTo(0), "the answer ends the think");
        }

        // A blade (A) and an enemy that stands still (B) 8.2 m east of it, a wall on the ground at cell (41, 32) between
        // them; engage "bound" with cover rules.
        private static M6World Walled(string more = "") => World(new[] { Blade(A, 38500, 30500), Still(B, 46500, 32500) },
            "{ \"meleeChaseRange\": 12, \"meleeNerve\": true, " + more + Bound + ", " + Pressed
                + ", \"coverRadiusMm\": 9000, \"coverStepMm\": 1000, \"coverDepthMm\": 2000 }",
            Guns(8, MeleeWorld.Spec(radius: 3, maxTargets: 1, engage: 1.8)), plates: false, map: WallOnly());

        // 6 (M4). Cover with cover rules (engage "bound", a wall on the ground at cell (41, 32), the enemy east of it in
        // sight): the pinned unit does not chase, it walks toward a spot with the wall close in front toward where it was
        // pressed from, and stands as soon as it is covered.
        [Test]
        public void APinnedBladeTakesCoverBehindAWall()
        {
            M6World w = Walled();
            w.Step();
            Assert.That(GoesTo(w, A, At(w, B), 0), Is.True, "calm: chasing");
            Press(w, A, 1);
            Assert.That(Level(w, A), Is.EqualTo(AiNerve.Pinned));
            w.Step();
            var order = Order(w, A);
            Assert.That(order.Ordered, Is.True);
            Assert.That(w.Combat.WallWithin(order.Target, At(w, B), SimFix.FromInt(2)), Is.True, "the spot is behind the wall");
            Assert.That(w.Combat.WallWithin(At(w, A), At(w, B), SimFix.FromInt(2)), Is.False, "the unit is not yet");
            Press(w, A, 1);
            w.Step(40);
            Assert.That(Level(w, A), Is.EqualTo(AiNerve.Pinned));
            Assert.That((Ordered(w, A), w.Combat.WallWithin(At(w, A), At(w, B), SimFix.FromInt(2))), Is.EqualTo((false, true)), "standing in cover");
            Assert.That(CombatSystem.Horizontal(At(w, A) - order.Target).Length.Raw, Is.LessThan(2 * M), "at the spot or just short of it");
        }

        // 6 (M4). Hold with the same wall to hide behind: the unit stops where it is and does not walk to the wall.
        [Test]
        public void HoldStandsWithCoverNearby()
        {
            M6World w = Walled();
            Install(w, "engage", new Asked { MeleeAnswer = AiEngage.Hold });
            w.Step();
            Assert.That(GoesTo(w, A, At(w, B), 0), Is.True, "calm: chasing");
            Press(w, A, 1);
            w.Step();
            Assert.That(Ordered(w, A), Is.False, "stopped, not sent to the wall");
        }

        // 6 (M4). Cover and an enemy within the fight range (the enemy stands 8 m away): the unit goes for it instead of
        // standing; at the range counts, 1 mm less does not. Hold stands and Fallback goes back whatever the range.
        [Test]
        public void ACloseEnemyIsFoughtNotHiddenFrom()
        {
            string With(int fight) => "{ \"meleeChaseRange\": 12, \"meleeNerve\": true, \"meleeNerveFightMm\": " + fight + ", " + Pressed + " }";
            var cover = new Asked { MeleeAnswer = AiEngage.Cover };
            M6World near = Duel(With(8000), part: cover);
            Press(near, A, 1);
            near.Step();
            Assert.That(GoesTo(near, A, At(near, B), 0), Is.True, "8 m, range 8 m");
            Assert.That((cover.Melees, cover.Unseens), Is.EqualTo((1, 0)));

            M6World far = Duel(With(7999), part: new Asked { MeleeAnswer = AiEngage.Cover });
            Press(far, A, 1);
            far.Step();
            Assert.That(Ordered(far, A), Is.False, "8 m, range 7.999 m");

            M6World hold = Duel(With(8000), part: new Asked { MeleeAnswer = AiEngage.Hold });
            Press(hold, A, 1);
            hold.Step();
            Assert.That(Ordered(hold, A), Is.False, "Hold stands");

            M6World back = Duel(With(8000), part: new Asked { MeleeAnswer = AiEngage.Fallback });
            Press(back, A, 3);
            back.Step();
            Assert.That(Slots(back).TryFindAll(0, Order(back, A).Target, 1, out _), Is.True, "Fallback goes to its point");
            Assert.That(GoesTo(back, A, At(back, B), 0), Is.False);
        }

        // 6 (M4). With cover rules and a wall to hide behind, an enemy within the fight range is still fought.
        [Test]
        public void ACloseEnemyComesBeforeCover()
        {
            M6World w = Walled("\"meleeNerveFightMm\": 9000, ");
            Press(w, A, 1);
            Assert.That(Level(w, A), Is.EqualTo(AiNerve.Pinned));
            w.Step();
            Assert.That(GoesTo(w, A, At(w, B), 0), Is.True, "the enemy stands 8.2 m away, inside 9 m");
        }

        // 6 (M4). Fallback: the unit goes to the nearest point its team owns and is not asked again in that think (the part's
        // Unseen would make it stand); it does not chase.
        [Test]
        public void FallbackGoesToTheRefugeWithoutAskingAgain()
        {
            var part = new Asked { MeleeAnswer = AiEngage.Fallback, UnseenAnswer = AiEngage.Hold };
            M6World w = Duel(Nerve, part: part);
            Press(w, A, 3);
            w.Step();
            Assert.That((part.Melees, part.Unseens), Is.EqualTo((1, 0)));
            var order = Order(w, A);
            Assert.That(order.Ordered, Is.True);
            Assert.That(Slots(w).TryFindAll(0, order.Target, 1, out _), Is.True, "a slot of the team's own point (index 1)");
            Assert.That(GoesTo(w, A, At(w, B), 0), Is.False);
            long before = At(w, A).X.Raw;
            w.Step(20);
            Assert.That(At(w, A).X.Raw, Is.LessThan(before - M), "walking away from the enemy");
        }

        // 6 (M4). Fallback without a point of its own: back to the base.
        [Test]
        public void FallbackWithoutAPointGoesToTheBase()
        {
            var part = new Asked { MeleeAnswer = AiEngage.Fallback, UnseenAnswer = AiEngage.Hold };
            M6World w = Duel(Nerve, ownPoint: false, part: part);
            Press(w, A, 3);
            w.Step();
            Assert.That(Field(w).TryBase(1, out SimVec3 home, out _), Is.True);
            Assert.That(GoesTo(w, A, home, 0), Is.True);
            Assert.That(part.Unseens, Is.EqualTo(0));
        }

        // Without meleeNerve nothing is asked: a broken unit chases as before.
        [Test]
        public void WithoutTheFieldABrokenUnitStillChases()
        {
            var part = new Asked { MeleeAnswer = AiEngage.Fallback, UnseenAnswer = AiEngage.Fallback };
            M6World w = Duel(NoNerve, part: part);
            Press(w, A, 3);
            Assert.That(Level(w, A), Is.EqualTo(AiNerve.Broken));
            w.Step();
            Assert.That(GoesTo(w, A, At(w, B), 0), Is.True);
            Assert.That((part.Melees, part.Unseens), Is.EqualTo((0, 0)));
        }

        // 7 (M6). The two parts: hold never looks at the nerve; bound sends a broken unit back, a pinned one to cover and
        // lets the rest chase, whatever its move and fire cycle says.
        [Test]
        public void ThePartsAnswerByNerve()
        {
            M6World w = Duel(Nerve);
            AiField field = Field(w);
            SimVec3 enemy = At(w, B);
            AiUnit Unit(AiNerve nerve) => new AiUnit(Id(w, A), 1, At(w, A), nerve, 0);
            var hold = new HoldEngage();
            var bound = new BoundEngage(1, 1000000, 0, 0);
            foreach (AiNerve nerve in new[] { AiNerve.Calm, AiNerve.Pinned, AiNerve.Broken })
                Assert.That(hold.Melee(Unit(nerve), enemy, field), Is.EqualTo(AiEngage.Advance), nerve.ToString());
            Assert.That(bound.Melee(Unit(AiNerve.Calm), enemy, field), Is.EqualTo(AiEngage.Advance), "an advancer share of 0 would hold a gunner");
            Assert.That(bound.Decide(Unit(AiNerve.Calm), enemy, field), Is.EqualTo(AiEngage.Hold));
            Assert.That(bound.Melee(Unit(AiNerve.Pinned), enemy, field), Is.EqualTo(AiEngage.Cover));
            Assert.That(bound.Melee(Unit(AiNerve.Broken), enemy, field), Is.EqualTo(AiEngage.Fallback));
        }

        // 7. With squads the engage part is wrapped: the wrapper hands the melee question to the part inside as it came
        // (the unit, its nerve, the enemy) and returns its answer, whatever it is.
        [Test]
        public void TheSquadPartPassesTheQuestionOn()
        {
            M6World w = Duel("{ \"meleeChaseRange\": 12, \"meleeNerve\": true, " + Pressed + ", \"squadMinSize\": 4, \"squadMaxSize\": 4, "
                + "\"squadSkirmisherPermille\": 300, \"squadRecruitRadiusMm\": 30000, \"squadFormTicks\": 15, \"squadSpreadMinMm\": 0, \"squadSpreadMaxMm\": 0 }");
            int asked = 0;
            foreach (AiEngage answer in new[] { AiEngage.Advance, AiEngage.Hold, AiEngage.Cover, AiEngage.Fallback })
                foreach (AiNerve nerve in new[] { AiNerve.Pinned, AiNerve.Broken })
                {
                    var part = new Asked { MeleeAnswer = answer };
                    IAiEngage wrap = w.System<Squads>().Wrap(part);
                    Assert.That(wrap.Melee(new AiUnit(Id(w, A), 1, At(w, A), nerve, 0), At(w, B), null), Is.EqualTo(answer), answer + " " + nerve);
                    Assert.That((part.Melees, part.Decides, part.Unseens), Is.EqualTo((1, 0, 0)));
                    Assert.That((part.Nerve, part.Enemy, part.Unit), Is.EqualTo((nerve, At(w, B), Id(w, A))));
                    asked++;
                }
            Assert.That(asked, Is.EqualTo(8));
        }

        // 6 and 7, with the parts as content builds them. hold: the pressed unit chases. bound: the pinned unit stands (no
        // cover rules), the broken one goes back to its point.
        [Test]
        public void TheContentPartsDecide()
        {
            M6World held = Duel(Nerve);
            Press(held, A, 3);
            held.Step();
            Assert.That(GoesTo(held, A, At(held, B), 0), Is.True, "hold");

            string bound = "{ \"meleeChaseRange\": 12, \"meleeNerve\": true, " + Bound + ", " + Pressed + " }";
            M6World pinned = Duel(bound);
            Press(pinned, A, 1);
            pinned.Step(3);
            Assert.That((Level(pinned, A), Ordered(pinned, A)), Is.EqualTo((AiNerve.Pinned, false)));

            M6World broken = Duel(bound);
            Press(broken, A, 3);
            broken.Step();
            Assert.That(Level(broken, A), Is.EqualTo(AiNerve.Broken));
            Assert.That(Slots(broken).TryFindAll(0, Order(broken, A).Target, 1, out _), Is.True);

            // The same rules without the field: the broken unit chases.
            M6World off = Duel("{ \"meleeChaseRange\": 12, " + Bound + ", " + Pressed + " }");
            Press(off, A, 3);
            off.Step();
            Assert.That(GoesTo(off, A, At(off, B), 0), Is.True);
        }

        // 8. The digest: the field on folds one more tag; off or false it is the digest as it was.
        [Test]
        public void OnlyTheFieldOnChangesTheDigest()
        {
            ulong none = Digest(Duel(NoNerve));
            ulong off = Digest(Duel("{ \"meleeChaseRange\": 12, \"meleeNerve\": false, " + Pressed + " }"));
            ulong on = Digest(Duel(Nerve));
            Assert.That(off, Is.EqualTo(none));
            Assert.That(on, Is.Not.EqualTo(none));
            var hash = new StateHash(); hash.Add("battle.ai.melee-nerve.v1"); hash.Add(none); hash.Add(0);
            Assert.That(on, Is.EqualTo(hash.Value));
        }

        // 8. The fight range is part of the policy: every value has its own digest.
        [Test]
        public void TheFightRangeChangesTheDigest()
        {
            ulong none = Digest(Duel(NoNerve));
            string With(int fight) => "{ \"meleeChaseRange\": 12, \"meleeNerve\": true, \"meleeNerveFightMm\": " + fight + ", " + Pressed + " }";
            Assert.That(Digest(Duel(With(0))), Is.EqualTo(Digest(Duel(Nerve))));
            ulong four = Digest(Duel(With(4000)));
            Assert.That(four, Is.Not.EqualTo(Digest(Duel(Nerve))));
            Assert.That(four, Is.Not.EqualTo(Digest(Duel(With(4001)))));
            var hash = new StateHash(); hash.Add("battle.ai.melee-nerve.v1"); hash.Add(none); hash.Add(4000);
            Assert.That(four, Is.EqualTo(hash.Value));
        }
    }
}
