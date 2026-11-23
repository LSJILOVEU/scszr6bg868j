using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.Ml11b1World;

namespace WarCore.Tests
{
    // ML-11b-1 acceptance 9 and 10: the melee chase on a map with floor plates. The enemy is picked and reached by the combat's
    // span (3D), the order carries the enemy's layer, and an enemy the soldier cannot walk to is passed over.
    public sealed class Ml11b1ChaseTests
    {
        private const string Chase = "{ \"meleeChaseRange\": 12 }";
        private const ulong C = 11;

        private static M6World Hunt(double engage, bool stair, params UnitSpawn[] units) =>
            World(units, Chase, Guns(8, MeleeWorld.Spec(radius: 3, maxTargets: 1, engage: engage)), stair);

        private static SimWide Span(M6World w, ulong a, ulong b) => w.Combat.SpanSquared(At(w, a), At(w, b));
        private static SimWide Square(double metres) => new SimVec3(SimFix.Ratio((long)(metres * 1000), 1000), default, default).LengthSquared;

        // 9. An enemy at the floor's edge, 2.2 m away on the plane and 3.72 m away, with a reach of 2.5 m: the soldier is not in
        // reach and is ordered to the enemy on the enemy's layer. The same 2.2 m on the ground is in reach: no order.
        [Test]
        public void UnderTheEnemyIsNotInReach()
        {
            M6World w = Hunt(2.5, true, Blade(A, 38060, 30500), Still(B, 40260, 30500, 1));
            Assert.That(w.Combat.Visible(w.Unit(A), w.Unit(B)), Is.True);
            Assert.That(CombatSystem.Horizontal(At(w, B) - At(w, A)).LengthSquared, Is.LessThan(Square(2.5)));
            Assert.That(Span(w, A, B), Is.GreaterThan(Square(2.5)));
            w.Step();
            Assert.That(GoesTo(w, A, At(w, B), 1), Is.True);
            Assert.That(Failed(w, A), Is.False);

            M6World level = Hunt(2.5, true, Blade(A, 38060, 30500), Still(B, 35860, 30500));
            level.Step(3);
            Assert.That(Ordered(level, A), Is.False, "in reach on the same floor: the soldier stands");
        }

        // 9. From the ground to an enemy at the head of the stair: every order names the enemy's place and layer 1, no walk
        // fails, and the soldier stops on the stair when the enemy is within its reach (1.8 m, 3D) and swings.
        [Test]
        public void AChaserClimbsTheStairAndStrikes()
        {
            M6World w = Hunt(1.8, true, Blade(A, 33500, 31500), Still(B, 40500, 31500, 1));
            SimVec3 enemy = At(w, B);
            int ordered = -1, struck = -1;
            for (int t = 1; t <= 200 && struck < 0; t++)
            {
                w.Step();
                Assert.That(Failed(w, A), Is.False, "tick " + t);
                Assert.That(At(w, B), Is.EqualTo(enemy), "the enemy stands");
                if (Ordered(w, A))
                {
                    Assert.That(GoesTo(w, A, enemy, 1), Is.True, "tick " + t);
                    // One tick's walk (0.125 m up the stair) inside the reach at most: the next think stops the soldier.
                    Assert.That(Span(w, A, B) > Square(1.6), Is.True, "tick " + t);
                    if (ordered < 0) ordered = t;
                }
                foreach (StrikeRecord strike in w.Combat.Strikes)
                    if (strike.Striker.Equals(Id(w, A)) && strike.Kind == StrikeKind.Started) struck = t;
            }
            Assert.That(Span(w, A, B), Is.LessThanOrEqualTo(Square(1.8)));
            // The swing starts after the tick's walk; the next think finds the enemy in reach and stops the soldier.
            w.Step();
            Assert.That(Ordered(w, A), Is.False, "in reach: the soldier stands");
            Assert.That(Layer(w, A), Is.EqualTo(2), "on the stair");
            // This package's baseline: the first think orders the chase; the swing starts on the stair 1.8 m under the enemy.
            Assert.That((ordered, struck), Is.EqualTo((1, 55)));
        }

        // 9. An order to the enemy's X and Z on another layer is not "already chasing": it is replaced.
        [Test]
        public void AChaseOrderOnAnotherLayerIsReplaced()
        {
            M6World w = Hunt(2.5, true, Blade(A, 38060, 30500), Still(B, 40260, 30500, 1));
            SimVec3 below = Flat(40260, 30500);
            Moves(w).Order(Id(w, A), below, 0);
            Assert.That(Order(w, A), Is.EqualTo((true, below, 0)));
            w.Step();
            Assert.That(GoesTo(w, A, At(w, B), 1), Is.True);
        }

        // 12. With fair decisions the chase order carries the layer too.
        [Test]
        public void FairDecisionsCarryTheChasesLayer()
        {
            M6World w = World(new[] { Blade(A, 38060, 30500), Still(B, 40260, 30500, 1) }, "{ \"meleeChaseRange\": 12, \"fairDecisions\": true }",
                Guns(8, MeleeWorld.Spec(radius: 3, maxTargets: 1, engage: 2.5)), fair: true);
            Assert.That(Ai(w).StateVersion, Is.EqualTo(4), "fair decisions are on");
            w.Step();
            Assert.That(GoesTo(w, A, At(w, B), 1), Is.True);
        }

