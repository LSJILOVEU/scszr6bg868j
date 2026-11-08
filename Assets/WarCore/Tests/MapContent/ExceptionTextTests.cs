using System;
using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed class ExceptionTextTests
    {
        [Test]
        public void Body_StripsOnlyTheRuntimeParamSuffix()
        {
            Assert.That(ExceptionText.Body(new ArgumentException("Bad pack.", "pack")), Is.EqualTo("Bad pack."));
            Assert.That(ExceptionText.Body(new ArgumentNullException("recipe")), Is.EqualTo(new ArgumentNullException().Message));
            Assert.That(ExceptionText.Body(new ArgumentOutOfRangeException("recipe", "Unsupported mover.")), Is.EqualTo("Unsupported mover."));
            Assert.That(ExceptionText.Body(new InvalidOperationException("Wall layers.")), Is.EqualTo("Wall layers."));
        }

        [Test]
        public void Body_KeepsAForgedSuffixWhenParamNameIsMissingOrDifferent()
        {
            const string forged = "Bad pack. (Parameter 'pack')";
            Assert.That(ExceptionText.Body(new ArgumentException(forged)), Is.EqualTo(forged));
            Assert.That(ExceptionText.Body(new ArgumentException(forged, "other")), Is.EqualTo(forged));
            Assert.That(ExceptionText.Body(new ArgumentException("Bad pack.\nParameter name: pack")), Is.EqualTo("Bad pack.\nParameter name: pack"));
        }

        [Test]
        public void Outcome_SeparatesTextFromParamName()
        {
            string real = AssemblyGuardCases.Outcome(() => throw new ArgumentException("Bad pack.", "pack"));
            Assert.That(real, Is.EqualTo("System.ArgumentException|Bad pack.|param=pack"));
            Assert.That(AssemblyGuardCases.Outcome(() => throw new ArgumentException("Bad pack. (Parameter 'pack')")), Is.Not.EqualTo(real));
            Assert.That(AssemblyGuardCases.Outcome(() => throw new ArgumentException("Bad pack.")), Is.EqualTo("System.ArgumentException|Bad pack.|param=null"));
            Assert.That(AssemblyGuardCases.Outcome(() => throw new ArgumentException("Bad pack.", "other")), Is.Not.EqualTo(real));
            Assert.That(AssemblyGuardCases.Outcome(() => throw new InvalidOperationException("Bad pack.")), Is.EqualTo("System.InvalidOperationException|Bad pack."));
        }
    }
}
