using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    internal static class SourceRules
    {
        public static string StripComments(string source)
        {
            // Preserve literals so a comment marker inside a string cannot hide later code.
            return Regex.Replace(source, "@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/", match => match.Value.StartsWith("//", StringComparison.Ordinal) || match.Value.StartsWith("/*", StringComparison.Ordinal) ? new string (' ', match.Length) : match.Value);
        }

        public static IEnumerable<string> ForbiddenTokens(string source)
        {
            string stripped = StripComments(source);
            string[] allowedRandomTypes = {"SimRandom", "RandomStream"};
            foreach (string name in allowedRandomTypes)
                stripped = Regex.Replace(stripped, @"\b" + name + @"\b", "approved_rng_type");
            string[] patterns = {@"\b(UnityEngine|DateTime|Stopwatch|Thread|Task|Guid|Random|float|double|decimal|MathF|Mathf|Vector2|Vector3)\b", @"\b(Environment|Time|Math)\s*\."};
            foreach (string pattern in patterns)
                foreach (Match match in Regex.Matches(stripped, pattern))
                    yield return match.Value;
        }

        public static bool IsImmutableTable(FieldInfo field) => field.IsPrivate && field.IsInitOnly && field.FieldType == typeof(long[]) && field.IsDefined(typeof(ImmutableTableAttribute), false);
        public static bool TableChecksumMatches(FieldInfo field)
        {
            if (!IsImmutableTable(field))
                return false;
            var hash = new StateHash();
            foreach (long value in (long[])field.GetValue(null))
                hash.Add(value);
            return hash.Value == field.GetCustomAttribute<ImmutableTableAttribute>().Checksum;
        }
    }

    public sealed class SourceRuleRegressionTests
    {
        [TestCase("UnityEngine")]
        [TestCase("DateTime")]
        [TestCase("Stopwatch")]
        [TestCase("Thread")]
        [TestCase("Task")]
        [TestCase("Guid")]
        [TestCase("Random")]
        [TestCase("float")]
        [TestCase("double")]
        [TestCase("decimal")]
        [TestCase("MathF")]
        [TestCase("Mathf")]
        [TestCase("Vector2")]
        [TestCase("Vector3")]
        [TestCase("Environment .")]
        [TestCase("Time .")]
        [TestCase("Math .")]
        public void EveryForbiddenTokenHasAnIndependentPositiveControl(string token)
        {
            string name = token.TrimEnd(' ', '.');
            Assert.That(SourceRules.ForbiddenTokens(token + " value;"), Is.Not.Empty);
            Assert.That(SourceRules.ForbiddenTokens("global::System." + token + " value;"), Is.Not.Empty);
            Assert.That(SourceRules.ForbiddenTokens("\"//\"; " + token + " value;"), Is.Not.Empty);
            Assert.That(SourceRules.ForbiddenTokens("\"" + token + "\""), Is.Not.Empty);
            Assert.That(SourceRules.ForbiddenTokens("/* " + token + " */ // " + token), Is.Empty);
            Assert.That(SourceRules.ForbiddenTokens(name + "Suffix _" + name + ";"), Is.Empty);
            if (token.EndsWith(".", StringComparison.Ordinal))
                Assert.That(SourceRules.ForbiddenTokens(name + " /* gap */ \n . Member"), Is.Not.Empty);
        }

        [Test]
        public void CommentsAreRemovedWordBoundariesAndLiteralsCannotHideForbiddenCode()
        {
            Assert.That(SourceRules.ForbiddenTokens("// float Math.Sqrt\n/* double Task Guid */ SimRandom RandomStream; floaty decimalLike;"), Is.Empty);
            Assert.That(SourceRules.ForbiddenTokens("float x; double y; decimal z; Math . Sqrt(1); Environment . Exit(0); Thread t; Task q; Guid id; Random r; Vector3 v;"), Is.EquivalentTo(new[]{"float", "double", "decimal", "Math .", "Environment .", "Thread", "Task", "Guid", "Random", "Vector3"}));
            Assert.That(SourceRules.ForbiddenTokens("string text = \"//\"; float x;"), Is.EquivalentTo(new[]{"float"}));
        }

        private sealed class IncorrectTable
        {
            [ImmutableTable(1)]
            private static readonly long[] Values = {1, 2, 3};
            public static long Value => Values[0];
        }

        [Test]
        public void MarkedTableWithWrongChecksumFailsValidation()
        {
            FieldInfo field = typeof(IncorrectTable).GetField("Values", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(SourceRules.IsImmutableTable(field), Is.True);
            Assert.That(SourceRules.TableChecksumMatches(field), Is.False);
        }
    }
}
