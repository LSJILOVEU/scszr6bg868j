using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class Mt154FeatureTableTests
    {
        [Test]
        public void MapWall_BlocksItsCell()
        {
            SimSession session = SimSession.Create(AssemblyWorlds.FmsRecipe(), AssemblyWorlds.FmsMap());
            Assert.That(session.BlockedCell(14, 8), Is.True);
            Assert.That(session.BlockedCell(13, 8), Is.False);
        }

        [Test]
        public void PlayerWithoutPrimaryInput_DoesNotAutoFire()
        {
            SimSession session = SimSession.Create(Wp18TestWorld.Recipe(), AssemblyWorlds.PlainMap());
            session.SetPlayerInput(1, Wp18TestWorld.Aim(false));
            session.StepOnce();
            Assert.That(session.TryUnitStatus(1, out UnitStatus status), Is.True);
            Assert.That(status.Ammo, Is.EqualTo(30));
        }

        [Test]
        public void InactiveStageExtension_DoesNotRegisterCombatComponents()
        {
            var assembly = new WorldAssembly(AssemblyWorlds.FmsRecipe().With(CombatFeature.Instance), null, false)
            {
                Sim = new Simulation(41),
            };
            FeatureTable.RunComponents(assembly);
            Assert.Throws<InvalidOperationException>(() => assembly.Sim.Entities.Resolve<Combatant>());
        }
    }
}
