using System;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class Mt158WorldAssemblyTests
    {
        [Test]
        public void UnitHooksRunInOptionOrder()
        {
            var probe = new F2bHookProbe();
            WorldRecipe recipe = F2bWorlds.PlayerMatch()
                .With(probe).With(new F2bThrowingExtension("PlaceUnit"));

            var error = Assert.Throws<TimeoutException>(() => WorldBuilder.Create(recipe, F2bWorlds.Map()));
            Assert.That(error.Message, Is.EqualTo("probe PlaceUnit"));
            Assert.That(probe.Calls.FindAll(call => call.StartsWith("PlaceUnit ", StringComparison.Ordinal)),
                Is.EqualTo(new[] { "PlaceUnit 0" }));
        }
    }
}