        // 10a. Two enemies in sight: one at the floor's edge 2.2 m away on the plane (3.72 m away), one on the ground 3 m
        // away. The nearer by the span is chased: the one on the ground.
        [Test]
        public void TheNearestEnemyIsTheNearestByTheSpan()
        {
            M6World w = Hunt(1.8, true, Blade(A, 38060, 30500), Still(B, 40260, 30500, 1), Still(C, 35060, 30500));
            Assert.That(w.Combat.Visible(w.Unit(A), w.Unit(B)) && w.Combat.Visible(w.Unit(A), w.Unit(C)), Is.True);
            Assert.That(CombatSystem.Horizontal(At(w, B) - At(w, A)).LengthSquared, Is.LessThan(CombatSystem.Horizontal(At(w, C) - At(w, A)).LengthSquared));
            Assert.That(Span(w, A, C), Is.LessThan(Span(w, A, B)));
            w.Step();
            Assert.That(GoesTo(w, A, At(w, C), 0), Is.True);
        }

        // 10b. Equally far by the span (4 m on the plane and 3 m up, 5 m on the ground): the lower stable id, whichever of the
        // two it is.
        [TestCase(B, C)]
        [TestCase(C, B)]
        public void EquallyNearEnemiesGoToTheLowerId(ulong up, ulong ground)
        {
            M6World w = Hunt(1.8, true, Blade(A, 36500, 30500), Still(up, 40500, 30500, 1), Still(ground, 31500, 30500));
            Assert.That(w.Combat.Visible(w.Unit(A), w.Unit(up)) && w.Combat.Visible(w.Unit(A), w.Unit(ground)), Is.True);
            Assert.That(Span(w, A, up), Is.EqualTo(Span(w, A, ground)));
            bool upFirst = Id(w, up).CompareTo(Id(w, ground)) < 0;
            w.Step();
            Assert.That(GoesTo(w, A, At(w, upFirst ? up : ground), upFirst ? 1 : 0), Is.True);
        }

        // 10c. Without the stair the enemy on the floor cannot be walked to: the soldier chases the farther one on the ground;
        // with nobody else it does not chase and goes to its point.
        [Test]
        public void AnEnemyThatCannotBeWalkedToIsPassedOver()
        {
            M6World w = Hunt(1.8, false, Blade(A, 36500, 30500), Still(B, 40500, 30500, 1), Still(C, 30500, 30500));
            Assert.That(w.Combat.Visible(w.Unit(A), w.Unit(B)), Is.True);
            Assert.That(Span(w, A, B), Is.LessThan(Span(w, A, C)));
            Assert.That(Field(w).Connected(At(w, A), 0, At(w, B), 1), Is.False);
            w.Step();
            Assert.That(GoesTo(w, A, At(w, C), 0), Is.True);

            M6World alone = Hunt(1.8, false, Blade(A, 36500, 30500), Still(B, 40500, 30500, 1));
            for (int t = 1; t <= 30; t++)
            {
                alone.Step();
                Assert.That(Ordered(alone, A) || Failed(alone, A), Is.False, "tick " + t);
            }
            Send(alone, A, 0);
            alone.Step();
            Assert.That(Order(alone, A).Layer == 0 && Slots(alone).TryFindAll(0, Order(alone, A).Target, -1, out _), Is.True, "on to the point");
        }

        // 10c. Three enemies: a nearer one on the floor that cannot be walked to, and two on the ground. The soldier chases the
        // nearer of the two it can walk to, whatever order the three are met in (the keys are dealt every way round).
        [TestCase(10UL, 11UL, 12UL)]
        [TestCase(10UL, 12UL, 11UL)]
        [TestCase(11UL, 10UL, 12UL)]
        [TestCase(11UL, 12UL, 10UL)]
        [TestCase(12UL, 10UL, 11UL)]
        [TestCase(12UL, 11UL, 10UL)]
        public void AnEnemyOutOfReachDoesNotHideAFartherOne(ulong above, ulong middle, ulong far)
        {
            M6World w = Hunt(1.8, false, Blade(A, 36500, 30500), Still(above, 40500, 30500, 1), Still(middle, 30500, 30500), Still(far, 27500, 30500));
            Assert.That(Span(w, A, above), Is.LessThan(Span(w, A, middle)));
            Assert.That(Span(w, A, middle), Is.LessThan(Span(w, A, far)));
            Assert.That(w.Combat.Visible(w.Unit(A), w.Unit(far)), Is.True);
            w.Step();
            Assert.That(GoesTo(w, A, At(w, middle), 0), Is.True);
        }

        // 10. The soldier's own layer is read: on the floor without a stair it chases an enemy on the same floor.
        [Test]
        public void AChaseStartsFromTheSoldiersOwnFloor()
        {
            M6World w = Hunt(1.8, false, Blade(A, 40500, 30500, 1), Still(B, 43500, 33500, 1));
            Assert.That(w.Combat.Visible(w.Unit(A), w.Unit(B)), Is.True);
            Assert.That(Field(w).Connected(At(w, A), 0, At(w, B), 1), Is.False);
            w.Step();
            Assert.That(GoesTo(w, A, At(w, B), 1), Is.True);
        }

        // 10. An enemy in the air (layer 65535) is passed over; the same enemy with its own layer is chased.
        [Test]
        public void AnEnemyInTheAirIsPassedOver()
        {
            M6World w = Hunt(1.8, true, Blade(A, 36500, 30500), Still(B, 31500, 30500));
            Install(w, "layers", new Layers(Part<IUnitLayers>(w, "layers")) { Of = { [w.Unit(B)] = 65535 } });
            Assert.DoesNotThrow(() => w.Step(3));
            Assert.That(Ordered(w, A), Is.False);

            M6World control = Hunt(1.8, true, Blade(A, 36500, 30500), Still(B, 31500, 30500));
            control.Step();
            Assert.That(GoesTo(control, A, At(control, B), 0), Is.True);
        }
    }
}
